namespace LightningForge.Arcade.Core.Chess
{
    /// <summary>
    /// Scores a position in centipawns from White's point of view: positive favours White.
    ///
    /// Material plus piece-square tables. The tables are what stop the engine shuffling
    /// aimlessly in the opening: they give knights a reason to come toward the centre,
    /// pawns a reason to advance, and the king a reason to stay tucked away.
    /// </summary>
    public static class Evaluation
    {
        public const int MateScore = 30000;

        public static readonly int[] PieceValues =
        {
            0,      // None
            100,    // Pawn
            320,    // Knight
            330,    // Bishop
            500,    // Rook
            900,    // Queen
            0       // King, handled by mate detection rather than material
        };

        // Tables are written from White's perspective with rank 1 on the first row, and
        // mirrored vertically for Black.
        static readonly int[] PawnTable =
        {
             0,  0,  0,  0,  0,  0,  0,  0,
             5, 10, 10,-20,-20, 10, 10,  5,
             5, -5,-10,  0,  0,-10, -5,  5,
             0,  0,  0, 20, 20,  0,  0,  0,
             5,  5, 10, 25, 25, 10,  5,  5,
            10, 10, 20, 30, 30, 20, 10, 10,
            50, 50, 50, 50, 50, 50, 50, 50,
             0,  0,  0,  0,  0,  0,  0,  0
        };

        static readonly int[] KnightTable =
        {
            -50,-40,-30,-30,-30,-30,-40,-50,
            -40,-20,  0,  5,  5,  0,-20,-40,
            -30,  5, 10, 15, 15, 10,  5,-30,
            -30,  0, 15, 20, 20, 15,  0,-30,
            -30,  5, 15, 20, 20, 15,  5,-30,
            -30,  0, 10, 15, 15, 10,  0,-30,
            -40,-20,  0,  0,  0,  0,-20,-40,
            -50,-40,-30,-30,-30,-30,-40,-50
        };

        static readonly int[] BishopTable =
        {
            -20,-10,-10,-10,-10,-10,-10,-20,
            -10,  5,  0,  0,  0,  0,  5,-10,
            -10, 10, 10, 10, 10, 10, 10,-10,
            -10,  0, 10, 10, 10, 10,  0,-10,
            -10,  5,  5, 10, 10,  5,  5,-10,
            -10,  0,  5, 10, 10,  5,  0,-10,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -20,-10,-10,-10,-10,-10,-10,-20
        };

        static readonly int[] RookTable =
        {
             0,  0,  5, 10, 10,  5,  0,  0,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
            -5,  0,  0,  0,  0,  0,  0, -5,
             5, 10, 10, 10, 10, 10, 10,  5,
             0,  0,  0,  0,  0,  0,  0,  0
        };

        static readonly int[] QueenTable =
        {
            -20,-10,-10, -5, -5,-10,-10,-20,
            -10,  0,  5,  0,  0,  0,  0,-10,
            -10,  5,  5,  5,  5,  5,  0,-10,
              0,  0,  5,  5,  5,  5,  0, -5,
             -5,  0,  5,  5,  5,  5,  0, -5,
            -10,  0,  5,  5,  5,  5,  0,-10,
            -10,  0,  0,  0,  0,  0,  0,-10,
            -20,-10,-10, -5, -5,-10,-10,-20
        };

        static readonly int[] KingTable =
        {
             20, 30, 10,  0,  0, 10, 30, 20,
             20, 20,  0,  0,  0,  0, 20, 20,
            -10,-20,-20,-20,-20,-20,-20,-10,
            -20,-30,-30,-40,-40,-30,-30,-20,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30,
            -30,-40,-40,-50,-50,-40,-40,-30
        };

        /// <summary>Centipawn score from White's point of view.</summary>
        public static int Evaluate(Board board)
        {
            int score = 0;

            for (int square = 0; square < Square.Count; square++)
            {
                Piece piece = board[square];
                if (piece.IsNone) continue;

                int value = PieceValues[(int)piece.Type] + PositionalBonus(piece.Type, square, piece.Color);
                score += piece.Color == PieceColor.White ? value : -value;
            }

            return score;
        }

        static int PositionalBonus(PieceType type, int square, PieceColor color)
        {
            // Black reads the same table from the opposite end of the board.
            int index = color == PieceColor.White
                ? square
                : Square.Of(Square.FileOf(square), 7 - Square.RankOf(square));

            switch (type)
            {
                case PieceType.Pawn: return PawnTable[index];
                case PieceType.Knight: return KnightTable[index];
                case PieceType.Bishop: return BishopTable[index];
                case PieceType.Rook: return RookTable[index];
                case PieceType.Queen: return QueenTable[index];
                case PieceType.King: return KingTable[index];
                default: return 0;
            }
        }
    }
}
