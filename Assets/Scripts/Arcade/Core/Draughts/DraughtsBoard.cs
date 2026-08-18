using System.Collections.Generic;
using System.Text;

namespace LightningForge.Arcade.Core.Draughts
{
    public enum DraughtsSide : byte
    {
        None = 0,
        White = 1,
        Black = 2,
    }

    public enum DraughtsStatus
    {
        Ongoing,
        WhiteWins,
        BlackWins,

        /// <summary>Neither side has made progress for long enough to call it.</summary>
        Draw,
    }

    /// <summary>A man or a king. Kings move and capture backwards as well as forwards.</summary>
    public struct DraughtsPiece
    {
        public DraughtsSide Side;
        public bool IsKing;

        public bool IsNone => Side == DraughtsSide.None;
        public static readonly DraughtsPiece None = new DraughtsPiece();
    }

    /// <summary>
    /// A draughts move: where it started, every square it landed on, and what it took.
    ///
    /// A move is a path rather than a from and a to, because a multiple jump is one move
    /// however many pieces it takes, and the squares in between are what the board has to
    /// animate through and what the player has to click.
    /// </summary>
    public struct DraughtsMove
    {
        public int From;
        public int[] Path;
        public int[] Captured;
        public bool Crowns;

        public int To => Path[Path.Length - 1];
        public bool IsCapture => Captured != null && Captured.Length > 0;

        /// <summary>
        /// The move as the squares it visits, "c3d4" or "c3e5g7" for a double jump. Compact,
        /// readable in a log, and unambiguous because a path names every landing square.
        /// </summary>
        public string ToNotation()
        {
            var sb = new StringBuilder(Square.ToAlgebraic(From));
            foreach (int square in Path) sb.Append(Square.ToAlgebraic(square));
            return sb.ToString();
        }
    }

    /// <summary>
    /// English draughts on the dark squares of a standard eight by eight board.
    ///
    /// The rules that matter and are easy to get wrong: captures are compulsory, and if a
    /// jump can be continued it must be, so a move is a whole chain. Men crown on reaching
    /// the far rank and the turn ends there even if the new king could jump again. Kings
    /// step one square, in any of the four diagonal directions; this is not the flying king
    /// of international draughts.
    ///
    /// White moves first and moves up the board, which lines up with White taking the first
    /// seat everywhere else in the arcade.
    /// </summary>
    public sealed class DraughtsBoard
    {
        /// <summary>Moves without a capture or a crowning before the game is called a draw.</summary>
        public const int DrawThreshold = 80;

        static readonly int[] FileSteps = { -1, 1, -1, 1 };
        static readonly int[] RankSteps = { 1, 1, -1, -1 };

        readonly DraughtsPiece[] squares = new DraughtsPiece[Square.Count];

        public DraughtsBoard()
        {
            Reset();
        }

        public DraughtsSide SideToMove { get; private set; }
        public DraughtsStatus Status { get; private set; }

        /// <summary>Plies since the last capture or crowning, for the draw rule.</summary>
        public int QuietPlies { get; private set; }

        public DraughtsPiece this[int square] => squares[square];

        /// <summary>Draughts is played on the dark squares only.</summary>
        public static bool IsPlayable(int square) =>
            Square.IsValid(square) && !Square.IsLight(square);

        public static DraughtsSide Opponent(DraughtsSide side) =>
            side == DraughtsSide.White ? DraughtsSide.Black : DraughtsSide.White;

        public static bool IsOver(DraughtsStatus status) => status != DraughtsStatus.Ongoing;

        public void Reset()
        {
            for (int i = 0; i < Square.Count; i++) squares[i] = DraughtsPiece.None;

            // Three rows each, on the dark squares, leaving the middle two rows empty.
            for (int square = 0; square < Square.Count; square++)
            {
                if (!IsPlayable(square)) continue;

                int rank = Square.RankOf(square);
                if (rank <= 2) squares[square] = new DraughtsPiece { Side = DraughtsSide.White };
                else if (rank >= 5) squares[square] = new DraughtsPiece { Side = DraughtsSide.Black };
            }

            SideToMove = DraughtsSide.White;
            Status = DraughtsStatus.Ongoing;
            QuietPlies = 0;
        }

        public int CountPieces(DraughtsSide side)
        {
            int count = 0;
            for (int square = 0; square < Square.Count; square++)
            {
                if (squares[square].Side == side) count++;
            }
            return count;
        }

        // Move generation -----------------------------------------------------------

        /// <summary>
        /// Every legal move for the side to move.
        ///
        /// Captures are compulsory, so if any exist the quiet moves are never offered. That
        /// single rule is most of what makes draughts draughts.
        /// </summary>
        public List<DraughtsMove> GenerateMoves()
        {
            var captures = new List<DraughtsMove>();
            var quiet = new List<DraughtsMove>();

            for (int square = 0; square < Square.Count; square++)
            {
                DraughtsPiece piece = squares[square];
                if (piece.Side != SideToMove) continue;

                CollectJumps(square, piece, captures);
                if (captures.Count == 0) CollectSteps(square, piece, quiet);
            }

            // A capture found late must still suppress the quiet moves found early.
            return captures.Count > 0 ? captures : quiet;
        }

        void CollectSteps(int from, DraughtsPiece piece, List<DraughtsMove> into)
        {
            for (int d = 0; d < 4; d++)
            {
                if (!CanGo(piece, d)) continue;

                int to = Square.Offset(from, FileSteps[d], RankSteps[d]);
                if (to == Square.None || !squares[to].IsNone) continue;

                into.Add(new DraughtsMove
                {
                    From = from,
                    Path = new[] { to },
                    Captured = null,
                    Crowns = CrownsOn(piece, to),
                });
            }
        }

        void CollectJumps(int from, DraughtsPiece piece, List<DraughtsMove> into)
        {
            ExtendJump(from, from, piece, new List<int>(), new List<int>(), into);
        }

        /// <summary>
        /// Walks a jump as far as it can go, recording only the longest chains.
        ///
        /// A jump that can be continued must be, so a partial chain is not a legal move and
        /// is never recorded. Crowning ends the turn, so a man that lands on the far rank
        /// stops there even if the king it becomes could jump again.
        /// </summary>
        void ExtendJump(int origin, int current, DraughtsPiece piece,
            List<int> path, List<int> captured, List<DraughtsMove> into)
        {
            bool extended = false;

            for (int d = 0; d < 4; d++)
            {
                if (!CanGo(piece, d)) continue;

                int over = Square.Offset(current, FileSteps[d], RankSteps[d]);
                int landing = Square.Offset(current, FileSteps[d] * 2, RankSteps[d] * 2);
                if (over == Square.None || landing == Square.None) continue;

                DraughtsPiece victim = squares[over];
                if (victim.IsNone || victim.Side == piece.Side) continue;

                // A piece already jumped in this chain cannot be jumped again.
                if (captured.Contains(over)) continue;
                if (!squares[landing].IsNone && landing != origin) continue;

                extended = true;
                path.Add(landing);
                captured.Add(over);

                bool crowns = CrownsOn(piece, landing);
                if (crowns)
                {
                    // Crowning ends the move, king or not.
                    Record(origin, path, captured, true, into);
                }
                else
                {
                    ExtendJump(origin, landing, piece, path, captured, into);
                }

                path.RemoveAt(path.Count - 1);
                captured.RemoveAt(captured.Count - 1);
            }

            // Nothing further from here, so the chain so far is a complete move.
            if (!extended && path.Count > 0) Record(origin, path, captured, false, into);
        }

        static void Record(int from, List<int> path, List<int> captured, bool crowns,
            List<DraughtsMove> into)
        {
            into.Add(new DraughtsMove
            {
                From = from,
                Path = path.ToArray(),
                Captured = captured.ToArray(),
                Crowns = crowns,
            });
        }

        /// <summary>Men move only forwards; kings move in all four diagonal directions.</summary>
        bool CanGo(DraughtsPiece piece, int direction)
        {
            if (piece.IsKing) return true;
            bool forward = direction < 2;
            return piece.Side == DraughtsSide.White ? forward : !forward;
        }

        static bool CrownsOn(DraughtsPiece piece, int square)
        {
            if (piece.IsKing) return false;
            int rank = Square.RankOf(square);
            return piece.Side == DraughtsSide.White ? rank == 7 : rank == 0;
        }

        // Making moves --------------------------------------------------------------

        public bool TryFindMove(string notation, out DraughtsMove move)
        {
            foreach (DraughtsMove candidate in GenerateMoves())
            {
                if (candidate.ToNotation() == notation)
                {
                    move = candidate;
                    return true;
                }
            }
            move = default;
            return false;
        }

        public void Play(DraughtsMove move)
        {
            DraughtsPiece piece = squares[move.From];
            squares[move.From] = DraughtsPiece.None;

            if (move.Captured != null)
            {
                foreach (int square in move.Captured) squares[square] = DraughtsPiece.None;
            }

            if (move.Crowns) piece.IsKing = true;
            squares[move.To] = piece;

            QuietPlies = move.IsCapture || move.Crowns ? 0 : QuietPlies + 1;

            SideToMove = Opponent(SideToMove);
            UpdateStatus();
        }

        /// <summary>
        /// A side with no legal move loses, whether that is because it has no pieces left or
        /// because everything it owns is blocked. Both are losses in draughts, unlike chess
        /// where being unable to move is a draw.
        /// </summary>
        void UpdateStatus()
        {
            if (GenerateMoves().Count == 0)
            {
                Status = SideToMove == DraughtsSide.White
                    ? DraughtsStatus.BlackWins
                    : DraughtsStatus.WhiteWins;
                return;
            }

            Status = QuietPlies >= DrawThreshold ? DraughtsStatus.Draw : DraughtsStatus.Ongoing;
        }

        public DraughtsBoard Clone()
        {
            var copy = new DraughtsBoard();
            for (int i = 0; i < Square.Count; i++) copy.squares[i] = squares[i];
            copy.SideToMove = SideToMove;
            copy.Status = Status;
            copy.QuietPlies = QuietPlies;
            return copy;
        }

        /// <summary>
        /// The board as eight rows, top rank first, so it reads the way it looks. Lower case
        /// is a man, upper case a king, and a dot is an empty playable square.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                for (int file = 0; file < 8; file++)
                {
                    int square = Square.Of(file, rank);
                    DraughtsPiece piece = squares[square];
                    if (piece.IsNone) sb.Append(IsPlayable(square) ? '.' : ' ');
                    else
                    {
                        char c = piece.Side == DraughtsSide.White ? 'w' : 'b';
                        sb.Append(piece.IsKing ? char.ToUpperInvariant(c) : c);
                    }
                }
                if (rank > 0) sb.Append('/');
            }
            return sb.ToString();
        }

        /// <summary>Builds a position from the text <see cref="ToString"/> produces.</summary>
        public static DraughtsBoard Parse(string text, DraughtsSide sideToMove = DraughtsSide.White)
        {
            var board = new DraughtsBoard();
            for (int i = 0; i < Square.Count; i++) board.squares[i] = DraughtsPiece.None;

            string[] rows = text.Split('/');
            for (int i = 0; i < rows.Length && i < 8; i++)
            {
                int rank = 7 - i;
                for (int file = 0; file < rows[i].Length && file < 8; file++)
                {
                    char c = rows[i][file];
                    if (c == '.' || c == ' ') continue;

                    board.squares[Square.Of(file, rank)] = new DraughtsPiece
                    {
                        Side = char.ToLowerInvariant(c) == 'w' ? DraughtsSide.White : DraughtsSide.Black,
                        IsKing = char.IsUpper(c),
                    };
                }
            }

            board.SideToMove = sideToMove;
            board.QuietPlies = 0;
            board.UpdateStatus();
            return board;
        }
    }
}
