using System;

namespace LightningForge.Arcade.Core.Chess
{
    [Flags]
    public enum MoveFlags : byte
    {
        None = 0,
        Capture = 1,
        DoublePawnPush = 2,
        EnPassant = 4,
        KingSideCastle = 8,
        QueenSideCastle = 16,
        Promotion = 32
    }

    /// <summary>A single ply. Promotion is <see cref="PieceType.None"/> unless the move promotes.</summary>
    public readonly struct Move : IEquatable<Move>
    {
        public static readonly Move None = default;

        public readonly byte From;
        public readonly byte To;
        public readonly PieceType Promotion;
        public readonly MoveFlags Flags;

        public Move(int from, int to, MoveFlags flags = MoveFlags.None, PieceType promotion = PieceType.None)
        {
            From = (byte)from;
            To = (byte)to;
            Flags = flags;
            Promotion = promotion;
        }

        public bool IsNone => From == 0 && To == 0 && Flags == MoveFlags.None;
        public bool IsCapture => (Flags & MoveFlags.Capture) != 0;
        public bool IsEnPassant => (Flags & MoveFlags.EnPassant) != 0;
        public bool IsPromotion => (Flags & MoveFlags.Promotion) != 0;
        public bool IsCastle => (Flags & (MoveFlags.KingSideCastle | MoveFlags.QueenSideCastle)) != 0;

        public bool Equals(Move other) =>
            From == other.From && To == other.To && Promotion == other.Promotion && Flags == other.Flags;

        public override bool Equals(object obj) => obj is Move other && Equals(other);
        public override int GetHashCode() => (From << 16) ^ (To << 8) ^ ((int)Promotion << 4) ^ (int)Flags;
        public static bool operator ==(Move a, Move b) => a.Equals(b);
        public static bool operator !=(Move a, Move b) => !a.Equals(b);

        /// <summary>Long algebraic (UCI) form, e.g. "e2e4" or "e7e8q".</summary>
        public string ToUci()
        {
            string text = Square.ToAlgebraic(From) + Square.ToAlgebraic(To);
            if (!IsPromotion) return text;
            switch (Promotion)
            {
                case PieceType.Knight: return text + "n";
                case PieceType.Bishop: return text + "b";
                case PieceType.Rook: return text + "r";
                default: return text + "q";
            }
        }

        public override string ToString() => ToUci();
    }
}
