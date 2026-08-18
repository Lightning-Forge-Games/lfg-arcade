using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Yahtzee
{
    /// <summary>The thirteen boxes, in the order they appear on a real card.</summary>
    public enum YahtzeeCategory
    {
        Ones,
        Twos,
        Threes,
        Fours,
        Fives,
        Sixes,
        ThreeOfAKind,
        FourOfAKind,
        FullHouse,
        SmallStraight,
        LargeStraight,
        Yahtzee,
        Chance,
    }

    /// <summary>
    /// One player's card: which boxes are filled, what they scored, and the running totals.
    ///
    /// Scoring lives here rather than in the game object because it is the part worth being
    /// sure about, and it is pure arithmetic over five numbers. The joker rules for a second
    /// Yahtzee are the corner that catches people out, so they are spelled out rather than
    /// left implicit.
    /// </summary>
    public sealed class YahtzeeScorecard
    {
        public const int CategoryCount = 13;
        public const int UpperBonusThreshold = 63;
        public const int UpperBonus = 35;
        public const int YahtzeeBonus = 100;

        readonly int?[] scores = new int?[CategoryCount];

        /// <summary>Extra Yahtzees after the first scoring one, each worth a bonus.</summary>
        public int ExtraYahtzees { get; private set; }

        public int? this[YahtzeeCategory category] => scores[(int)category];

        public bool IsFilled(YahtzeeCategory category) => scores[(int)category].HasValue;

        public bool IsComplete
        {
            get
            {
                for (int i = 0; i < CategoryCount; i++)
                {
                    if (!scores[i].HasValue) return false;
                }
                return true;
            }
        }

        public IEnumerable<YahtzeeCategory> OpenCategories()
        {
            for (int i = 0; i < CategoryCount; i++)
            {
                if (!scores[i].HasValue) yield return (YahtzeeCategory)i;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < CategoryCount; i++) scores[i] = null;
            ExtraYahtzees = 0;
        }

        public int UpperTotal
        {
            get
            {
                int total = 0;
                for (int i = (int)YahtzeeCategory.Ones; i <= (int)YahtzeeCategory.Sixes; i++)
                {
                    total += scores[i] ?? 0;
                }
                return total;
            }
        }

        public int LowerTotal
        {
            get
            {
                int total = 0;
                for (int i = (int)YahtzeeCategory.ThreeOfAKind; i < CategoryCount; i++)
                {
                    total += scores[i] ?? 0;
                }
                return total;
            }
        }

        public int Bonus => UpperTotal >= UpperBonusThreshold ? UpperBonus : 0;

        public int Total => UpperTotal + Bonus + LowerTotal + ExtraYahtzees * YahtzeeBonus;

        /// <summary>
        /// Writes a score into a box. Returns false if the box is already used, which is the
        /// one thing the interface must never let happen.
        /// </summary>
        public bool Fill(YahtzeeCategory category, int[] dice)
        {
            if (scores[(int)category].HasValue) return false;

            // A second Yahtzee earns a bonus whether or not the Yahtzee box scored, but only
            // once the box itself has been used and scored.
            if (IsYahtzee(dice) && scores[(int)YahtzeeCategory.Yahtzee].HasValue
                && scores[(int)YahtzeeCategory.Yahtzee] > 0)
            {
                ExtraYahtzees++;
            }

            scores[(int)category] = ScoreFor(category, dice, this);
            return true;
        }

        public static bool IsYahtzee(int[] dice)
        {
            for (int i = 1; i < dice.Length; i++)
            {
                if (dice[i] != dice[0]) return false;
            }
            return dice.Length > 0;
        }

        /// <summary>
        /// What a roll is worth in a box.
        ///
        /// The card is passed in because the joker rules depend on it: a Yahtzee rolled when
        /// the Yahtzee box is already used, and the matching upper box is gone too, may be
        /// scored in a lower box at its full value even though the dice do not fit the shape.
        /// </summary>
        public static int ScoreFor(YahtzeeCategory category, int[] dice, YahtzeeScorecard card = null)
        {
            int[] counts = new int[7];
            int sum = 0;
            foreach (int die in dice)
            {
                counts[die]++;
                sum += die;
            }

            bool joker = card != null
                && IsYahtzee(dice)
                && card.IsFilled(YahtzeeCategory.Yahtzee)
                && card.IsFilled(UpperBoxFor(dice[0]));

            switch (category)
            {
                case YahtzeeCategory.Ones: return counts[1] * 1;
                case YahtzeeCategory.Twos: return counts[2] * 2;
                case YahtzeeCategory.Threes: return counts[3] * 3;
                case YahtzeeCategory.Fours: return counts[4] * 4;
                case YahtzeeCategory.Fives: return counts[5] * 5;
                case YahtzeeCategory.Sixes: return counts[6] * 6;

                case YahtzeeCategory.ThreeOfAKind:
                    return HasOfAKind(counts, 3) ? sum : 0;

                case YahtzeeCategory.FourOfAKind:
                    return HasOfAKind(counts, 4) ? sum : 0;

                case YahtzeeCategory.FullHouse:
                    if (joker) return 25;
                    return IsFullHouse(counts) ? 25 : 0;

                case YahtzeeCategory.SmallStraight:
                    if (joker) return 30;
                    return HasStraight(counts, 4) ? 30 : 0;

                case YahtzeeCategory.LargeStraight:
                    if (joker) return 40;
                    return HasStraight(counts, 5) ? 40 : 0;

                case YahtzeeCategory.Yahtzee:
                    return IsYahtzee(dice) ? 50 : 0;

                case YahtzeeCategory.Chance:
                    return sum;

                default:
                    return 0;
            }
        }

        public static YahtzeeCategory UpperBoxFor(int face) =>
            (YahtzeeCategory)((int)YahtzeeCategory.Ones + face - 1);

        static bool HasOfAKind(int[] counts, int needed)
        {
            for (int face = 1; face <= 6; face++)
            {
                if (counts[face] >= needed) return true;
            }
            return false;
        }

        /// <summary>
        /// A full house is three of one face and two of another. Five of a kind is not one,
        /// except under the joker rule, which is handled by the caller.
        /// </summary>
        static bool IsFullHouse(int[] counts)
        {
            bool three = false;
            bool two = false;
            for (int face = 1; face <= 6; face++)
            {
                if (counts[face] == 3) three = true;
                else if (counts[face] == 2) two = true;
            }
            return three && two;
        }

        static bool HasStraight(int[] counts, int length)
        {
            int run = 0;
            for (int face = 1; face <= 6; face++)
            {
                if (counts[face] > 0)
                {
                    run++;
                    if (run >= length) return true;
                }
                else
                {
                    run = 0;
                }
            }
            return false;
        }

        public static string NameOf(YahtzeeCategory category)
        {
            switch (category)
            {
                case YahtzeeCategory.ThreeOfAKind: return "Three of a Kind";
                case YahtzeeCategory.FourOfAKind: return "Four of a Kind";
                case YahtzeeCategory.FullHouse: return "Full House";
                case YahtzeeCategory.SmallStraight: return "Small Straight";
                case YahtzeeCategory.LargeStraight: return "Large Straight";
                default: return category.ToString();
            }
        }

        public YahtzeeScorecard Clone()
        {
            var copy = new YahtzeeScorecard();
            for (int i = 0; i < CategoryCount; i++) copy.scores[i] = scores[i];
            copy.ExtraYahtzees = ExtraYahtzees;
            return copy;
        }
    }
}
