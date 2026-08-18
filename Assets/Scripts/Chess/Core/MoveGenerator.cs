using System.Collections.Generic;

namespace LightningForge.Chess.Core
{
    /// <summary>A file/rank step, used for both attack scanning and move generation.</summary>
    public readonly struct Delta
    {
        public readonly int File;
        public readonly int Rank;

        public Delta(int file, int rank)
        {
            File = file;
            Rank = rank;
        }
    }

    /// <summary>
    /// Generates fully legal moves. Pseudo-legal moves are produced first and then
    /// filtered by playing each one and testing whether it leaves our own king attacked,
    /// which keeps pins, discovered checks and en passant edge cases correct without a
    /// separate pin detector.
    /// </summary>
    public static class MoveGenerator
    {
        public static readonly Delta[] KnightDeltas =
        {
            new Delta(1, 2), new Delta(2, 1), new Delta(2, -1), new Delta(1, -2),
            new Delta(-1, -2), new Delta(-2, -1), new Delta(-2, 1), new Delta(-1, 2)
        };

        public static readonly Delta[] KingDeltas =
        {
            new Delta(0, 1), new Delta(1, 1), new Delta(1, 0), new Delta(1, -1),
            new Delta(0, -1), new Delta(-1, -1), new Delta(-1, 0), new Delta(-1, 1)
        };

        public static readonly Delta[] RookDeltas =
        {
            new Delta(0, 1), new Delta(1, 0), new Delta(0, -1), new Delta(-1, 0)
        };

        public static readonly Delta[] BishopDeltas =
        {
            new Delta(1, 1), new Delta(1, -1), new Delta(-1, -1), new Delta(-1, 1)
        };

        static readonly PieceType[] PromotionPieces =
        {
            PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight
        };

        public static List<Move> GenerateLegalMoves(Board board)
        {
            var moves = new List<Move>(64);
            GenerateLegalMoves(board, moves);
            return moves;
        }

        public static void GenerateLegalMoves(Board board, List<Move> moves)
        {
            moves.Clear();

            var pseudo = new List<Move>(64);
            GeneratePseudoLegalMoves(board, pseudo);

            PieceColor us = board.SideToMove;
            foreach (Move move in pseudo)
            {
                Undo undo = board.MakeMove(move);
                if (!board.IsInCheck(us)) moves.Add(move);
                board.UnmakeMove(move, undo);
            }
        }

        public static void GeneratePseudoLegalMoves(Board board, List<Move> moves)
        {
            PieceColor us = board.SideToMove;

            for (int square = 0; square < Square.Count; square++)
            {
                Piece piece = board[square];
                if (piece.IsNone || piece.Color != us) continue;

                switch (piece.Type)
                {
                    case PieceType.Pawn:
                        GeneratePawnMoves(board, square, us, moves);
                        break;
                    case PieceType.Knight:
                        GenerateStepMoves(board, square, us, KnightDeltas, moves);
                        break;
                    case PieceType.Bishop:
                        GenerateSlidingMoves(board, square, us, BishopDeltas, moves);
                        break;
                    case PieceType.Rook:
                        GenerateSlidingMoves(board, square, us, RookDeltas, moves);
                        break;
                    case PieceType.Queen:
                        GenerateSlidingMoves(board, square, us, RookDeltas, moves);
                        GenerateSlidingMoves(board, square, us, BishopDeltas, moves);
                        break;
                    case PieceType.King:
                        GenerateStepMoves(board, square, us, KingDeltas, moves);
                        GenerateCastles(board, square, us, moves);
                        break;
                }
            }
        }

        static void GeneratePawnMoves(Board board, int from, PieceColor us, List<Move> moves)
        {
            int forward = us == PieceColor.White ? 1 : -1;
            int startRank = us == PieceColor.White ? 1 : 6;
            int promotionRank = us == PieceColor.White ? 7 : 0;

            int oneStep = Square.Offset(from, 0, forward);
            if (oneStep != Square.None && board[oneStep].IsNone)
            {
                AddPawnMove(from, oneStep, MoveFlags.None, promotionRank, moves);

                if (Square.RankOf(from) == startRank)
                {
                    int twoStep = Square.Offset(from, 0, forward * 2);
                    if (twoStep != Square.None && board[twoStep].IsNone)
                    {
                        moves.Add(new Move(from, twoStep, MoveFlags.DoublePawnPush));
                    }
                }
            }

            for (int fileDelta = -1; fileDelta <= 1; fileDelta += 2)
            {
                int target = Square.Offset(from, fileDelta, forward);
                if (target == Square.None) continue;

                Piece occupant = board[target];
                if (occupant.IsSome && occupant.Color != us)
                {
                    AddPawnMove(from, target, MoveFlags.Capture, promotionRank, moves);
                }
                else if (occupant.IsNone && target == board.EnPassantSquare)
                {
                    moves.Add(new Move(from, target, MoveFlags.Capture | MoveFlags.EnPassant));
                }
            }
        }

        static void AddPawnMove(int from, int to, MoveFlags flags, int promotionRank, List<Move> moves)
        {
            if (Square.RankOf(to) == promotionRank)
            {
                foreach (PieceType promotion in PromotionPieces)
                {
                    moves.Add(new Move(from, to, flags | MoveFlags.Promotion, promotion));
                }
            }
            else
            {
                moves.Add(new Move(from, to, flags));
            }
        }

        static void GenerateStepMoves(Board board, int from, PieceColor us, Delta[] deltas, List<Move> moves)
        {
            foreach (var d in deltas)
            {
                int target = Square.Offset(from, d.File, d.Rank);
                if (target == Square.None) continue;

                Piece occupant = board[target];
                if (occupant.IsNone)
                {
                    moves.Add(new Move(from, target));
                }
                else if (occupant.Color != us)
                {
                    moves.Add(new Move(from, target, MoveFlags.Capture));
                }
            }
        }

        static void GenerateSlidingMoves(Board board, int from, PieceColor us, Delta[] deltas, List<Move> moves)
        {
            foreach (var d in deltas)
            {
                int target = from;
                while (true)
                {
                    target = Square.Offset(target, d.File, d.Rank);
                    if (target == Square.None) break;

                    Piece occupant = board[target];
                    if (occupant.IsNone)
                    {
                        moves.Add(new Move(from, target));
                        continue;
                    }

                    if (occupant.Color != us) moves.Add(new Move(from, target, MoveFlags.Capture));
                    break;
                }
            }
        }

        static void GenerateCastles(Board board, int from, PieceColor us, List<Move> moves)
        {
            // Only from the home square, and never out of check.
            int homeSquare = us == PieceColor.White ? 4 : 60;
            if (from != homeSquare) return;

            PieceColor them = us.Opposite();
            if (board.IsAttacked(from, them)) return;

            CastlingRights kingSide = us == PieceColor.White
                ? CastlingRights.WhiteKingSide
                : CastlingRights.BlackKingSide;
            CastlingRights queenSide = us == PieceColor.White
                ? CastlingRights.WhiteQueenSide
                : CastlingRights.BlackQueenSide;

            if ((board.Castling & kingSide) != 0
                && board[from + 1].IsNone
                && board[from + 2].IsNone
                && !board.IsAttacked(from + 1, them))
            {
                moves.Add(new Move(from, from + 2, MoveFlags.KingSideCastle));
            }

            if ((board.Castling & queenSide) != 0
                && board[from - 1].IsNone
                && board[from - 2].IsNone
                && board[from - 3].IsNone
                && !board.IsAttacked(from - 1, them))
            {
                moves.Add(new Move(from, from - 2, MoveFlags.QueenSideCastle));
            }
        }
    }
}
