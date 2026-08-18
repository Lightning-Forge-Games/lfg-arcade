using System.Collections.Generic;
using LightningForge.Arcade.Core.Snooker;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// The frame rules, tested without a table. What is worth being sure about is the ball
    /// on, what a pot is worth, and which fouls cost what, since all of those decide the
    /// score rather than merely how the balls move.
    /// </summary>
    public class SnookerTests
    {
        static SnookerShot Shot(SnookerBall? first, params SnookerBall[] potted) =>
            new SnookerShot { FirstContact = first, Potted = new List<SnookerBall>(potted) };

        [Test]
        public void AFrameStartsOnTheReds()
        {
            var frame = new SnookerFrame(2);
            Assert.AreEqual(SnookerFrame.TotalReds, frame.RedsRemaining);
            Assert.AreEqual(SnookerBall.Red, frame.BallOn);
            Assert.AreEqual("a red", frame.BallOnName);
            Assert.AreEqual(0, frame.ScoreOf(0));
        }

        [Test]
        public void PottingARedScoresOneAndPutsAColourOn()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));

            Assert.AreEqual(1, outcome.Scored);
            Assert.IsFalse(outcome.TurnEnds, "a pot keeps you at the table");
            Assert.AreEqual(14, frame.RedsRemaining);
            Assert.IsTrue(frame.ColourIsOn);
            Assert.IsNull(frame.BallOn, "any colour will do now");
            Assert.AreEqual("a colour", frame.BallOnName);
            Assert.AreEqual(1, frame.Break);
        }

        [Test]
        public void AColourAfterARedScoresAndGoesBackOnItsSpot()
        {
            var frame = new SnookerFrame(2);
            frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));

            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Black, SnookerBall.Black));
            Assert.AreEqual(7, outcome.Scored);
            Assert.Contains(SnookerBall.Black, outcome.Respot, "the black must be respotted");
            Assert.IsFalse(frame.ColourIsOn, "back on the reds");
            Assert.AreEqual(SnookerBall.Red, frame.BallOn);
            Assert.AreEqual(8, frame.Break, "a red and a black is a break of eight");
        }

        [Test]
        public void TwoRedsInOneShotScoreTwo()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red, SnookerBall.Red));

            Assert.AreEqual(2, outcome.Scored);
            Assert.AreEqual(13, frame.RedsRemaining);
            Assert.IsTrue(frame.ColourIsOn);
        }

        [Test]
        public void MissingEverythingIsAFoulOfFour()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(null));

            Assert.IsTrue(outcome.IsFoul);
            Assert.AreEqual(SnookerFrame.MinimumFoul, outcome.FoulPoints);
            Assert.AreEqual(4, frame.ScoreOf(1), "the points go to the opponent");
            Assert.AreEqual(0, frame.ScoreOf(0));
            Assert.AreEqual(1, frame.CurrentPlayer, "and the table changes hands");
        }

        [Test]
        public void HittingTheWrongBallFirstIsAFoulWorthThatBall()
        {
            // Reds are on, so striking the black first is a foul worth seven, not four.
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Black));

            Assert.IsTrue(outcome.IsFoul);
            Assert.AreEqual(7, outcome.FoulPoints);
            Assert.AreEqual(7, frame.ScoreOf(1));
        }

        [Test]
        public void GoingInOffIsAFoul()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red, SnookerBall.Cue));

            Assert.IsTrue(outcome.IsFoul);
            Assert.IsTrue(outcome.TurnEnds);
            Assert.AreEqual(4, frame.ScoreOf(1), "in off the red is the minimum four");
        }

        [Test]
        public void AColourPottedOnAFoulComesBackUp()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Blue, SnookerBall.Blue));

            Assert.IsTrue(outcome.IsFoul, "the blue was not on");
            Assert.Contains(SnookerBall.Blue, outcome.Respot);
            Assert.AreEqual(5, frame.ScoreOf(1), "worth the blue, above the minimum");
        }

        [Test]
        public void ALegalMissPassesTheTableWithoutPoints()
        {
            var frame = new SnookerFrame(2);
            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Red));

            Assert.IsFalse(outcome.IsFoul, "hitting the ball on is not a foul");
            Assert.IsTrue(outcome.TurnEnds);
            Assert.AreEqual(0, frame.ScoreOf(0));
            Assert.AreEqual(0, frame.ScoreOf(1));
            Assert.AreEqual(1, frame.CurrentPlayer);
        }

        [Test]
        public void ABreakResetsWhenTheTurnEnds()
        {
            var frame = new SnookerFrame(2);
            frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));
            frame.Apply(Shot(SnookerBall.Black, SnookerBall.Black));
            Assert.AreEqual(8, frame.Break);

            frame.Apply(Shot(SnookerBall.Red));
            Assert.AreEqual(0, frame.Break, "a missed pot ends the break");
        }

        [Test]
        public void OnceTheRedsAreGoneTheColoursComeInOrder()
        {
            var frame = new SnookerFrame(1);

            // Clear the reds, taking a colour after each so the sequence is legal.
            for (int i = 0; i < SnookerFrame.TotalReds; i++)
            {
                frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));
                frame.Apply(Shot(SnookerBall.Black, SnookerBall.Black));
            }

            Assert.AreEqual(0, frame.RedsRemaining);
            Assert.AreEqual(SnookerBall.Yellow, frame.BallOn, "yellow is next");

            frame.Apply(Shot(SnookerBall.Yellow, SnookerBall.Yellow));
            Assert.AreEqual(SnookerBall.Green, frame.BallOn, "then green");
            Assert.IsFalse(frame.IsFinished);
        }

        [Test]
        public void ColoursInTheEndSequenceStayDown()
        {
            var frame = new SnookerFrame(1);
            for (int i = 0; i < SnookerFrame.TotalReds; i++)
            {
                frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));
                frame.Apply(Shot(SnookerBall.Black, SnookerBall.Black));
            }

            SnookerOutcome outcome = frame.Apply(Shot(SnookerBall.Yellow, SnookerBall.Yellow));
            Assert.AreEqual(0, outcome.Respot.Count, "colours stay down at the end of a frame");
        }

        [Test]
        public void ClearingTheTableFinishesTheFrame()
        {
            var frame = new SnookerFrame(1);
            for (int i = 0; i < SnookerFrame.TotalReds; i++)
            {
                frame.Apply(Shot(SnookerBall.Red, SnookerBall.Red));
                frame.Apply(Shot(SnookerBall.Black, SnookerBall.Black));
            }

            foreach (SnookerBall colour in new[]
                     {
                         SnookerBall.Yellow, SnookerBall.Green, SnookerBall.Brown,
                         SnookerBall.Blue, SnookerBall.Pink, SnookerBall.Black,
                     })
            {
                Assert.IsFalse(frame.IsFinished, "not done until the black goes");
                frame.Apply(Shot(colour, colour));
            }

            Assert.IsTrue(frame.IsFinished);
            // Fifteen reds and fifteen blacks, then the colours: 15 + 105 + 27.
            Assert.AreEqual(147, frame.ScoreOf(0), "that is a maximum break");
        }

        [Test]
        public void ASoloFrameKeepsTheSamePlayerAtTheTable()
        {
            var frame = new SnookerFrame(1);
            frame.Apply(Shot(null));

            Assert.AreEqual(0, frame.CurrentPlayer, "there is nobody to hand over to");
        }
    }
}
