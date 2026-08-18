namespace LightningForge.Chess.Core
{
    public enum GameStatus
    {
        Ongoing,
        Check,
        Checkmate,
        Stalemate,
        DrawByFiftyMoveRule,
        DrawByInsufficientMaterial
    }

    public static class GameStatusEvaluator
    {
        /// <summary>
        /// Status from the perspective of the side to move. Checkmate and stalemate both
        /// mean "no legal moves"; what separates them is whether the king is attacked.
        /// </summary>
        public static GameStatus Evaluate(Board board)
        {
            bool hasLegalMove = MoveGenerator.GenerateLegalMoves(board).Count > 0;
            bool inCheck = board.IsInCheck(board.SideToMove);

            if (!hasLegalMove) return inCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
            if (HasInsufficientMaterial(board)) return GameStatus.DrawByInsufficientMaterial;
            if (board.HalfmoveClock >= 100) return GameStatus.DrawByFiftyMoveRule;

            return inCheck ? GameStatus.Check : GameStatus.Ongoing;
        }

        public static bool IsGameOver(GameStatus status) =>
            status == GameStatus.Checkmate
            || status == GameStatus.Stalemate
            || status == GameStatus.DrawByFiftyMoveRule
            || status == GameStatus.DrawByInsufficientMaterial;

        /// <summary>
        /// Covers the positions where mate is impossible for either side: bare kings,
        /// king and a single minor piece, and king and bishop against king and bishop
        /// where both bishops share a square colour.
        /// </summary>
        static bool HasInsufficientMaterial(Board board)
        {
            int knights = 0;
            int lightBishops = 0;
            int darkBishops = 0;

            for (int square = 0; square < Square.Count; square++)
            {
                Piece piece = board[square];
                if (piece.IsNone) continue;

                switch (piece.Type)
                {
                    case PieceType.King:
                        break;
                    case PieceType.Knight:
                        knights++;
                        break;
                    case PieceType.Bishop:
                        if (Square.IsLight(square)) lightBishops++;
                        else darkBishops++;
                        break;
                    default:
                        // A pawn, rook or queen anywhere means mate is still possible.
                        return false;
                }
            }

            int bishops = lightBishops + darkBishops;
            if (knights == 0 && bishops == 0) return true;
            if (knights == 1 && bishops == 0) return true;
            if (knights == 0 && bishops == 1) return true;
            if (knights == 0 && (lightBishops == 0 || darkBishops == 0)) return true;

            return false;
        }
    }
}
