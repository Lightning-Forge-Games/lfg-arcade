using System.Collections.Generic;
using System.Text;

namespace LightningForge.Chess.Core
{
    /// <summary>
    /// Writes moves in Standard Algebraic Notation, the form used in every chess book and
    /// database ("Nf3", "exd5", "O-O", "Qh4#").
    ///
    /// SAN is context sensitive, which is why this lives beside the rules rather than in
    /// the UI: naming a move needs the full legal move list to know whether the piece must
    /// be disambiguated, and needs the resulting position to know whether to append check
    /// or mate.
    /// </summary>
    public static class SanWriter
    {
        /// <summary>
        /// Names <paramref name="move"/> in the position <paramref name="board"/>. The
        /// board is restored before returning.
        /// </summary>
        public static string ToSan(Board board, Move move)
        {
            if ((move.Flags & MoveFlags.KingSideCastle) != 0) return WithSuffix(board, move, "O-O");
            if ((move.Flags & MoveFlags.QueenSideCastle) != 0) return WithSuffix(board, move, "O-O-O");

            Piece piece = board[move.From];
            var sb = new StringBuilder(8);

            if (piece.Type == PieceType.Pawn)
            {
                // A capturing pawn is named by the file it left: "exd5".
                if (move.IsCapture)
                {
                    sb.Append((char)('a' + Square.FileOf(move.From)));
                    sb.Append('x');
                }
                sb.Append(Square.ToAlgebraic(move.To));

                if (move.IsPromotion)
                {
                    sb.Append('=');
                    sb.Append(LetterFor(move.Promotion));
                }
            }
            else
            {
                sb.Append(LetterFor(piece.Type));
                sb.Append(Disambiguation(board, move, piece.Type));
                if (move.IsCapture) sb.Append('x');
                sb.Append(Square.ToAlgebraic(move.To));
            }

            return WithSuffix(board, move, sb.ToString());
        }

        /// <summary>
        /// Works out the least notation needed to identify which piece moved. Only pieces
        /// of the same type that could legally reach the same square matter.
        /// </summary>
        static string Disambiguation(Board board, Move move, PieceType type)
        {
            List<Move> legal = MoveGenerator.GenerateLegalMoves(board);

            bool anyOther = false;
            bool sameFile = false;
            bool sameRank = false;

            foreach (Move other in legal)
            {
                if (other.From == move.From) continue;
                if (other.To != move.To) continue;
                if (board[other.From].Type != type) continue;

                anyOther = true;
                if (Square.FileOf(other.From) == Square.FileOf(move.From)) sameFile = true;
                if (Square.RankOf(other.From) == Square.RankOf(move.From)) sameRank = true;
            }

            if (!anyOther) return string.Empty;

            // File alone is preferred, then rank, then the whole square.
            if (!sameFile) return ((char)('a' + Square.FileOf(move.From))).ToString();
            if (!sameRank) return ((char)('1' + Square.RankOf(move.From))).ToString();
            return Square.ToAlgebraic(move.From);
        }

        /// <summary>Appends '+' for check or '#' for mate by briefly playing the move.</summary>
        static string WithSuffix(Board board, Move move, string text)
        {
            Undo undo = board.MakeMove(move);
            bool inCheck = board.IsInCheck(board.SideToMove);
            bool hasReply = MoveGenerator.GenerateLegalMoves(board).Count > 0;
            board.UnmakeMove(move, undo);

            if (inCheck) return text + (hasReply ? "+" : "#");
            return text;
        }

        static char LetterFor(PieceType type)
        {
            switch (type)
            {
                case PieceType.Knight: return 'N';
                case PieceType.Bishop: return 'B';
                case PieceType.Rook: return 'R';
                case PieceType.Queen: return 'Q';
                case PieceType.King: return 'K';
                default: return 'P';
            }
        }
    }
}
