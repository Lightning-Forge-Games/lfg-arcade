using System;

namespace LightningForge.Arcade.Core
{
    /// <summary>
    /// Square index helpers. Squares are 0..63 with 0 = a1 and 63 = h8, so
    /// index = rank * 8 + file, where file 0 = 'a' and rank 0 = rank 1.
    /// </summary>
    public static class Square
    {
        public const int None = -1;
        public const int Count = 64;

        public static int Of(int file, int rank) => rank * 8 + file;
        public static int FileOf(int square) => square & 7;
        public static int RankOf(int square) => square >> 3;
        public static bool IsValid(int square) => square >= 0 && square < Count;

        public static bool IsLight(int square) => ((FileOf(square) + RankOf(square)) & 1) != 0;

        public static int FromAlgebraic(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 2) return None;
            int file = char.ToLowerInvariant(text[0]) - 'a';
            int rank = text[1] - '1';
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return None;
            return Of(file, rank);
        }

        public static string ToAlgebraic(int square)
        {
            if (!IsValid(square)) return "-";
            return string.Concat((char)('a' + FileOf(square)), (char)('1' + RankOf(square)));
        }

        /// <summary>
        /// Offsets a square by a file/rank delta, returning <see cref="None"/> if the
        /// result falls off the board. Going through file/rank rather than raw index
        /// arithmetic is what keeps knight and slider moves from wrapping around edges.
        /// </summary>
        public static int Offset(int square, int fileDelta, int rankDelta)
        {
            int file = FileOf(square) + fileDelta;
            int rank = RankOf(square) + rankDelta;
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return None;
            return Of(file, rank);
        }
    }
}
