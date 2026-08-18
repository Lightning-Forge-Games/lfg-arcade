using System;
using System.Collections.Generic;
using System.Text;

namespace LightningForge.Arcade.Core.Backgammon
{
    public enum BackgammonSide : byte
    {
        None = 0,
        White = 1,
        Black = 2,
    }

    public enum BackgammonStatus
    {
        Ongoing,
        WhiteWins,
        BlackWins,
    }

    /// <summary>One of the twenty four points, holding any number of one side's checkers.</summary>
    public struct BackgammonPoint
    {
        public BackgammonSide Side;
        public int Count;

        public bool IsEmpty => Count == 0;
    }

    /// <summary>
    /// A single checker move within a turn.
    ///
    /// <see cref="From"/> is <see cref="Bar"/> when entering from the bar, and
    /// <see cref="To"/> is <see cref="Off"/> when bearing off, so one shape covers all
    /// three kinds of move and nothing downstream needs to special case them.
    /// </summary>
    public struct BackgammonMove
    {
        public const int Bar = -1;
        public const int Off = 24;

        public int From;
        public int To;
        public int Die;
        public bool Hits;

        public override string ToString() =>
            (From == Bar ? "bar" : From.ToString()) + "-" + (To == Off ? "off" : To.ToString());
    }

    /// <summary>
    /// Backgammon: twenty four points, a bar, and two homes to bear off into.
    ///
    /// White runs up the numbers and bears off past point 23; Black runs down and bears off
    /// past point 0. Keeping both directions in one board rather than mirroring one side
    /// means every rule is written once, at the cost of a direction term in the arithmetic.
    ///
    /// The rule that makes backgammon hard to implement is that a player must use as many
    /// dice as they can, and must use the higher die if only one can be played. That cannot
    /// be decided move by move, because whether a die is playable depends on what was
    /// played before it. Turns are therefore generated whole and then filtered, rather than
    /// legality being checked one move at a time.
    /// </summary>
    public sealed class BackgammonBoard
    {
        public const int Points = 24;
        public const int CheckersPerSide = 15;

        readonly BackgammonPoint[] points = new BackgammonPoint[Points];
        readonly int[] bar = new int[3];
        readonly int[] off = new int[3];

        public BackgammonBoard()
        {
            Reset();
        }

        public BackgammonSide SideToMove { get; set; }
        public BackgammonStatus Status { get; private set; }

        public BackgammonPoint this[int point] => points[point];
        public int Bar(BackgammonSide side) => bar[(int)side];
        public int Off(BackgammonSide side) => off[(int)side];

        public static BackgammonSide Opponent(BackgammonSide side) =>
            side == BackgammonSide.White ? BackgammonSide.Black : BackgammonSide.White;

        public static bool IsOver(BackgammonStatus status) => status != BackgammonStatus.Ongoing;

        /// <summary>White counts up, Black counts down.</summary>
        public static int DirectionOf(BackgammonSide side) => side == BackgammonSide.White ? 1 : -1;

        public void Reset()
        {
            for (int i = 0; i < Points; i++) points[i] = new BackgammonPoint();
            for (int i = 0; i < 3; i++) { bar[i] = 0; off[i] = 0; }

            // The standard opening: two, five, three and five, mirrored.
            Place(0, BackgammonSide.White, 2);
            Place(11, BackgammonSide.White, 5);
            Place(16, BackgammonSide.White, 3);
            Place(18, BackgammonSide.White, 5);

            Place(23, BackgammonSide.Black, 2);
            Place(12, BackgammonSide.Black, 5);
            Place(7, BackgammonSide.Black, 3);
            Place(5, BackgammonSide.Black, 5);

            SideToMove = BackgammonSide.White;
            Status = BackgammonStatus.Ongoing;
        }

        void Place(int point, BackgammonSide side, int count)
        {
            points[point] = new BackgammonPoint { Side = side, Count = count };
        }

        /// <summary>The six points a side bears off from.</summary>
        public static bool IsHome(BackgammonSide side, int point) =>
            side == BackgammonSide.White ? point >= 18 : point <= 5;

        /// <summary>Bearing off is only legal once every checker is home.</summary>
        public bool AllHome(BackgammonSide side)
        {
            if (bar[(int)side] > 0) return false;

            for (int i = 0; i < Points; i++)
            {
                if (points[i].Side == side && points[i].Count > 0 && !IsHome(side, i)) return false;
            }
            return true;
        }

        /// <summary>
        /// How far a side's rearmost checker still has to travel. Used to decide whether a
        /// die larger than any occupied point may bear off, and by the opponent to judge
        /// who is ahead in the race.
        /// </summary>
        public int Pip(BackgammonSide side)
        {
            int total = bar[(int)side] * 25;
            for (int i = 0; i < Points; i++)
            {
                if (points[i].Side != side || points[i].Count == 0) continue;
                int distance = side == BackgammonSide.White ? 24 - i : i + 1;
                total += distance * points[i].Count;
            }
            return total;
        }

        // Turn generation -----------------------------------------------------------

        /// <summary>The dice available from a roll. Doubles are played four times.</summary>
        public static int[] DiceFor(int first, int second) =>
            first == second
                ? new[] { first, first, first, first }
                : new[] { first, second };

        /// <summary>
        /// Every legal way to play the roll, already filtered to the sequences that use the
        /// most dice. When only one die can be played and either would do on its own, only
        /// the higher one is offered, as the rules require.
        /// </summary>
        public List<List<BackgammonMove>> GenerateTurns(int first, int second)
        {
            var results = new List<List<BackgammonMove>>();
            int[] dice = DiceFor(first, second);

            Explore(this, dice, new bool[dice.Length], new List<BackgammonMove>(), results);

            if (results.Count == 0) return results;

            int longest = 0;
            foreach (List<BackgammonMove> turn in results)
            {
                if (turn.Count > longest) longest = turn.Count;
            }

            var best = new List<List<BackgammonMove>>();
            foreach (List<BackgammonMove> turn in results)
            {
                if (turn.Count == longest) best.Add(turn);
            }

            // With exactly one die playable, the larger must be used if that is a choice.
            if (longest == 1 && first != second)
            {
                int larger = Math.Max(first, second);
                var forced = new List<List<BackgammonMove>>();
                foreach (List<BackgammonMove> turn in best)
                {
                    if (turn[0].Die == larger) forced.Add(turn);
                }
                if (forced.Count > 0) return Deduplicate(forced);
            }

            return Deduplicate(best);
        }

        /// <summary>
        /// Two orderings of the same two dice often reach the same position. Offering both
        /// would show the player duplicate choices that do exactly the same thing.
        /// </summary>
        static List<List<BackgammonMove>> Deduplicate(List<List<BackgammonMove>> turns)
        {
            var seen = new HashSet<string>();
            var unique = new List<List<BackgammonMove>>();

            foreach (List<BackgammonMove> turn in turns)
            {
                var key = new StringBuilder();
                foreach (BackgammonMove move in turn) key.Append(move).Append(',');
                if (seen.Add(key.ToString())) unique.Add(turn);
            }
            return unique;
        }

        static void Explore(BackgammonBoard board, int[] dice, bool[] used,
            List<BackgammonMove> sequence, List<List<BackgammonMove>> results)
        {
            bool anyPlayed = false;

            for (int i = 0; i < dice.Length; i++)
            {
                if (used[i]) continue;

                // With doubles every die is the same, so trying each index repeats work.
                bool duplicateIndex = false;
                for (int j = 0; j < i; j++)
                {
                    if (!used[j] && dice[j] == dice[i]) { duplicateIndex = true; break; }
                }
                if (duplicateIndex) continue;

                foreach (BackgammonMove move in board.MovesForDie(dice[i]))
                {
                    used[i] = true;
                    sequence.Add(move);

                    BackgammonBoard next = board.Clone();
                    next.ApplyMove(move);
                    anyPlayed = true;
                    Explore(next, dice, used, sequence, results);

                    sequence.RemoveAt(sequence.Count - 1);
                    used[i] = false;
                }
            }

            // A sequence that cannot be extended is a complete turn, including the empty
            // one when the roll cannot be played at all.
            if (!anyPlayed) results.Add(new List<BackgammonMove>(sequence));
        }

        /// <summary>Every single move one die allows right now.</summary>
        public List<BackgammonMove> MovesForDie(int die)
        {
            var moves = new List<BackgammonMove>();
            BackgammonSide side = SideToMove;
            int direction = DirectionOf(side);

            // Checkers on the bar must come in before anything else may move.
            if (bar[(int)side] > 0)
            {
                int entry = side == BackgammonSide.White ? die - 1 : Points - die;
                if (CanLand(side, entry))
                {
                    moves.Add(new BackgammonMove
                    {
                        From = BackgammonMove.Bar,
                        To = entry,
                        Die = die,
                        Hits = IsBlot(side, entry),
                    });
                }
                return moves;
            }

            for (int from = 0; from < Points; from++)
            {
                if (points[from].Side != side || points[from].Count == 0) continue;

                int to = from + die * direction;
                if (to >= 0 && to < Points)
                {
                    if (CanLand(side, to))
                    {
                        moves.Add(new BackgammonMove
                        {
                            From = from,
                            To = to,
                            Die = die,
                            Hits = IsBlot(side, to),
                        });
                    }
                    continue;
                }

                // Past the end of the board, so this is a bear off.
                if (!AllHome(side)) continue;

                int distance = side == BackgammonSide.White ? Points - from : from + 1;
                if (distance == die || (die > distance && IsRearmost(side, from)))
                {
                    moves.Add(new BackgammonMove
                    {
                        From = from,
                        To = BackgammonMove.Off,
                        Die = die,
                        Hits = false,
                    });
                }
            }

            return moves;
        }

        /// <summary>
        /// A die larger than the exact distance may only bear off from the furthest back
        /// point still occupied, otherwise the roll would waste a checker's progress.
        /// </summary>
        bool IsRearmost(BackgammonSide side, int point)
        {
            if (side == BackgammonSide.White)
            {
                for (int i = 18; i < point; i++)
                {
                    if (points[i].Side == side && points[i].Count > 0) return false;
                }
            }
            else
            {
                for (int i = 5; i > point; i--)
                {
                    if (points[i].Side == side && points[i].Count > 0) return false;
                }
            }
            return true;
        }

        /// <summary>A point is open unless the opponent has two or more checkers on it.</summary>
        bool CanLand(BackgammonSide side, int point)
        {
            if (point < 0 || point >= Points) return false;
            BackgammonPoint p = points[point];
            return p.Count == 0 || p.Side == side || p.Count == 1;
        }

        bool IsBlot(BackgammonSide side, int point) =>
            point >= 0 && point < Points
            && points[point].Count == 1 && points[point].Side == Opponent(side);

        // Making moves --------------------------------------------------------------

        public void ApplyMove(BackgammonMove move)
        {
            BackgammonSide side = SideToMove;

            if (move.From == BackgammonMove.Bar) bar[(int)side]--;
            else
            {
                points[move.From].Count--;
                if (points[move.From].Count == 0) points[move.From].Side = BackgammonSide.None;
            }

            if (move.To == BackgammonMove.Off)
            {
                off[(int)side]++;
                UpdateStatus();
                return;
            }

            // A lone opposing checker is sent to the bar.
            if (points[move.To].Count == 1 && points[move.To].Side != side)
            {
                bar[(int)points[move.To].Side]++;
                points[move.To].Count = 0;
                points[move.To].Side = BackgammonSide.None;
            }

            points[move.To].Side = side;
            points[move.To].Count++;
            UpdateStatus();
        }

        public void ApplyTurn(IEnumerable<BackgammonMove> moves)
        {
            foreach (BackgammonMove move in moves) ApplyMove(move);
            if (!IsOver(Status)) SideToMove = Opponent(SideToMove);
        }

        void UpdateStatus()
        {
            if (off[(int)BackgammonSide.White] >= CheckersPerSide) Status = BackgammonStatus.WhiteWins;
            else if (off[(int)BackgammonSide.Black] >= CheckersPerSide) Status = BackgammonStatus.BlackWins;
        }

        public BackgammonBoard Clone()
        {
            var copy = new BackgammonBoard();
            for (int i = 0; i < Points; i++) copy.points[i] = points[i];
            for (int i = 0; i < 3; i++) { copy.bar[i] = bar[i]; copy.off[i] = off[i]; }
            copy.SideToMove = SideToMove;
            copy.Status = Status;
            return copy;
        }

        /// <summary>
        /// Points as "side:count" separated by commas, then the bars and the borne off.
        /// Verbose, but a backgammon position has no short standard form and this is only
        /// used for fixtures and for reporting a desynced online game.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Points; i++)
            {
                if (i > 0) sb.Append(',');
                if (points[i].Count == 0) sb.Append('-');
                else sb.Append(points[i].Side == BackgammonSide.White ? 'w' : 'b').Append(points[i].Count);
            }
            sb.Append(" bar:").Append(bar[1]).Append('/').Append(bar[2]);
            sb.Append(" off:").Append(off[1]).Append('/').Append(off[2]);
            return sb.ToString();
        }

        /// <summary>Builds a position for tests. Empty points are written as a dash.</summary>
        public static BackgammonBoard Parse(string text, BackgammonSide sideToMove)
        {
            var board = new BackgammonBoard();
            for (int i = 0; i < Points; i++) board.points[i] = new BackgammonPoint();
            for (int i = 0; i < 3; i++) { board.bar[i] = 0; board.off[i] = 0; }

            string[] sections = text.Split(' ');
            string[] cells = sections[0].Split(',');
            for (int i = 0; i < cells.Length && i < Points; i++)
            {
                string cell = cells[i].Trim();
                if (cell == "-" || cell.Length < 2) continue;
                board.points[i] = new BackgammonPoint
                {
                    Side = cell[0] == 'w' ? BackgammonSide.White : BackgammonSide.Black,
                    Count = int.Parse(cell.Substring(1)),
                };
            }

            foreach (string section in sections)
            {
                if (section.StartsWith("bar:"))
                {
                    string[] parts = section.Substring(4).Split('/');
                    board.bar[1] = int.Parse(parts[0]);
                    board.bar[2] = int.Parse(parts[1]);
                }
                else if (section.StartsWith("off:"))
                {
                    string[] parts = section.Substring(4).Split('/');
                    board.off[1] = int.Parse(parts[0]);
                    board.off[2] = int.Parse(parts[1]);
                }
            }

            board.SideToMove = sideToMove;
            board.UpdateStatus();
            return board;
        }
    }
}
