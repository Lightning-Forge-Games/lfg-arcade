using System;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Yahtzee;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// Scoring is the whole game, so it is tested box by box, including the ones that score
    /// zero when they miss and the joker rules for a second Yahtzee.
    /// </summary>
    public class YahtzeeTests
    {
        static int Score(YahtzeeCategory category, params int[] dice) =>
            YahtzeeScorecard.ScoreFor(category, dice);

        [TestCase(YahtzeeCategory.Ones, 5, new[] { 1, 1, 1, 1, 1 })]
        [TestCase(YahtzeeCategory.Ones, 3, new[] { 1, 1, 1, 4, 5 })]
        [TestCase(YahtzeeCategory.Fives, 15, new[] { 5, 5, 5, 2, 3 })]
        [TestCase(YahtzeeCategory.Sixes, 0, new[] { 1, 2, 3, 4, 5 })]
        public void UpperBoxesCountOnlyTheirOwnFace(YahtzeeCategory category, int expected, int[] dice)
        {
            Assert.AreEqual(expected, Score(category, dice));
        }

        [Test]
        public void ThreeAndFourOfAKindScoreTheWholeRoll()
        {
            // The total of all five dice, not just the matching ones, which is the part
            // people misremember.
            Assert.AreEqual(20, Score(YahtzeeCategory.ThreeOfAKind, 4, 4, 4, 3, 5));
            Assert.AreEqual(0, Score(YahtzeeCategory.ThreeOfAKind, 4, 4, 3, 2, 5));

            Assert.AreEqual(21, Score(YahtzeeCategory.FourOfAKind, 4, 4, 4, 4, 5));
            Assert.AreEqual(0, Score(YahtzeeCategory.FourOfAKind, 4, 4, 4, 3, 5));
        }

        [Test]
        public void AFullHouseIsThreeAndTwo()
        {
            Assert.AreEqual(25, Score(YahtzeeCategory.FullHouse, 3, 3, 3, 6, 6));
            Assert.AreEqual(0, Score(YahtzeeCategory.FullHouse, 3, 3, 3, 6, 4));

            // Five of a kind is not a full house on its own; only the joker rule makes it one.
            Assert.AreEqual(0, Score(YahtzeeCategory.FullHouse, 3, 3, 3, 3, 3));
        }

        [Test]
        public void StraightsNeedConsecutiveFaces()
        {
            Assert.AreEqual(30, Score(YahtzeeCategory.SmallStraight, 1, 2, 3, 4, 4));
            Assert.AreEqual(30, Score(YahtzeeCategory.SmallStraight, 2, 3, 4, 5, 1));
            Assert.AreEqual(0, Score(YahtzeeCategory.SmallStraight, 1, 2, 3, 5, 6));

            Assert.AreEqual(40, Score(YahtzeeCategory.LargeStraight, 1, 2, 3, 4, 5));
            Assert.AreEqual(40, Score(YahtzeeCategory.LargeStraight, 2, 3, 4, 5, 6));
            Assert.AreEqual(0, Score(YahtzeeCategory.LargeStraight, 1, 2, 3, 4, 6));

            // A large straight also satisfies the small one.
            Assert.AreEqual(30, Score(YahtzeeCategory.SmallStraight, 2, 3, 4, 5, 6));
        }

        [Test]
        public void YahtzeeAndChance()
        {
            Assert.AreEqual(50, Score(YahtzeeCategory.Yahtzee, 6, 6, 6, 6, 6));
            Assert.AreEqual(0, Score(YahtzeeCategory.Yahtzee, 6, 6, 6, 6, 5));
            Assert.AreEqual(21, Score(YahtzeeCategory.Chance, 1, 3, 5, 6, 6));
        }

        [Test]
        public void TheUpperBonusArrivesAtSixtyThree()
        {
            var card = new YahtzeeScorecard();
            // Three of each face is exactly the threshold.
            card.Fill(YahtzeeCategory.Ones, new[] { 1, 1, 1, 4, 5 });
            card.Fill(YahtzeeCategory.Twos, new[] { 2, 2, 2, 4, 5 });
            card.Fill(YahtzeeCategory.Threes, new[] { 3, 3, 3, 4, 5 });
            card.Fill(YahtzeeCategory.Fours, new[] { 4, 4, 4, 1, 5 });
            card.Fill(YahtzeeCategory.Fives, new[] { 5, 5, 5, 1, 2 });

            Assert.AreEqual(0, card.Bonus, "not there yet");

            card.Fill(YahtzeeCategory.Sixes, new[] { 6, 6, 6, 1, 2 });
            Assert.AreEqual(YahtzeeScorecard.UpperBonusThreshold, card.UpperTotal);
            Assert.AreEqual(YahtzeeScorecard.UpperBonus, card.Bonus);
        }

        [Test]
        public void ABoxCanOnlyBeUsedOnce()
        {
            var card = new YahtzeeScorecard();
            Assert.IsTrue(card.Fill(YahtzeeCategory.Chance, new[] { 1, 2, 3, 4, 5 }));
            Assert.IsFalse(card.Fill(YahtzeeCategory.Chance, new[] { 6, 6, 6, 6, 6 }),
                "a filled box must be refused");
            Assert.AreEqual(15, card[YahtzeeCategory.Chance], "the original score must stand");
        }

        [Test]
        public void ABoxMayBeZeroedDeliberately()
        {
            // Taking a zero is a legal and sometimes correct move, so it must be allowed
            // rather than treated as an error.
            var card = new YahtzeeScorecard();
            Assert.IsTrue(card.Fill(YahtzeeCategory.Yahtzee, new[] { 1, 2, 3, 4, 5 }));
            Assert.AreEqual(0, card[YahtzeeCategory.Yahtzee]);
            Assert.IsTrue(card.IsFilled(YahtzeeCategory.Yahtzee));
        }

        [Test]
        public void ASecondYahtzeeEarnsABonus()
        {
            var card = new YahtzeeScorecard();
            card.Fill(YahtzeeCategory.Yahtzee, new[] { 5, 5, 5, 5, 5 });
            Assert.AreEqual(50, card[YahtzeeCategory.Yahtzee]);
            Assert.AreEqual(0, card.ExtraYahtzees);

            card.Fill(YahtzeeCategory.Fives, new[] { 5, 5, 5, 5, 5 });
            Assert.AreEqual(1, card.ExtraYahtzees, "the second one is worth a bonus");
            Assert.AreEqual(25, card[YahtzeeCategory.Fives], "and still scores in the box");
            Assert.AreEqual(50 + 25 + YahtzeeScorecard.YahtzeeBonus, card.Total);
        }

        [Test]
        public void NoBonusWhenTheYahtzeeBoxWasZeroed()
        {
            // Having thrown the box away for nothing, later Yahtzees earn no bonus.
            var card = new YahtzeeScorecard();
            card.Fill(YahtzeeCategory.Yahtzee, new[] { 1, 2, 3, 4, 5 });
            card.Fill(YahtzeeCategory.Sixes, new[] { 6, 6, 6, 6, 6 });

            Assert.AreEqual(0, card.ExtraYahtzees);
        }

        [Test]
        public void TheJokerRuleFillsAShapeBoxThatDoesNotFit()
        {
            // Yahtzee box used, and the matching upper box gone too, so five sixes may be
            // written into a lower box at full value even though the dice are not that shape.
            var card = new YahtzeeScorecard();
            card.Fill(YahtzeeCategory.Yahtzee, new[] { 5, 5, 5, 5, 5 });
            card.Fill(YahtzeeCategory.Sixes, new[] { 6, 6, 6, 2, 1 });

            int[] fiveSixes = { 6, 6, 6, 6, 6 };
            Assert.AreEqual(25, YahtzeeScorecard.ScoreFor(YahtzeeCategory.FullHouse, fiveSixes, card));
            Assert.AreEqual(30, YahtzeeScorecard.ScoreFor(YahtzeeCategory.SmallStraight, fiveSixes, card));
            Assert.AreEqual(40, YahtzeeScorecard.ScoreFor(YahtzeeCategory.LargeStraight, fiveSixes, card));
        }

        [Test]
        public void TheJokerRuleDoesNotApplyWhileTheUpperBoxIsFree()
        {
            // The matching upper box is still open, so the roll belongs there and a straight
            // box would score its usual nothing.
            var card = new YahtzeeScorecard();
            card.Fill(YahtzeeCategory.Yahtzee, new[] { 5, 5, 5, 5, 5 });

            int[] fiveSixes = { 6, 6, 6, 6, 6 };
            Assert.AreEqual(0, YahtzeeScorecard.ScoreFor(YahtzeeCategory.LargeStraight, fiveSixes, card));
        }

        [Test]
        public void ACardIsCompleteAfterThirteenBoxes()
        {
            var card = new YahtzeeScorecard();
            int[] dice = { 1, 2, 3, 4, 5 };

            var open = new List<YahtzeeCategory>(card.OpenCategories());
            Assert.AreEqual(YahtzeeScorecard.CategoryCount, open.Count);

            foreach (YahtzeeCategory category in open) card.Fill(category, dice);

            Assert.IsTrue(card.IsComplete);
            Assert.AreEqual(0, new List<YahtzeeCategory>(card.OpenCategories()).Count);
        }

        // The opponent ------------------------------------------------------------

        [Test]
        public void TheOpponentKeepsItsBestFace()
        {
            var player = new YahtzeePlayer(new Random(1));
            var card = new YahtzeeScorecard();

            bool[] keep = player.ChooseKeeps(new[] { 4, 4, 4, 1, 2 }, card, Difficulty.Easy);
            Assert.IsTrue(keep[0] && keep[1] && keep[2], "should keep the three fours");
            Assert.IsFalse(keep[3] || keep[4], "and reroll the rest");
        }

        [Test]
        public void TheOpponentChasesAStraightWhenThatIsTheBetterShape()
        {
            var player = new YahtzeePlayer(new Random(1));
            var card = new YahtzeeScorecard();

            // Four to a straight beats a bare pair, but only above Easy.
            bool[] keep = player.ChooseKeeps(new[] { 2, 3, 4, 5, 5 }, card, Difficulty.Hard);
            int kept = 0;
            foreach (bool k in keep)
            {
                if (k) kept++;
            }
            Assert.AreEqual(4, kept, "should keep the run of four and reroll one die");
        }

        [Test]
        public void TheOpponentAlwaysPicksAnOpenBox()
        {
            var random = new Random(4);
            var player = new YahtzeePlayer(random);
            var card = new YahtzeeScorecard();

            for (int turn = 0; turn < YahtzeeScorecard.CategoryCount; turn++)
            {
                var dice = new int[5];
                for (int i = 0; i < dice.Length; i++) dice[i] = random.Next(1, 7);

                YahtzeeCategory chosen = player.ChooseCategory(dice, card, Difficulty.Medium);
                Assert.IsFalse(card.IsFilled(chosen), "picked a box that was already used");
                Assert.IsTrue(card.Fill(chosen, dice));
            }

            Assert.IsTrue(card.IsComplete, "thirteen turns should fill the card");
        }

        [Test]
        public void TheOpponentTakesAYahtzeeWhenItRollsOne()
        {
            var player = new YahtzeePlayer(new Random(2));
            var card = new YahtzeeScorecard();

            YahtzeeCategory chosen = player.ChooseCategory(new[] { 3, 3, 3, 3, 3 }, card, Difficulty.Hard);
            Assert.AreEqual(YahtzeeCategory.Yahtzee, chosen, "fifty points should not be passed up");
        }

        [Test]
        public void TheHarderOpponentScoresBetterOnAverage()
        {
            // Over a run of full games, the setting that protects boxes and chases the upper
            // bonus should beat the one that just takes the most points on offer.
            double hard = AverageScore(Difficulty.Hard, 40);
            double easy = AverageScore(Difficulty.Easy, 40);

            Assert.Greater(hard, easy,
                "Hard averaged " + hard.ToString("0.0") + " against Easy on " + easy.ToString("0.0"));
        }

        static double AverageScore(Difficulty difficulty, int games)
        {
            int total = 0;
            for (int seed = 0; seed < games; seed++)
            {
                var random = new Random(seed);
                var player = new YahtzeePlayer(random);
                var card = new YahtzeeScorecard();

                while (!card.IsComplete)
                {
                    var dice = new int[5];
                    for (int i = 0; i < dice.Length; i++) dice[i] = random.Next(1, 7);

                    // Two rerolls, as in the real game.
                    for (int roll = 0; roll < 2; roll++)
                    {
                        bool[] keep = player.ChooseKeeps(dice, card, difficulty);
                        for (int i = 0; i < dice.Length; i++)
                        {
                            if (!keep[i]) dice[i] = random.Next(1, 7);
                        }
                    }

                    card.Fill(player.ChooseCategory(dice, card, difficulty), dice);
                }
                total += card.Total;
            }
            return (double)total / games;
        }
    }
}
