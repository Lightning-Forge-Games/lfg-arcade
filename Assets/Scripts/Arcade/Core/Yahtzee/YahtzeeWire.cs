using System.Collections.Generic;
using System.Text;

namespace LightningForge.Arcade.Core.Yahtzee
{
    /// <summary>What one relayed Yahtzee message is telling the other player.</summary>
    public enum YahtzeeMessageKind
    {
        Unknown,

        /// <summary>The cup has been picked up and the dice are in it.</summary>
        CupLifted,

        /// <summary>The dice have been thrown and come to rest.</summary>
        Thrown,

        /// <summary>Which dice are being kept has changed.</summary>
        Kept,

        /// <summary>A box has been filled and the turn is over.</summary>
        Scored,
    }

    /// <summary>Where one die came to rest, and what it is showing.</summary>
    public struct YahtzeeLandedDie
    {
        public float X;
        public float Z;
        public int Value;
    }

    /// <summary>A decoded message.</summary>
    public struct YahtzeeMessage
    {
        public YahtzeeMessageKind Kind;

        /// <summary>Where the dice landed. Only for <see cref="YahtzeeMessageKind.Thrown"/>.</summary>
        public YahtzeeLandedDie[] Landed;

        /// <summary>Which dice are kept. Only for <see cref="YahtzeeMessageKind.Kept"/>.</summary>
        public bool[] Held;

        /// <summary>The box filled and the dice put in it. Only for Scored.</summary>
        public YahtzeeCategory Category;
        public int[] Dice;
    }

    /// <summary>
    /// The wire format for online Yahtzee.
    ///
    /// A turn used to travel as a single message sent when a box was filled, which meant the
    /// other player watched a frozen table and then saw the result appear. There is nothing
    /// live about that, so the turn is now narrated: the cup going up, the dice coming to
    /// rest, what is being kept, and finally the box.
    ///
    /// The throw carries where each die landed as well as what it shows. PhysX is not
    /// deterministic across platforms, so replaying the throw on the other machine would
    /// produce a different scatter and different numbers. The thrower's table is the one
    /// that counts and the other end is told the outcome, which is the same bargain any
    /// networked physics game makes.
    ///
    /// Positions travel as hundredths of a unit in whole numbers rather than as decimals,
    /// so nothing depends on the number formatting of whichever machine sent it.
    /// </summary>
    public static class YahtzeeWire
    {
        const char Separator = '|';
        const char Field = ',';
        const int Scale = 100;

        public static string CupLifted() => "c";

        public static string Thrown(IList<YahtzeeLandedDie> landed)
        {
            var sb = new StringBuilder("t");
            foreach (YahtzeeLandedDie die in landed)
            {
                sb.Append(Separator)
                  .Append(Round(die.X * Scale)).Append(Field)
                  .Append(Round(die.Z * Scale)).Append(Field)
                  .Append(die.Value);
            }
            return sb.ToString();
        }

        public static string Kept(IList<bool> held)
        {
            var sb = new StringBuilder("k").Append(Separator);
            foreach (bool one in held) sb.Append(one ? '1' : '0');
            return sb.ToString();
        }

        public static string Scored(YahtzeeCategory category, IList<int> dice)
        {
            var sb = new StringBuilder("s").Append(Separator)
                .Append((int)category).Append(Separator);
            foreach (int die in dice) sb.Append(die);
            return sb.ToString();
        }

        public static bool TryParse(string text, out YahtzeeMessage message)
        {
            message = new YahtzeeMessage();
            if (string.IsNullOrEmpty(text)) return false;

            string[] parts = text.Split(Separator);
            switch (parts[0])
            {
                case "c":
                    message.Kind = YahtzeeMessageKind.CupLifted;
                    return true;

                case "t":
                    return TryParseThrown(parts, ref message);

                case "k":
                    return TryParseKept(parts, ref message);

                case "s":
                    return TryParseScored(parts, ref message);

                default:
                    return false;
            }
        }

        static bool TryParseThrown(string[] parts, ref YahtzeeMessage message)
        {
            if (parts.Length < 2) return false;

            var landed = new YahtzeeLandedDie[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                string[] fields = parts[i].Split(Field);
                if (fields.Length != 3) return false;

                if (!int.TryParse(fields[0], out int x)) return false;
                if (!int.TryParse(fields[1], out int z)) return false;
                if (!int.TryParse(fields[2], out int value)) return false;
                if (value < 1 || value > 6) return false;

                landed[i - 1] = new YahtzeeLandedDie
                {
                    X = x / (float)Scale,
                    Z = z / (float)Scale,
                    Value = value,
                };
            }

            message.Kind = YahtzeeMessageKind.Thrown;
            message.Landed = landed;
            return true;
        }

        static bool TryParseKept(string[] parts, ref YahtzeeMessage message)
        {
            if (parts.Length != 2 || parts[1].Length == 0) return false;

            var held = new bool[parts[1].Length];
            for (int i = 0; i < held.Length; i++)
            {
                char c = parts[1][i];
                if (c != '0' && c != '1') return false;
                held[i] = c == '1';
            }

            message.Kind = YahtzeeMessageKind.Kept;
            message.Held = held;
            return true;
        }

        static bool TryParseScored(string[] parts, ref YahtzeeMessage message)
        {
            if (parts.Length != 3) return false;
            if (!int.TryParse(parts[1], out int category)) return false;
            if (category < 0 || category >= YahtzeeScorecard.CategoryCount) return false;
            if (parts[2].Length == 0) return false;

            var dice = new int[parts[2].Length];
            for (int i = 0; i < dice.Length; i++)
            {
                int value = parts[2][i] - '0';
                if (value < 1 || value > 6) return false;
                dice[i] = value;
            }

            message.Kind = YahtzeeMessageKind.Scored;
            message.Category = (YahtzeeCategory)category;
            message.Dice = dice;
            return true;
        }

        /// <summary>
        /// Rounds away from zero at the halfway point, without going through a float format.
        /// </summary>
        static int Round(float value) => (int)(value >= 0f ? value + 0.5f : value - 0.5f);
    }
}
