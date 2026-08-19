using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Yahtzee;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Game.Yahtzee
{
    /// <summary>
    /// Yahtzee: five dice thrown from a cup into a tray, and a scorecard beside them.
    ///
    /// The dice are genuinely rolled. The cup lifts, swings over the tray and tips, the
    /// dice tumble out and bounce off the walls, and whatever they come to rest showing is
    /// the roll. Nothing decides the numbers in advance and then poses the dice to match,
    /// which means the faces and the score can never disagree.
    ///
    /// The card is a table of thirteen numbers and would be worse in 3D, so it stays as UI,
    /// with both players' cards visible because half the game is watching which boxes your
    /// opponent has left.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class YahtzeeGame : ArcadeGame
    {
        const int DiceCount = 5;
        const int RollsPerTurn = 3;

        // Half again as large as it was, so a throw has room to scatter rather than
        // landing in a heap.
        const float TrayWidth = 5.1f;
        const float TrayDepth = 3.6f;
        const float WallHeight = 0.46f;

        const float CupRadius = 0.38f;
        const float CupHeight = 0.92f;

        /// <summary>How high the cup rides while it is being carried.</summary>
        const float CarryHeight = 1.15f;

        /// <summary>Unity's built in layer 2, which the default raycast mask excludes.</summary>
        const int IgnoreRaycastLayer = 2;

        static readonly Color TrayFelt = new Color(0.11f, 0.19f, 0.14f);
        static readonly Color TrayWall = new Color(0.22f, 0.14f, 0.09f);
        static readonly Color CupColour = new Color(0.26f, 0.16f, 0.10f);
        static readonly Color DieColour = new Color(0.92f, 0.90f, 0.85f);
        static readonly Color PipColour = new Color(0.11f, 0.10f, 0.10f);
        static readonly Color HeldColour = new Color(0.85f, 0.66f, 0.28f);

        readonly YahtzeeScorecard[] cards =
        {
            new YahtzeeScorecard(),
            new YahtzeeScorecard(),
        };

        readonly int[] dice = new int[DiceCount];
        readonly List<YahtzeeDie> dieViews = new List<YahtzeeDie>(DiceCount);

        YahtzeePlayer opponent;
        Transform root;
        Transform cup;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine thinking;
        Coroutine rolling;
        UIDocument document;
        AudioSource rattleSource;
        AudioSource impactSource;

        /// <summary>The dice waiting in the cup, loaded when a roll begins.</summary>
        readonly List<YahtzeeDie> loaded = new List<YahtzeeDie>();

        /// <summary>
        /// The opponent's messages waiting to be played out, in the order they arrived.
        ///
        /// A turn arrives as several of them now and each one takes time to watch, so they
        /// cannot simply be applied as they land: a keep arriving while the dice are still
        /// in the air would move them mid pour.
        /// </summary>
        readonly Queue<YahtzeeMessage> remote = new Queue<YahtzeeMessage>();
        Coroutine pump;
        Coroutine remoteSwirl;

        bool cupHeld;
        bool countedThisRoll;
        Vector3 cupTarget;
        Vector3 lastCupPosition;
        float swirl;

        VisualElement panel;
        readonly Dictionary<YahtzeeCategory, Button>[] boxButtons =
        {
            new Dictionary<YahtzeeCategory, Button>(),
            new Dictionary<YahtzeeCategory, Button>(),
        };
        Label[] totalLabels = new Label[2];
        Button rollButton;

        int seat;
        int rollsUsed;
        bool rolledThisTurn;

        public override ArcadeGameId Id => ArcadeGameId.Yahtzee;

        public override bool IsFinished => cards[0].IsComplete && cards[1].IsComplete;

        public override string DebugState =>
            "seat" + seat + " rolls" + rollsUsed + " dice" + string.Join("", System.Array.ConvertAll(dice, d => d.ToString()));

        public override string StatusText
        {
            get
            {
                if (IsFinished)
                {
                    int a = cards[0].Total;
                    int b = cards[1].Total;
                    if (a == b) return "A tie at " + a;
                    return (a > b ? "Player 1 wins " : "Player 2 wins ")
                        + Mathf.Max(a, b) + " to " + Mathf.Min(a, b);
                }

                string who = "Player " + (seat + 1);
                if (cupHeld) return who + ", swirl the cup then click to throw";
                if (rolling != null) return who + " rolling...";
                if (!rolledThisTurn) return who + " to roll, or pick up the cup";
                if (rollsUsed >= RollsPerTurn) return who + ", pick a box";
                return who + ", roll " + rollsUsed + " of " + RollsPerTurn + ". Click a die to keep it";
            }
        }

        bool FirstSeatToPlay => seat == 0;

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
            document = GetComponent<UIDocument>();
        }

        protected override void OnBegin()
        {
            // Play Again goes straight here rather than through End, and the table is about
            // to be rebuilt underneath anything still animating against the old one.
            StopWork();

            opponent = new YahtzeePlayer(new System.Random());

            cards[0].Reset();
            cards[1].Reset();
            seat = 0;
            rollsUsed = 0;
            rolledThisTurn = false;
            for (int i = 0; i < DiceCount; i++) dice[i] = 1;

            ArcadeAudio.EnsureListener();
            BuildTable();
            BuildCard();

            if (cameraRig != null)
            {
                cameraRig.OverrideFraming(new BoardFraming
                {
                    // Over the tray and steep, because the number that matters is the one
                    // on top and a low angle hides it behind the near face.
                    Focus = new Vector3(-1.4f, 0f, -0.3f),
                    Height = 9.6f,
                    Distance = 4.4f,
                    Pitch = 64f,
                    Fov = 42f,
                    HalfExtent = 5.4f,
                });
            }

            RefreshCard();
            BeginTurn();
        }

        public override void End()
        {
            StopWork();
            if (cameraRig != null) cameraRig.ClearFramingOverride();
            DestroyDice();
            if (root != null)
            {
                Destroy(root.gameObject);
                root = null;
            }
            if (document != null && document.rootVisualElement != null) document.rootVisualElement.Clear();
        }

        public override void Restart()
        {
            Begin(Setup);
        }

        void StopWork()
        {
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
            if (rolling != null) { StopCoroutine(rolling); rolling = null; }
            if (pump != null) { StopCoroutine(pump); pump = null; }
            StopIdleSwirl();
            remote.Clear();
            if (rattleSource != null) rattleSource.Stop();
            cupHeld = false;
            loaded.Clear();
        }

        // Turn flow -----------------------------------------------------------------

        void BeginTurn()
        {
            rollsUsed = 0;
            rolledThisTurn = false;
            foreach (YahtzeeDie die in dieViews) die.SetHeld(false);

            // Nothing is kept at the start of a turn, so anything still up on the rail from
            // the last one comes back down.
            LiftHeldDice();

            Raise();
            RefreshCard();

            if (IsFinished) return;

            if (Setup.Mode == GameMode.SinglePlayer && !LocalControls(FirstSeatToPlay))
            {
                thinking = StartCoroutine(OpponentTurn());
            }
        }

        /// <summary>The Roll button: loads the cup and throws it, all in one.</summary>
        void Roll()
        {
            if (!CanRoll()) return;
            LoadCup();

            // Sent before the throw rather than after it. What the dice land on is not
            // known until they stop, so the opponent cannot be told the outcome yet, but
            // they can be shown the cup going up right now instead of watching a still
            // table for the two seconds a roll takes.
            if (ShouldRelay) RaiseMovePlayed(YahtzeeWire.CupLifted());

            rolling = StartCoroutine(RollRoutine());
        }

        bool CanRoll() =>
            rollsUsed < RollsPerTurn && rolling == null && thinking == null && !cupHeld
            && !IsFinished && LocalControls(FirstSeatToPlay);

        /// <summary>
        /// Gathers the dice that are going to be thrown into the cup.
        ///
        /// This is what stops a throw landing on dice still lying in the tray. They are
        /// released over the course of the tip, so without collecting them first the ones
        /// still waiting are sitting exactly where the first ones out are about to land.
        /// </summary>
        void LoadCup()
        {
            loaded.Clear();
            foreach (YahtzeeDie die in dieViews)
            {
                if (die.Held) continue;
                loaded.Add(die);
            }

            for (int i = 0; i < loaded.Count; i++)
            {
                // Stacked loosely down the inside of the cup.
                float angle = i * 2.4f;
                loaded[i].StowIn(cup, new Vector3(
                    Mathf.Cos(angle) * 0.11f,
                    -0.3f + i * 0.085f,
                    Mathf.Sin(angle) * 0.11f));
            }
        }

        /// <summary>
        /// The throw. The cup lifts, swings over the tray and tips; the dice leave its mouth
        /// with the spin it gave them and are left to settle on their own.
        /// </summary>
        IEnumerator RollRoutine()
        {
            // The hand throw counts its own roll before it starts, so it is only counted
            // here for the ones that come straight from the button.
            if (!countedThisRoll)
            {
                rollsUsed++;
                rolledThisTurn = true;
            }
            countedThisRoll = false;
            Raise();
            RefreshCard();

            var loose = new List<YahtzeeDie>(loaded);

            Vector3 rest = CupRest();
            Vector3 over = new Vector3(TrayCentre().x - 0.7f, 1.55f, TrayCentre().z - 0.15f);

            // Lift and carry the cup over the tray.
            yield return MoveCup(rest, over, Quaternion.identity, Quaternion.Euler(0f, 0f, -34f), 0.32f);

            // Tip it, and let the dice go part way through the tip so they pour rather than
            // all appearing at the moment it finishes.
            Quaternion tipped = Quaternion.Euler(0f, 0f, -132f);
            float duration = 0.42f;
            float elapsed = 0f;
            int released = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cup.rotation = Quaternion.Slerp(Quaternion.Euler(0f, 0f, -34f), tipped, t * t);

                int shouldHaveReleased = Mathf.FloorToInt(Mathf.InverseLerp(0.25f, 0.85f, t) * loose.Count);
                while (released < shouldHaveReleased && released < loose.Count)
                {
                    Vector3 mouth = cup.position + cup.up * (CupHeight * 0.72f);
                    Vector3 velocity = new Vector3(1.5f, -1.4f, 0f)
                        + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.9f, 0.9f));
                    loose[released].Throw(mouth, velocity);
                    released++;
                }
                yield return null;
            }

            // Anything the loop did not get to.
            while (released < loose.Count)
            {
                loose[released].Throw(cup.position + cup.up * (CupHeight * 0.72f),
                    new Vector3(1.5f, -1.4f, 0f));
                released++;
            }

            yield return MoveCup(over, rest, tipped, Quaternion.identity, 0.3f);

            yield return Settle(loose);
        }

        /// <summary>Waits for the thrown dice to stop, then reads what they are showing.</summary>
        IEnumerator Settle(List<YahtzeeDie> loose)
        {
            // Let them tumble and stop.
            float deadline = Time.time + 6f;
            yield return new WaitForSeconds(0.35f);
            while (Time.time < deadline)
            {
                bool moving = false;
                foreach (YahtzeeDie die in loose)
                {
                    if (!die.IsAtRest) { moving = true; break; }
                }
                if (!moving) break;
                yield return new WaitForSeconds(0.08f);
            }

            // Frozen before they are read, not merely stopped. Whatever they are showing
            // now is the roll, and nothing is allowed to change it afterwards.
            foreach (YahtzeeDie die in loose) die.Rest();

            // The dice are the source of truth: read what they are actually showing.
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dice[i] = dieViews[i].Value;

            if (ShouldRelay) RelayThrow();

            loaded.Clear();
            rolling = null;
            Raise();
            RefreshCard();
        }

        /// <summary>
        /// The hand throw: the cup is already where the player left it, so it only has to
        /// tip from there.
        /// </summary>
        IEnumerator PourFromHand()
        {
            var loose = new List<YahtzeeDie>(loaded);

            // Last guard against pouring outside the tray. The pointer is clamped while
            // swirling, but nothing else guarantees where the cup ended up.
            Vector3 centre = TrayCentre();
            Vector3 safe = cup.position;
            safe.x = Mathf.Clamp(safe.x, centre.x - TrayWidth * 0.35f, centre.x + TrayWidth * 0.35f);
            safe.z = Mathf.Clamp(safe.z, centre.z - TrayDepth * 0.35f, centre.z + TrayDepth * 0.35f);
            safe.y = Mathf.Max(safe.y, CarryHeight);
            cup.position = safe;

            Quaternion upright = cup.rotation;
            Quaternion tipped = Quaternion.Euler(0f, 0f, -128f);
            float duration = 0.4f;
            float elapsed = 0f;
            int released = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cup.rotation = Quaternion.Slerp(upright, tipped, t * t);

                int shouldHaveReleased =
                    Mathf.FloorToInt(Mathf.InverseLerp(0.2f, 0.8f, t) * loose.Count);
                while (released < shouldHaveReleased && released < loose.Count)
                {
                    Release(loose[released]);
                    released++;
                }
                yield return null;
            }

            while (released < loose.Count)
            {
                Release(loose[released]);
                released++;
            }

            yield return MoveCup(cup.position, CupRest(), cup.rotation, Quaternion.identity, 0.32f);
            yield return Settle(loose);
        }

        void Release(YahtzeeDie die)
        {
            // Clear of the rim. Half the cup's height is exactly the rim, so anything less
            // than that spawns the die inside the vessel it is meant to be leaving.
            Vector3 mouth = cup.position + cup.up * (CupHeight * 0.72f);
            Vector3 velocity = cup.up * -1.4f
                + new Vector3(Random.Range(-0.5f, 0.5f), -0.5f, Random.Range(-0.5f, 0.5f));
            die.Throw(mouth, velocity);
        }

        /// <summary>The computer throws by the same route, without the CanRoll gate.</summary>
        void OpponentRoll()
        {
            if (rolling != null) return;
            LoadCup();
            rolling = StartCoroutine(RollRoutine());
        }

        IEnumerator MoveCup(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                cup.position = Vector3.Lerp(from, to, eased);
                cup.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);
                yield return null;
            }
            cup.position = to;
            cup.rotation = toRotation;
        }

        /// <summary>
        /// Whether what the local player is doing needs telling the opponent about.
        ///
        /// Whose turn it is matters as well as the mode: everything that relays runs on the
        /// player doing it, so a message going out while the other seat is live would be
        /// this client narrating a turn it is not taking. That is also what keeps the
        /// computer opponent and the hot seat silent.
        /// </summary>
        bool ShouldRelay => Setup.Mode == GameMode.Online && LocalControls(FirstSeatToPlay);

        /// <summary>
        /// Tells the opponent where the dice came to rest and what they are showing.
        ///
        /// Both, because neither can be worked out from the other. PhysX is not
        /// deterministic across platforms, so a throw replayed on the other machine would
        /// scatter differently and land on different numbers. The thrower's tray is the
        /// real one and the other end is told the outcome.
        /// </summary>
        void RelayThrow()
        {
            var landed = new YahtzeeLandedDie[DiceCount];
            for (int i = 0; i < DiceCount; i++)
            {
                Vector3 at = i < dieViews.Count ? dieViews[i].transform.position : Vector3.zero;
                landed[i] = new YahtzeeLandedDie { X = at.x, Z = at.z, Value = dice[i] };
            }
            RaiseMovePlayed(YahtzeeWire.Thrown(landed));
        }

        void RelayKeeps()
        {
            var held = new bool[DiceCount];
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++) held[i] = dieViews[i].Held;
            RaiseMovePlayed(YahtzeeWire.Kept(held));
        }

        void Score(YahtzeeCategory category, bool local)
        {
            if (!cards[seat].Fill(category, dice)) return;

            if (local && Setup.Mode == GameMode.Online)
            {
                RaiseMovePlayed(YahtzeeWire.Scored(category, dice));
            }

            seat = 1 - seat;
            Raise();
            BeginTurn();
        }

        /// <summary>
        /// Takes one message from the opponent and lines it up to be played out.
        ///
        /// A turn used to arrive as a single message at the moment a box was filled, which
        /// meant the other player watched a still table and then saw the result appear.
        /// It now arrives as four kinds of message covering the whole turn, so the table
        /// moves while the opponent is playing rather than only after they have finished.
        /// </summary>
        public override bool ApplyRemoteMove(string encoded)
        {
            if (!YahtzeeWire.TryParse(encoded, out YahtzeeMessage message)) return false;

            remote.Enqueue(message);
            if (pump == null) pump = StartCoroutine(PlayRemote());
            return true;
        }

        IEnumerator PlayRemote()
        {
            while (remote.Count > 0)
            {
                YahtzeeMessage message = remote.Dequeue();
                switch (message.Kind)
                {
                    case YahtzeeMessageKind.CupLifted:
                        yield return RemoteLift();
                        break;

                    case YahtzeeMessageKind.Thrown:
                        // Borrowing the local roll's flag, because it means exactly the same
                        // thing here: the table is mid throw and nothing else may touch it.
                        rolling = StartCoroutine(RemoteThrow(message.Landed));
                        while (rolling != null) yield return null;
                        break;

                    case YahtzeeMessageKind.Kept:
                        RemoteKeep(message.Held);
                        break;

                    case YahtzeeMessageKind.Scored:
                        RemoteScore(message.Category, message.Dice);
                        break;
                }
            }
            pump = null;
        }

        /// <summary>
        /// The opponent has picked the cup up.
        ///
        /// Their swirling is not streamed. It is a pointer moving every frame, and an RPC a
        /// frame to animate a cup would cost more than the rest of the game put together
        /// for something nobody is reading precisely. What matters is that the table is
        /// visibly busy, so the cup goes up and shakes on its own until the throw arrives.
        /// </summary>
        IEnumerator RemoteLift()
        {
            LoadCup();

            if (rattleSource != null)
            {
                rattleSource.volume = 0.55f;
                rattleSource.pitch = 1f;
                rattleSource.Play();
            }

            Vector3 over = TrayCentre() + new Vector3(0f, CarryHeight, 0f);
            yield return MoveCup(cup.position, over, cup.rotation, Quaternion.identity, 0.3f);

            StopIdleSwirl();
            remoteSwirl = StartCoroutine(IdleSwirl(over));
        }

        /// <summary>A cup being shaken, until the throw comes.</summary>
        IEnumerator IdleSwirl(Vector3 centre)
        {
            float t = 0f;
            while (true)
            {
                t += Time.deltaTime;
                cup.position = centre + new Vector3(
                    Mathf.Sin(t * 5.4f) * 0.34f, 0f, Mathf.Cos(t * 4.1f) * 0.26f);
                cup.rotation = Quaternion.Euler(
                    Mathf.Sin(t * 4.1f) * 13f, 0f, Mathf.Sin(t * 5.4f) * -13f);
                yield return null;
            }
        }

        void StopIdleSwirl()
        {
            if (remoteSwirl == null) return;
            StopCoroutine(remoteSwirl);
            remoteSwirl = null;
        }

        /// <summary>The opponent's throw, poured out here and landed on their numbers.</summary>
        IEnumerator RemoteThrow(YahtzeeLandedDie[] landed)
        {
            // A throw made with the Roll button arrives with no lift in front of it.
            if (remoteSwirl == null) yield return RemoteLift();
            StopIdleSwirl();
            if (rattleSource != null) rattleSource.Stop();

            rollsUsed = Mathf.Min(rollsUsed + 1, RollsPerTurn);
            rolledThisTurn = true;
            Raise();
            RefreshCard();

            var loose = new List<YahtzeeDie>(loaded);

            Quaternion upright = cup.rotation;
            Quaternion tipped = Quaternion.Euler(0f, 0f, -128f);
            float duration = 0.4f;
            float elapsed = 0f;
            int released = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cup.rotation = Quaternion.Slerp(upright, tipped, t * t);

                int shouldHaveReleased =
                    Mathf.FloorToInt(Mathf.InverseLerp(0.2f, 0.8f, t) * loose.Count);
                while (released < shouldHaveReleased && released < loose.Count)
                {
                    Release(loose[released]);
                    released++;
                }
                yield return null;
            }

            while (released < loose.Count)
            {
                Release(loose[released]);
                released++;
            }

            yield return MoveCup(cup.position, CupRest(), cup.rotation, Quaternion.identity, 0.32f);

            loaded.Clear();
            yield return LandOn(loose, landed);

            rolling = null;
        }

        /// <summary>
        /// Lets the opponent's dice tumble for as long as they still look like they are
        /// tumbling, then eases them onto the places and numbers they landed on over there.
        ///
        /// The alternative is letting them come fully to rest on whatever the local physics
        /// decides and correcting afterwards, which is the same repositioning done a second
        /// later and in plain view. Catching them while they are still slowing hides it in
        /// the motion they already have.
        /// </summary>
        IEnumerator LandOn(List<YahtzeeDie> loose, YahtzeeLandedDie[] landed)
        {
            float deadline = Time.time + 1.6f;
            yield return new WaitForSeconds(0.45f);
            while (Time.time < deadline)
            {
                bool moving = false;
                foreach (YahtzeeDie die in loose)
                {
                    if (!die.IsAtRest) { moving = true; break; }
                }
                if (!moving) break;
                yield return new WaitForSeconds(0.08f);
            }

            var glides = new List<Coroutine>();
            for (int i = 0; i < DiceCount && i < dieViews.Count && i < landed.Length; i++)
            {
                dice[i] = landed[i].Value;

                // A kept die never left the rail, and its relayed position is where it is
                // sitting over there rather than anywhere in the tray.
                if (dieViews[i].Held) continue;

                var to = new Vector3(landed[i].X, YahtzeeDie.Size * 0.5f, landed[i].Z);
                glides.Add(StartCoroutine(dieViews[i].GlideTo(to, landed[i].Value, 0.28f)));
            }
            foreach (Coroutine glide in glides) yield return glide;

            Raise();
            RefreshCard();
        }

        void RemoteKeep(bool[] held)
        {
            for (int i = 0; i < DiceCount && i < dieViews.Count && i < held.Length; i++)
            {
                dieViews[i].SetHeld(held[i]);
            }
            LiftHeldDice();
            Raise();
        }

        void RemoteScore(YahtzeeCategory category, int[] values)
        {
            for (int i = 0; i < DiceCount && i < values.Length && i < dieViews.Count; i++)
            {
                dice[i] = values[i];

                // The throw already put this die on this number in almost every case, and
                // re-posing it would tidy away the scatter the player just watched land.
                // Only a die showing something else is worth touching.
                if (dieViews[i].Value == values[i]) continue;
                dieViews[i].Park(dieViews[i].transform.position);
                dieViews[i].transform.rotation = PippedDie.RotationShowing(values[i]);
            }

            if (cards[seat].IsFilled(category))
            {
                // Both sides run the same card, so a box that is already full here means
                // the two games have drifted apart. Worth saying out loud.
                Debug.LogError("YahtzeeGame: the opponent filled " + category
                    + ", which is already taken here. Local state: " + DebugState);
                return;
            }

            Score(category, false);
        }

        /// <summary>
        /// The opponent has gone and this client has both seats back.
        ///
        /// Anything of theirs still playing out has to be put down first. A throw is stopped
        /// halfway otherwise, and the dice spend that half inside the cup with their
        /// renderers off, so the player inherits a table with no dice on it.
        /// </summary>
        public override void ReleaseOnlineSide()
        {
            base.ReleaseOnlineSide();

            if (pump != null) { StopCoroutine(pump); pump = null; }
            if (rolling != null) { StopCoroutine(rolling); rolling = null; }
            StopIdleSwirl();
            remote.Clear();
            loaded.Clear();

            if (rattleSource != null) rattleSource.Stop();
            if (cup != null)
            {
                cup.position = CupRest();
                cup.rotation = Quaternion.identity;
            }

            ShowParkedDice();
            Raise();
            RefreshCard();
        }

        /// <summary>Places every die flat, showing the value recorded for it.</summary>
        void ShowParkedDice()
        {
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++)
            {
                dieViews[i].Park(DieRestPosition(i));
                dieViews[i].transform.rotation = PippedDie.RotationShowing(dice[i]);
            }
        }

        IEnumerator OpponentTurn()
        {
            yield return new WaitForSeconds(0.5f);

            OpponentRoll();
            while (rolling != null) yield return null;

            for (int reroll = 0; reroll < RollsPerTurn - 1; reroll++)
            {
                yield return new WaitForSeconds(0.5f);

                bool[] keep = opponent.ChooseKeeps(dice, cards[seat], Setup.Difficulty);
                for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dieViews[i].SetHeld(keep[i]);
                LiftHeldDice();

                yield return new WaitForSeconds(0.45f);
                OpponentRoll();
                while (rolling != null) yield return null;
            }

            yield return new WaitForSeconds(0.7f);
            YahtzeeCategory choice = opponent.ChooseCategory(dice, cards[seat], Setup.Difficulty);
            thinking = null;
            Score(choice, false);
        }

        /// <summary>
        /// Held dice are lifted onto the rail at the back of the tray, out of the way of the
        /// next throw. That is also what stops a thrown die knocking a kept one over.
        /// </summary>
        /// <summary>
        /// Moves dice between the tray and the rail to match what is being kept.
        ///
        /// Both directions, because keeping a die and then changing your mind about it is
        /// perfectly ordinary, and a die that goes up but never comes back leaves the rail
        /// disagreeing with the game about what is being kept.
        /// </summary>
        void LiftHeldDice()
        {
            int slot = 0;
            foreach (YahtzeeDie die in dieViews)
            {
                if (die.Held) die.LiftToRail(RailPosition(slot++));
            }

            foreach (YahtzeeDie die in dieViews)
            {
                if (die.Held || !die.OnRail) continue;
                // A later roll may have dropped a die where this one was lying, so it goes
                // back near its old place rather than exactly on top of whatever is there.
                die.ReturnToTray(FreeTraySpot(die.TrayPosition, die));
            }
        }

        /// <summary>
        /// The wanted spot if nothing is in it, otherwise the nearest clear one, searched
        /// outwards. Dice are kinematic while they sit in the tray, so an overlap would
        /// simply stay overlapping rather than being pushed apart.
        /// </summary>
        Vector3 FreeTraySpot(Vector3 wanted, YahtzeeDie ignore)
        {
            Vector3 centre = TrayCentre();
            float halfW = TrayWidth * 0.5f - YahtzeeDie.Size;
            float halfD = TrayDepth * 0.5f - YahtzeeDie.Size;

            wanted.x = Mathf.Clamp(wanted.x, centre.x - halfW, centre.x + halfW);
            wanted.z = Mathf.Clamp(wanted.z, centre.z - halfD, centre.z + halfD);
            wanted.y = YahtzeeDie.Size * 0.6f;

            if (IsClear(wanted, ignore)) return wanted;

            for (float radius = YahtzeeDie.Size; radius < TrayWidth * 0.5f; radius += YahtzeeDie.Size * 0.8f)
            {
                for (int step = 0; step < 8; step++)
                {
                    float angle = step / 8f * Mathf.PI * 2f;
                    Vector3 candidate = wanted
                        + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);

                    candidate.x = Mathf.Clamp(candidate.x, centre.x - halfW, centre.x + halfW);
                    candidate.z = Mathf.Clamp(candidate.z, centre.z - halfD, centre.z + halfD);
                    if (IsClear(candidate, ignore)) return candidate;
                }
            }
            return wanted;
        }

        bool IsClear(Vector3 position, YahtzeeDie ignore)
        {
            foreach (YahtzeeDie die in dieViews)
            {
                if (die == ignore || die.OnRail) continue;
                if ((die.transform.position - position).sqrMagnitude
                    < YahtzeeDie.Size * YahtzeeDie.Size * 1.6f)
                {
                    return false;
                }
            }
            return true;
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || IsFinished) return;

            if (cupHeld)
            {
                SwirlCup();
                if (WasPressedThisFrame()) Pour();
                return;
            }

            if (thinking != null || rolling != null) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!WasPressedThisFrame()) return;

            // Picking the cup up is how you roll by hand.
            if (CanRoll() && CupUnderPointer())
            {
                PickUpCup();
                return;
            }

            if (!rolledThisTurn || rollsUsed >= RollsPerTurn) return;

            YahtzeeDie die = DieUnderPointer();
            if (die == null) return;

            die.SetHeld(!die.Held);
            LiftHeldDice();
            if (ShouldRelay) RelayKeeps();
            Raise();
        }

        void PickUpCup()
        {
            LoadCup();
            cupHeld = true;

            // Straight over the tray, not merely lifted from where it was resting. The cup
            // sits beside the tray, so a player who picks it up and clicks again without
            // moving the mouse would otherwise tip five dice onto the floor.
            Vector3 over = TrayCentre() + new Vector3(0f, CarryHeight, 0f);
            cup.position = over;
            cupTarget = over;
            lastCupPosition = over;
            swirl = 0f;

            if (rattleSource != null)
            {
                rattleSource.volume = 0f;
                rattleSource.Play();
            }

            if (ShouldRelay) RaiseMovePlayed(YahtzeeWire.CupLifted());
            Raise();
        }

        /// <summary>
        /// Follows the pointer while the cup is held, and turns how fast it is being moved
        /// into how loudly the dice rattle. A cup that made the same noise however it was
        /// moved would be worse than a silent one.
        /// </summary>
        void SwirlCup()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return;

            // Move on a plane at the height the cup is carried at.
            Ray ray = targetCamera.ScreenPointToRay(pointer);
            var plane = new Plane(Vector3.up, new Vector3(0f, CarryHeight, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                Vector3 wanted = ray.GetPoint(distance);
                // Kept over the tray, so the dice cannot be poured onto the floor.
                Vector3 centre = TrayCentre();
                wanted.x = Mathf.Clamp(wanted.x, centre.x - TrayWidth * 0.4f, centre.x + TrayWidth * 0.4f);
                wanted.z = Mathf.Clamp(wanted.z, centre.z - TrayDepth * 0.4f, centre.z + TrayDepth * 0.4f);
                wanted.y = CarryHeight;
                cupTarget = wanted;
            }

            cup.position = Vector3.Lerp(cup.position, cupTarget, 1f - Mathf.Exp(-14f * Time.deltaTime));

            // Tilt into the movement, the way a hand carrying a cup would.
            Vector3 travel = cup.position - lastCupPosition;
            float speed = travel.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            lastCupPosition = cup.position;

            swirl = Mathf.Lerp(swirl, Mathf.Clamp01(speed / 6f), 1f - Mathf.Exp(-9f * Time.deltaTime));

            Vector3 lean = new Vector3(travel.z, 0f, -travel.x) * 26f;
            cup.rotation = Quaternion.Slerp(cup.rotation,
                Quaternion.Euler(Mathf.Clamp(lean.x, -22f, 22f), 0f, Mathf.Clamp(lean.z, -22f, 22f)),
                1f - Mathf.Exp(-8f * Time.deltaTime));

            if (rattleSource != null)
            {
                rattleSource.volume = Mathf.Clamp01(swirl) * 0.85f;
                rattleSource.pitch = 0.85f + swirl * 0.5f;
            }
        }

        void Pour()
        {
            cupHeld = false;
            if (rattleSource != null) rattleSource.Stop();

            rollsUsed++;
            rolledThisTurn = true;
            countedThisRoll = true;
            Raise();
            RefreshCard();

            rolling = StartCoroutine(PourFromHand());
        }

        bool CupUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || cup == null) return false;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return false;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            return Physics.Raycast(ray, out RaycastHit hit, 200f)
                && hit.collider.transform.IsChildOf(cup);
        }

        static bool WasPressedThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        YahtzeeDie DieUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return null;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return null;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            return Physics.Raycast(ray, out RaycastHit hit, 200f)
                ? hit.collider.GetComponentInParent<YahtzeeDie>()
                : null;
        }

        // Presentation --------------------------------------------------------------

        static Vector3 TrayCentre() => new Vector3(-1.5f, 0f, 0f);

        /// <summary>
        /// The cup waits to the left of the tray. The scorecard occupies the right of the
        /// screen, and a cup over there is simply behind it.
        /// </summary>
        Vector3 CupRest() =>
            new Vector3(TrayCentre().x - TrayWidth * 0.5f - 0.75f, 0.5f, -0.4f);

        static Vector3 DieRestPosition(int index) =>
            TrayCentre() + new Vector3(-1.3f + index * 0.65f, 0.2f, 0.4f);

        static Vector3 RailPosition(int slot) =>
            TrayCentre() + new Vector3(-1.3f + slot * 0.65f, 0.2f, -TrayDepth * 0.5f - 0.5f);

        void BuildTable()
        {
            DestroyDice();
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Yahtzee Table");
            go.transform.SetParent(transform, false);
            root = go.transform;

            Vector3 centre = TrayCentre();

            // The tray: a felt floor inside four walls, which is what the dice bounce off.
            AddBox("Felt", centre + new Vector3(0f, -0.06f, 0f),
                new Vector3(TrayWidth, 0.12f, TrayDepth), TrayFelt, true, 0.04f);

            float halfW = TrayWidth * 0.5f;
            float halfD = TrayDepth * 0.5f;
            AddBox("Wall_Left", centre + new Vector3(-halfW - 0.12f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.24f, WallHeight, TrayDepth + 0.48f), TrayWall, true, 0.08f);
            AddBox("Wall_Right", centre + new Vector3(halfW + 0.12f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.24f, WallHeight, TrayDepth + 0.48f), TrayWall, true, 0.08f);
            AddBox("Wall_Far", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, halfD + 0.12f),
                new Vector3(TrayWidth, WallHeight, 0.24f), TrayWall, true, 0.08f);
            AddBox("Wall_Near", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, -halfD - 0.12f),
                new Vector3(TrayWidth, WallHeight, 0.24f), TrayWall, true, 0.08f);

            // The rail behind the tray, where kept dice sit out of the throw.
            AddBox("Rail", centre + new Vector3(0f, -0.06f, -halfD - 0.5f),
                new Vector3(TrayWidth, 0.12f, 0.68f), TrayWall, false, 0.05f);

            // Invisible walls well above the wooden ones. A die thrown hard clears a rail
            // it can bounce as high as, and a die that leaves the tray is gone: it lands on
            // the floor showing a number nobody can see and the roll is lost.
            const float containment = 2.4f;
            AddContainment("Contain_Left", centre + new Vector3(-halfW - 0.12f, containment * 0.4f, 0f),
                new Vector3(0.24f, containment, TrayDepth + 0.48f));
            AddContainment("Contain_Right", centre + new Vector3(halfW + 0.12f, containment * 0.4f, 0f),
                new Vector3(0.24f, containment, TrayDepth + 0.48f));
            AddContainment("Contain_Far", centre + new Vector3(0f, containment * 0.4f, halfD + 0.12f),
                new Vector3(TrayWidth, containment, 0.24f));
            AddContainment("Contain_Near", centre + new Vector3(0f, containment * 0.4f, -halfD - 0.12f),
                new Vector3(TrayWidth, containment, 0.24f));

            impactSource = ArcadeAudio.AddSource(go, ArcadeAudio.Knock(), false);
            impactSource.volume = 1f;

            BuildCup();

            for (int i = 0; i < DiceCount; i++)
            {
                YahtzeeDie die = YahtzeeDie.Create(root, DieColour, PipColour, HeldColour);
                die.Struck += OnDieStruck;
                die.Park(DieRestPosition(i));
                dieViews.Add(die);
            }
            ShowParkedDice();
        }

        /// <summary>
        /// The cup, built as a ring of staves around an open mouth. A solid cylinder would
        /// read as a mug; the gap down the middle is what makes it look like something the
        /// dice come out of.
        /// </summary>
        void BuildCup()
        {
            var go = new GameObject("Cup");
            go.transform.SetParent(root, false);
            cup = go.transform;
            cup.position = CupRest();

            var shell = new GameObject("Shell");
            shell.transform.SetParent(cup, false);
            shell.AddComponent<MeshFilter>().sharedMesh =
                ArcadeMeshes.Tube(CupRadius, CupRadius - 0.055f, CupHeight, 44);
            shell.AddComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(CupColour, 0.35f);

            // A capsule stood in for the cup would be picked at the wrong place; this
            // matches what the player sees closely enough to click.
            var picker = cup.gameObject.AddComponent<CapsuleCollider>();
            picker.radius = CupRadius;
            picker.height = CupHeight;
            picker.direction = 1;

            // A trigger, not a solid body. This exists only so the cup can be clicked, and
            // raycasts still find triggers; leaving it solid means a die released at the
            // mouth is overlapping it the instant it stops being kinematic, and the solver
            // ejects it whichever way is cheapest, which is often out through the base.
            picker.isTrigger = true;

            // Loud enough to hear over the dice, quiet enough not to be the whole scene.
            rattleSource = ArcadeAudio.AddSource(cup.gameObject, ArcadeAudio.Rattle(), true);
        }

        /// <summary>A collider with nothing to look at, purely to keep the dice in.</summary>
        void AddContainment(string name, Vector3 localPosition, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPosition;

            // Unity's built in Ignore Raycast layer. These walls are taller than everything
            // else and stand between the camera and the table, so while they are raycastable
            // they swallow every click meant for the cup or a die. The layer only affects
            // queries: the collision matrix is untouched, so dice still bounce off them.
            go.layer = IgnoreRaycastLayer;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// Clears the dice out by hand rather than relying on the table taking them with
        /// it. A thrown die is reparented, and anything that has left the table root is
        /// invisible to destroying the table.
        /// </summary>
        void DestroyDice()
        {
            foreach (YahtzeeDie die in dieViews)
            {
                if (die == null) continue;
                die.Struck -= OnDieStruck;
                Destroy(die.gameObject);
            }
            dieViews.Clear();
            loaded.Clear();
        }

        /// <summary>A die landing. Louder for a harder knock, so a clatter reads as one.</summary>
        void OnDieStruck(float force)
        {
            if (impactSource == null) return;

            impactSource.pitch = Random.Range(0.86f, 1.18f);
            impactSource.PlayOneShot(impactSource.clip, Mathf.Clamp01(force / 5f) * 0.55f);
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour,
            bool collide, float bevel)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = localPosition;
            // The mesh carries the real size so the bevel stays the same width on every
            // face; scaling a unit cube would stretch it.
            box.transform.localScale = Vector3.one;
            ArcadeMeshes.ApplyMesh(box, ArcadeMeshes.RoundedBox(scale, bevel, 5));

            if (collide) box.GetComponent<BoxCollider>().size = scale;
            else Destroy(box.GetComponent<Collider>());

            box.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(colour, 0.2f);
        }

        // Scorecard -----------------------------------------------------------------

        void BuildCard()
        {
            VisualElement rootElement = document.rootVisualElement;
            if (rootElement == null) return;

            rootElement.Clear();
            rootElement.pickingMode = PickingMode.Ignore;

            panel = ArcadeTheme.Panel(12f);
            panel.style.position = Position.Absolute;
            panel.style.right = 22f;
            panel.style.top = 70f;
            panel.style.minWidth = 300f;
            rootElement.Add(panel);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 4f;
            panel.Add(header);
            header.Add(Cell(ArcadeTheme.Heading("Box", 12f), 150f));
            header.Add(Cell(ArcadeTheme.Heading("P1", 12f), 66f));
            header.Add(Cell(ArcadeTheme.Heading("P2", 12f), 66f));

            for (int i = 0; i < YahtzeeScorecard.CategoryCount; i++)
            {
                var category = (YahtzeeCategory)i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1f;
                panel.Add(row);

                row.Add(Cell(ArcadeTheme.Body(YahtzeeScorecard.NameOf(category), 12f), 150f));

                for (int player = 0; player < 2; player++)
                {
                    int captured = player;
                    YahtzeeCategory capturedCategory = category;
                    Button button = ArcadeTheme.MakeButton(string.Empty,
                        () => OnBoxClicked(captured, capturedCategory), 62f);
                    button.style.marginTop = 0f;
                    button.style.paddingTop = 2f;
                    button.style.paddingBottom = 2f;
                    button.style.fontSize = 12f;
                    boxButtons[player][category] = button;
                    row.Add(Cell(button, 66f));
                }
            }

            var totals = new VisualElement();
            totals.style.flexDirection = FlexDirection.Row;
            totals.style.marginTop = 8f;
            panel.Add(totals);
            totals.Add(Cell(ArcadeTheme.Heading("Total", 13f), 150f));
            for (int player = 0; player < 2; player++)
            {
                totalLabels[player] = ArcadeTheme.Body("0", 14f);
                totalLabels[player].style.unityFontStyleAndWeight = FontStyle.Bold;
                totals.Add(Cell(totalLabels[player], 66f));
            }

            rollButton = ArcadeTheme.MakeButton("Roll", Roll, 150f);
            rollButton.style.marginTop = 12f;
            panel.Add(rollButton);
        }

        static VisualElement Cell(VisualElement content, float width)
        {
            var cell = new VisualElement();
            cell.style.width = width;
            cell.Add(content);
            return cell;
        }

        void OnBoxClicked(int player, YahtzeeCategory category)
        {
            if (IsFinished || thinking != null || rolling != null) return;
            if (player != seat) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!rolledThisTurn) return;
            if (cards[player].IsFilled(category)) return;

            Score(category, true);
        }

        void RefreshCard()
        {
            if (panel == null) return;

            bool localTurn = !IsFinished && thinking == null && rolling == null
                && LocalControls(FirstSeatToPlay);

            for (int player = 0; player < 2; player++)
            {
                foreach (var pair in boxButtons[player])
                {
                    Button button = pair.Value;
                    int? filled = cards[player][pair.Key];

                    if (filled.HasValue)
                    {
                        button.text = filled.Value.ToString();
                        button.style.color = ArcadeTheme.TextBright;
                        button.SetEnabled(false);
                        continue;
                    }

                    bool offer = localTurn && player == seat && rolledThisTurn;
                    button.text = offer
                        ? YahtzeeScorecard.ScoreFor(pair.Key, dice, cards[player]).ToString()
                        : string.Empty;
                    button.style.color = ArcadeTheme.TextDim;
                    button.SetEnabled(offer);
                }

                totalLabels[player].text = cards[player].Total.ToString();
            }

            rollButton.SetEnabled(localTurn && rollsUsed < RollsPerTurn);
            rollButton.text = rollsUsed == 0
                ? "Roll"
                : "Roll again (" + (RollsPerTurn - rollsUsed) + " left)";
        }
    }
}
