using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Snooker
{
    public enum SnookerBall
    {
        Cue = 0,
        Red = 1,
        Yellow = 2,
        Green = 3,
        Brown = 4,
        Blue = 5,
        Pink = 6,
        Black = 7,
    }

    /// <summary>What one shot did, gathered by the table and handed to the rules.</summary>
    public struct SnookerShot
    {
        /// <summary>The first object ball the cue ball touched, or null if it hit nothing.</summary>
        public SnookerBall? FirstContact;

        /// <summary>Every ball that went down, cue ball included.</summary>
        public List<SnookerBall> Potted;

        public static SnookerShot Nothing => new SnookerShot { Potted = new List<SnookerBall>() };
    }

    /// <summary>The outcome of a shot, for the table to act on and the HUD to describe.</summary>
    public struct SnookerOutcome
    {
        public int Scored;
        public int FoulPoints;
        public bool IsFoul;
        public bool TurnEnds;
        public string Description;

        /// <summary>Colours that must go back on their spots.</summary>
        public List<SnookerBall> Respot;
    }

    /// <summary>
    /// Snooker scoring and the ball on.
    ///
    /// Kept apart from the table so the rules can be tested without a physics simulation.
    /// The table reports what happened, which ball was struck first and what went down, and
    /// this decides what it was worth and who is at it next.
    ///
    /// Covers the sequence that actually comes up in a frame: reds then a colour while reds
    /// remain, then the colours in ascending order; fouls for hitting the wrong ball first,
    /// hitting nothing, or potting the cue ball; and respotting colours until the reds are
    /// gone. It does not model free balls or snookers required.
    /// </summary>
    public sealed class SnookerFrame
    {
        public const int MinimumFoul = 4;
        public const int TotalReds = 15;

        static readonly SnookerBall[] ColourOrder =
        {
            SnookerBall.Yellow, SnookerBall.Green, SnookerBall.Brown,
            SnookerBall.Blue, SnookerBall.Pink, SnookerBall.Black,
        };

        readonly int[] scores = new int[2];

        public SnookerFrame(int players)
        {
            Players = players < 1 ? 1 : players;
            Reset();
        }

        public int Players { get; }
        public int RedsRemaining { get; private set; }
        public int CurrentPlayer { get; private set; }
        public int Break { get; private set; }

        /// <summary>True when a red has just gone and a colour is owed.</summary>
        public bool ColourIsOn { get; private set; }

        /// <summary>How far through the colours the frame is once the reds are gone.</summary>
        public int ColourIndex { get; private set; }

        public bool IsFinished { get; private set; }

        public int ScoreOf(int player) => scores[player];

        public static int ValueOf(SnookerBall ball) => (int)ball;

        public void Reset()
        {
            scores[0] = 0;
            scores[1] = 0;
            RedsRemaining = TotalReds;
            CurrentPlayer = 0;
            Break = 0;
            ColourIsOn = false;
            ColourIndex = 0;
            IsFinished = false;
        }

        /// <summary>
        /// The ball that must be struck first. Null once a colour is on and any of them
        /// will do, which is the state right after a red goes down.
        /// </summary>
        public SnookerBall? BallOn
        {
            get
            {
                // The colour owed after a red comes first, otherwise potting the last red
                // would put yellow on rather than leaving any colour to choose from.
                if (ColourIsOn) return null;
                if (RedsRemaining > 0) return SnookerBall.Red;
                return ColourIndex < ColourOrder.Length ? ColourOrder[ColourIndex] : (SnookerBall?)null;
            }
        }

        public string BallOnName
        {
            get
            {
                if (ColourIsOn) return "a colour";
                if (RedsRemaining > 0) return "a red";
                return ColourIndex < ColourOrder.Length ? ColourOrder[ColourIndex].ToString() : "nothing";
            }
        }

        /// <summary>
        /// Applies a shot and returns what it was worth.
        ///
        /// A shot is legal when the first contact is the ball on, nothing wrong went down,
        /// and the cue ball stayed on the table. Anything else is a foul worth at least four
        /// to the opponent, or the value of the ball on if that is higher.
        /// </summary>
        public SnookerOutcome Apply(SnookerShot shot)
        {
            var outcome = new SnookerOutcome { Respot = new List<SnookerBall>() };
            List<SnookerBall> potted = shot.Potted ?? new List<SnookerBall>();

            bool cuePotted = potted.Contains(SnookerBall.Cue);
            SnookerBall? on = BallOn;

            // No contact at all is a foul, and so is striking anything but the ball on.
            bool wrongFirst = shot.FirstContact == null
                || (on.HasValue && shot.FirstContact.Value != on.Value)
                // With a colour owed, a red is the one thing that may not be struck.
                || (!on.HasValue && ColourIsOn && shot.FirstContact.Value == SnookerBall.Red);

            int foulValue = MinimumFoul;
            if (on.HasValue) foulValue = System.Math.Max(MinimumFoul, ValueOf(on.Value));
            if (shot.FirstContact.HasValue && shot.FirstContact.Value != SnookerBall.Cue)
            {
                foulValue = System.Math.Max(foulValue, ValueOf(shot.FirstContact.Value));
            }
            foreach (SnookerBall ball in potted)
            {
                if (ball != SnookerBall.Cue) foulValue = System.Math.Max(foulValue, ValueOf(ball));
            }

            if (wrongFirst || cuePotted)
            {
                outcome.IsFoul = true;
                outcome.FoulPoints = foulValue;
                outcome.TurnEnds = true;
                outcome.Description = shot.FirstContact == null
                    ? "Foul, no ball struck"
                    : cuePotted && !wrongFirst
                        ? "Foul, in off"
                        : "Foul, wrong ball";

                // Every colour that went down comes back up after a foul.
                foreach (SnookerBall ball in potted)
                {
                    if (ball != SnookerBall.Cue && ball != SnookerBall.Red) outcome.Respot.Add(ball);
                    else if (ball == SnookerBall.Red) RedsRemaining--;
                }

                Award(1 - CurrentPlayer, foulValue);
                EndTurn();
                CheckFinished();
                return outcome;
            }

            // A legal shot that pots nothing simply passes the table over.
            if (potted.Count == 0)
            {
                outcome.TurnEnds = true;
                outcome.Description = "No pot";
                EndTurn();
                CheckFinished();
                return outcome;
            }

            int scored = 0;
            bool pottedRed = false;
            bool pottedColour = false;
            SnookerBall lastColour = SnookerBall.Yellow;

            foreach (SnookerBall ball in potted)
            {
                if (ball == SnookerBall.Red)
                {
                    scored += 1;
                    RedsRemaining--;
                    pottedRed = true;
                }
                else
                {
                    scored += ValueOf(ball);
                    pottedColour = true;
                    lastColour = ball;
                }
            }

            // Potting a red and a colour with one shot is a foul, but the shapes that reach
            // here are already legal, so this is the ordinary case: one or more reds, or the
            // single colour that was on.
            if (pottedRed)
            {
                ColourIsOn = true;
                outcome.Description = potted.Count > 1 ? potted.Count + " reds" : "Red";
            }
            else if (pottedColour)
            {
                if (RedsRemaining > 0 || ColourIsOn)
                {
                    // A colour taken while reds remain goes back on its spot.
                    ColourIsOn = false;
                    outcome.Respot.Add(lastColour);
                    outcome.Description = lastColour.ToString();
                }
                else
                {
                    // In the colour sequence, each one stays down.
                    ColourIndex++;
                    outcome.Description = lastColour.ToString();
                }
            }

            Award(CurrentPlayer, scored);
            Break += scored;
            outcome.Scored = scored;
            CheckFinished();
            return outcome;
        }

        void Award(int player, int points)
        {
            if (player < 0 || player >= scores.Length) return;
            scores[player] += points;
        }

        void EndTurn()
        {
            Break = 0;
            ColourIsOn = false;
            if (Players > 1) CurrentPlayer = 1 - CurrentPlayer;
        }

        void CheckFinished()
        {
            if (RedsRemaining <= 0 && ColourIndex >= ColourOrder.Length) IsFinished = true;
        }

        /// <summary>
        /// Colours still on the table, so the frame can rebuild after a foul or work out
        /// what to respot. Reds are counted separately.
        /// </summary>
        public IEnumerable<SnookerBall> RemainingColours()
        {
            if (RedsRemaining > 0 || ColourIndex == 0)
            {
                foreach (SnookerBall ball in ColourOrder) yield return ball;
                yield break;
            }

            for (int i = ColourIndex; i < ColourOrder.Length; i++) yield return ColourOrder[i];
        }
    }
}
