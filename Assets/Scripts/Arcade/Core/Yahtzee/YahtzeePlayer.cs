using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Yahtzee
{
    /// <summary>
    /// The Yahtzee opponent: which dice to keep, and which box to use.
    ///
    /// Yahtzee is a question of expected value rather than search. Playing it well means
    /// knowing that a box is worth more than the points it scores now, because filling the
    /// wrong one early costs the bonus or wastes the only slot a big roll could have used.
    ///
    /// Rather than enumerate the reroll space exactly, which is large and adds little at
    /// this level, keeping is decided by the shape already on the table and scoring is
    /// decided by value minus what the box is worth to hold back.
    /// </summary>
    public sealed class YahtzeePlayer
    {
        readonly Random random;

        public YahtzeePlayer(Random random)
        {
            this.random = random ?? new Random(0);
        }

        /// <summary>
        /// Which of the five dice to keep before rerolling. True means keep.
        ///
        /// Easy keeps only the most common face. Medium and Hard also chase a straight when
        /// that is the better shape, which is the difference between a card with a large
        /// straight on it and one without.
        /// </summary>
        public bool[] ChooseKeeps(int[] dice, YahtzeeScorecard card, Difficulty difficulty)
        {
            var keep = new bool[dice.Length];

            int[] counts = new int[7];
            foreach (int die in dice) counts[die]++;

            int bestFace = 1;
            for (int face = 2; face <= 6; face++)
            {
                if (counts[face] > counts[bestFace]) bestFace = face;
                // On a tie the higher face is worth more in the upper section.
                else if (counts[face] == counts[bestFace] && face > bestFace) bestFace = face;
            }

            if (difficulty != Difficulty.Easy)
            {
                // A run of three or four is worth more than a pair, so keep the run instead.
                int runStart = LongestRunStart(counts, out int runLength);
                bool straightsOpen = !card.IsFilled(YahtzeeCategory.SmallStraight)
                    || !card.IsFilled(YahtzeeCategory.LargeStraight);

                if (straightsOpen && runLength >= 3 && runLength > counts[bestFace])
                {
                    var used = new bool[7];
                    for (int i = 0; i < dice.Length; i++)
                    {
                        int die = dice[i];
                        if (die >= runStart && die < runStart + runLength && !used[die])
                        {
                            keep[i] = true;
                            used[die] = true;
                        }
                    }
                    return keep;
                }
            }

            for (int i = 0; i < dice.Length; i++) keep[i] = dice[i] == bestFace;
            return keep;
        }

        /// <summary>Which box to write this roll into.</summary>
        public YahtzeeCategory ChooseCategory(int[] dice, YahtzeeScorecard card, Difficulty difficulty)
        {
            var open = new List<YahtzeeCategory>(card.OpenCategories());
            if (open.Count == 0) return YahtzeeCategory.Chance;

            var best = new List<YahtzeeCategory>();
            double bestValue = double.NegativeInfinity;

            foreach (YahtzeeCategory category in open)
            {
                double value = YahtzeeScorecard.ScoreFor(category, dice, card);

                if (difficulty != Difficulty.Easy)
                {
                    // Zeroing a box that could still score well is a real cost, so a box is
                    // worth less than its face value when the roll does not suit it.
                    value -= HoldingValue(category, value);

                    // The upper bonus is thirty five points that a couple of thin boxes can
                    // quietly throw away, so reward staying on pace for it.
                    if (IsUpper(category))
                    {
                        int face = (int)category - (int)YahtzeeCategory.Ones + 1;
                        double pace = value - face * 3;
                        value += pace * (difficulty == Difficulty.Hard ? 1.4 : 0.7);
                    }
                }

                if (value > bestValue + 0.001)
                {
                    bestValue = value;
                    best.Clear();
                    best.Add(category);
                }
                else if (value > bestValue - 0.001)
                {
                    best.Add(category);
                }
            }

            return best[random.Next(best.Count)];
        }

        /// <summary>
        /// Roughly what a box is worth keeping for a better roll. Yahtzee and the straights
        /// are worth holding because they score nothing at all when they miss, so filling
        /// one with a bad roll throws the whole box away.
        /// </summary>
        static double HoldingValue(YahtzeeCategory category, double scoredNow)
        {
            switch (category)
            {
                case YahtzeeCategory.Yahtzee: return scoredNow > 0 ? 0 : 22;
                case YahtzeeCategory.LargeStraight: return scoredNow > 0 ? 0 : 16;
                case YahtzeeCategory.SmallStraight: return scoredNow > 0 ? 0 : 11;
                case YahtzeeCategory.FullHouse: return scoredNow > 0 ? 0 : 8;
                case YahtzeeCategory.FourOfAKind: return scoredNow > 0 ? 0 : 7;
                case YahtzeeCategory.ThreeOfAKind: return scoredNow > 0 ? 0 : 4;
                case YahtzeeCategory.Chance: return 4;
                default: return 0;
            }
        }

        static bool IsUpper(YahtzeeCategory category) =>
            category >= YahtzeeCategory.Ones && category <= YahtzeeCategory.Sixes;

        static int LongestRunStart(int[] counts, out int length)
        {
            int bestStart = 1;
            int bestLength = 0;
            int start = 1;
            int run = 0;

            for (int face = 1; face <= 6; face++)
            {
                if (counts[face] > 0)
                {
                    if (run == 0) start = face;
                    run++;
                    if (run > bestLength)
                    {
                        bestLength = run;
                        bestStart = start;
                    }
                }
                else
                {
                    run = 0;
                }
            }

            length = bestLength;
            return bestStart;
        }
    }
}
