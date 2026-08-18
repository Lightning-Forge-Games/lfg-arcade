using System;
using System.Text;

namespace LightningForge.Chess.Core
{
    /// <summary>State needed to reverse a move. Produced by <see cref="Board.MakeMove"/>.</summary>
    public readonly struct Undo
    {
        public readonly Piece Captured;
        public readonly CastlingRights Castling;
        public readonly int EnPassantSquare;
        public readonly int HalfmoveClock;

        public Undo(Piece captured, CastlingRights castling, int enPassantSquare, int halfmoveClock)
        {
            Captured = captured;
            Castling = castling;
            EnPassantSquare = enPassantSquare;
            HalfmoveClock = halfmoveClock;
        }
    }

    /// <summary>
    /// Mutable chess position. Deliberately free of any UnityEngine dependency so the
    /// rules can be exercised outside the editor.
    /// </summary>
    public sealed class Board
    {
        public const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

        const int A1 = 0, E1 = 4, H1 = 7, A8 = 56, E8 = 60, H8 = 63;

        /// <summary>
        /// Rights surviving a move touching each square. Applied to both origin and
        /// destination, which handles a rook being captured on its home square as well
        /// as a king or rook moving off one.
        /// </summary>
        static readonly CastlingRights[] RightsMask = BuildRightsMask();

        readonly Piece[] squares = new Piece[Square.Count];
        readonly int[] kingSquare = { Square.None, Square.None };

        public PieceColor SideToMove { get; private set; }
        public CastlingRights Castling { get; private set; }
        public int EnPassantSquare { get; private set; }
        public int HalfmoveClock { get; private set; }
        public int FullmoveNumber { get; private set; }

        public Board() : this(StartFen) { }

        public Board(string fen)
        {
            LoadFen(fen);
        }

        public Piece this[int square] => squares[square];

        public int KingSquare(PieceColor color) => kingSquare[(int)color];

        static CastlingRights[] BuildRightsMask()
        {
            var mask = new CastlingRights[Square.Count];
            for (int i = 0; i < mask.Length; i++) mask[i] = CastlingRights.All;

            mask[A1] &= ~CastlingRights.WhiteQueenSide;
            mask[H1] &= ~CastlingRights.WhiteKingSide;
            mask[E1] &= ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide);
            mask[A8] &= ~CastlingRights.BlackQueenSide;
            mask[H8] &= ~CastlingRights.BlackKingSide;
            mask[E8] &= ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
            return mask;
        }

        public void LoadFen(string fen)
        {
            if (string.IsNullOrWhiteSpace(fen)) throw new ArgumentException("FEN is empty.", nameof(fen));

            string[] parts = fen.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4) throw new ArgumentException("FEN needs at least four fields.", nameof(fen));

            Array.Clear(squares, 0, squares.Length);
            kingSquare[0] = kingSquare[1] = Square.None;

            int rank = 7;
            int file = 0;
            foreach (char c in parts[0])
            {
                if (c == '/')
                {
                    rank--;
                    file = 0;
                }
                else if (char.IsDigit(c))
                {
                    file += c - '0';
                }
                else
                {
                    Piece piece = Piece.FromFenChar(c);
                    if (piece.IsNone) throw new ArgumentException($"Unknown piece '{c}' in FEN.", nameof(fen));
                    int square = Square.Of(file, rank);
                    squares[square] = piece;
                    if (piece.Type == PieceType.King) kingSquare[(int)piece.Color] = square;
                    file++;
                }
            }

            SideToMove = parts[1] == "b" ? PieceColor.Black : PieceColor.White;

            Castling = CastlingRights.None;
            if (parts[2] != "-")
            {
                foreach (char c in parts[2])
                {
                    switch (c)
                    {
                        case 'K': Castling |= CastlingRights.WhiteKingSide; break;
                        case 'Q': Castling |= CastlingRights.WhiteQueenSide; break;
                        case 'k': Castling |= CastlingRights.BlackKingSide; break;
                        case 'q': Castling |= CastlingRights.BlackQueenSide; break;
                    }
                }
            }

            EnPassantSquare = parts[3] == "-" ? Square.None : Square.FromAlgebraic(parts[3]);
            HalfmoveClock = parts.Length > 4 && int.TryParse(parts[4], out int half) ? half : 0;
            FullmoveNumber = parts.Length > 5 && int.TryParse(parts[5], out int full) ? full : 1;
        }

        public string ToFen()
        {
            var sb = new StringBuilder();

            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    Piece piece = squares[Square.Of(file, rank)];
                    if (piece.IsNone)
                    {
                        empty++;
                        continue;
                    }
                    if (empty > 0)
                    {
                        sb.Append(empty);
                        empty = 0;
                    }
                    sb.Append(piece.ToFenChar());
                }
                if (empty > 0) sb.Append(empty);
                if (rank > 0) sb.Append('/');
            }

            sb.Append(SideToMove == PieceColor.White ? " w " : " b ");

            if (Castling == CastlingRights.None)
            {
                sb.Append('-');
            }
            else
            {
                if ((Castling & CastlingRights.WhiteKingSide) != 0) sb.Append('K');
                if ((Castling & CastlingRights.WhiteQueenSide) != 0) sb.Append('Q');
                if ((Castling & CastlingRights.BlackKingSide) != 0) sb.Append('k');
                if ((Castling & CastlingRights.BlackQueenSide) != 0) sb.Append('q');
            }

            sb.Append(' ').Append(EnPassantSquare == Square.None ? "-" : Square.ToAlgebraic(EnPassantSquare));
            sb.Append(' ').Append(HalfmoveClock);
            sb.Append(' ').Append(FullmoveNumber);
            return sb.ToString();
        }

        public Undo MakeMove(Move move)
        {
            PieceColor us = SideToMove;
            PieceColor them = us.Opposite();
            Piece moving = squares[move.From];

            int captureSquare = move.IsEnPassant
                ? move.To + (us == PieceColor.White ? -8 : 8)
                : move.To;

            var undo = new Undo(squares[captureSquare], Castling, EnPassantSquare, HalfmoveClock);

            squares[captureSquare] = Piece.None;
            squares[move.From] = Piece.None;
            squares[move.To] = move.IsPromotion ? new Piece(move.Promotion, us) : moving;

            if (moving.Type == PieceType.King)
            {
                kingSquare[(int)us] = move.To;

                if ((move.Flags & MoveFlags.KingSideCastle) != 0)
                {
                    int rookFrom = us == PieceColor.White ? H1 : H8;
                    squares[rookFrom - 2] = squares[rookFrom];
                    squares[rookFrom] = Piece.None;
                }
                else if ((move.Flags & MoveFlags.QueenSideCastle) != 0)
                {
                    int rookFrom = us == PieceColor.White ? A1 : A8;
                    squares[rookFrom + 3] = squares[rookFrom];
                    squares[rookFrom] = Piece.None;
                }
            }

            Castling &= RightsMask[move.From] & RightsMask[move.To];

            EnPassantSquare = (move.Flags & MoveFlags.DoublePawnPush) != 0
                ? (move.From + move.To) / 2
                : Square.None;

            HalfmoveClock = moving.Type == PieceType.Pawn || !undo.Captured.IsNone ? 0 : HalfmoveClock + 1;
            if (us == PieceColor.Black) FullmoveNumber++;
            SideToMove = them;

            return undo;
        }

        public void UnmakeMove(Move move, Undo undo)
        {
            PieceColor us = SideToMove.Opposite();

            SideToMove = us;
            if (us == PieceColor.Black) FullmoveNumber--;
            Castling = undo.Castling;
            EnPassantSquare = undo.EnPassantSquare;
            HalfmoveClock = undo.HalfmoveClock;

            Piece moved = squares[move.To];
            squares[move.From] = move.IsPromotion ? new Piece(PieceType.Pawn, us) : moved;
            squares[move.To] = Piece.None;

            if (moved.Type == PieceType.King)
            {
                kingSquare[(int)us] = move.From;

                if ((move.Flags & MoveFlags.KingSideCastle) != 0)
                {
                    int rookFrom = us == PieceColor.White ? H1 : H8;
                    squares[rookFrom] = squares[rookFrom - 2];
                    squares[rookFrom - 2] = Piece.None;
                }
                else if ((move.Flags & MoveFlags.QueenSideCastle) != 0)
                {
                    int rookFrom = us == PieceColor.White ? A1 : A8;
                    squares[rookFrom] = squares[rookFrom + 3];
                    squares[rookFrom + 3] = Piece.None;
                }
            }

            int captureSquare = move.IsEnPassant
                ? move.To + (us == PieceColor.White ? -8 : 8)
                : move.To;
            squares[captureSquare] = undo.Captured;
        }

        public bool IsInCheck(PieceColor color) => IsAttacked(kingSquare[(int)color], color.Opposite());

        /// <summary>True when <paramref name="attacker"/> attacks <paramref name="square"/>.</summary>
        public bool IsAttacked(int square, PieceColor attacker)
        {
            if (!Square.IsValid(square)) return false;

            // A pawn one rank "behind" the target diagonally is the one attacking it.
            int pawnRankDelta = attacker == PieceColor.White ? -1 : 1;
            for (int fileDelta = -1; fileDelta <= 1; fileDelta += 2)
            {
                int from = Square.Offset(square, fileDelta, pawnRankDelta);
                if (from != Square.None && squares[from].Is(PieceType.Pawn, attacker)) return true;
            }

            foreach (var d in MoveGenerator.KnightDeltas)
            {
                int from = Square.Offset(square, d.File, d.Rank);
                if (from != Square.None && squares[from].Is(PieceType.Knight, attacker)) return true;
            }

            foreach (var d in MoveGenerator.KingDeltas)
            {
                int from = Square.Offset(square, d.File, d.Rank);
                if (from != Square.None && squares[from].Is(PieceType.King, attacker)) return true;
            }

            if (ScanForSlider(square, MoveGenerator.RookDeltas, PieceType.Rook, attacker)) return true;
            if (ScanForSlider(square, MoveGenerator.BishopDeltas, PieceType.Bishop, attacker)) return true;

            return false;
        }

        bool ScanForSlider(int square, Delta[] deltas, PieceType straightType, PieceColor attacker)
        {
            foreach (var d in deltas)
            {
                int current = square;
                while (true)
                {
                    current = Square.Offset(current, d.File, d.Rank);
                    if (current == Square.None) break;

                    Piece piece = squares[current];
                    if (piece.IsNone) continue;

                    if (piece.Color == attacker && (piece.Type == straightType || piece.Type == PieceType.Queen))
                        return true;

                    break;
                }
            }
            return false;
        }

        public Board Clone() => new Board(ToFen());

        /// <summary>
        /// Finds a legal move by its UCI string, e.g. "e2e4" or "e7e8q". Convenience for
        /// tests, scripted openings and engine integration; not for hot paths.
        /// </summary>
        public bool TryFindMove(string uci, out Move move)
        {
            foreach (Move candidate in MoveGenerator.GenerateLegalMoves(this))
            {
                if (candidate.ToUci() == uci)
                {
                    move = candidate;
                    return true;
                }
            }

            move = Move.None;
            return false;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                sb.Append(rank + 1).Append(' ');
                for (int file = 0; file < 8; file++)
                {
                    sb.Append(squares[Square.Of(file, rank)].ToFenChar()).Append(' ');
                }
                sb.AppendLine();
            }
            sb.AppendLine("  a b c d e f g h");
            sb.Append(SideToMove == PieceColor.White ? "White" : "Black").Append(" to move");
            return sb.ToString();
        }
    }
}
