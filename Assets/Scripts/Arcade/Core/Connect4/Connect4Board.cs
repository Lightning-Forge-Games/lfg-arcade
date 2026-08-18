using System.Collections.Generic;
using System.Text;

namespace LightningForge.Arcade.Core.Connect4
{
    public enum Connect4Player : byte
    {
        None = 0,
        Red = 1,
        Yellow = 2,
    }

    public enum Connect4Status
    {
        Ongoing,
        RedWins,
        YellowWins,
        Draw,
    }

    /// <summary>
    /// A standard seven by six Connect 4 grid.
    ///
    /// Cells are indexed row major with row 0 at the bottom, matching the direction discs
    /// fall, so a column's next free row is simply the count of discs already in it.
    ///
    /// The board supports undo because the opponent searches by playing moves on the real
    /// board and taking them back. Copying a board per node would dominate the search cost
    /// and cap how deep the hard setting can look.
    /// </summary>
    public sealed class Connect4Board
    {
        public const int Columns = 7;
        public const int Rows = 6;
        public const int Cells = Columns * Rows;

        /// <summary>Every direction a line of four can run: east, north, and both diagonals.</summary>
        static readonly int[,] Directions = { { 1, 0 }, { 0, 1 }, { 1, 1 }, { 1, -1 } };

        readonly Connect4Player[] cells = new Connect4Player[Cells];
        readonly int[] heights = new int[Columns];
        readonly List<int> winningCells = new List<int>(4);

        public Connect4Board()
        {
            Reset();
        }

        public Connect4Player SideToMove { get; private set; }
        public Connect4Status Status { get; private set; }
        public int MoveCount { get; private set; }

        /// <summary>The four cells that won, for highlighting. Empty until someone wins.</summary>
        public IReadOnlyList<int> WinningCells => winningCells;

        public static int IndexOf(int column, int row) => row * Columns + column;
        public static int ColumnOf(int index) => index % Columns;
        public static int RowOf(int index) => index / Columns;

        public Connect4Player this[int column, int row] => cells[IndexOf(column, row)];
        public Connect4Player At(int index) => cells[index];

        /// <summary>Discs currently in a column, which is also the row the next one lands on.</summary>
        public int Height(int column) => heights[column];

        public void Reset()
        {
            for (int i = 0; i < Cells; i++) cells[i] = Connect4Player.None;
            for (int c = 0; c < Columns; c++) heights[c] = 0;
            winningCells.Clear();

            SideToMove = Connect4Player.Red;
            Status = Connect4Status.Ongoing;
            MoveCount = 0;
        }

        public bool IsPlayable(int column) =>
            Status == Connect4Status.Ongoing
            && column >= 0 && column < Columns
            && heights[column] < Rows;

        public IEnumerable<int> PlayableColumns()
        {
            for (int c = 0; c < Columns; c++)
            {
                if (IsPlayable(c)) yield return c;
            }
        }

        /// <summary>
        /// Drops a disc for the side to move. Returns the row it came to rest on, or -1 if
        /// the column was full, out of range, or the game is already over.
        /// </summary>
        public int Drop(int column)
        {
            if (!IsPlayable(column)) return -1;

            int row = heights[column];
            int index = IndexOf(column, row);

            cells[index] = SideToMove;
            heights[column]++;
            MoveCount++;

            if (FindLineThrough(column, row, SideToMove))
            {
                Status = SideToMove == Connect4Player.Red
                    ? Connect4Status.RedWins
                    : Connect4Status.YellowWins;
            }
            else if (MoveCount == Cells)
            {
                Status = Connect4Status.Draw;
            }

            SideToMove = Opponent(SideToMove);
            return row;
        }

        /// <summary>Takes back the last disc dropped in a column. Used by the search.</summary>
        public void Undo(int column)
        {
            if (column < 0 || column >= Columns || heights[column] == 0) return;

            heights[column]--;
            cells[IndexOf(column, heights[column])] = Connect4Player.None;
            MoveCount--;

            SideToMove = Opponent(SideToMove);
            Status = Connect4Status.Ongoing;
            winningCells.Clear();
        }

        public static Connect4Player Opponent(Connect4Player player) =>
            player == Connect4Player.Red ? Connect4Player.Yellow : Connect4Player.Red;

        public static bool IsOver(Connect4Status status) => status != Connect4Status.Ongoing;

        /// <summary>
        /// Looks for four in a row through a cell just played.
        ///
        /// Only lines through the new disc can have been completed by it, so this is far
        /// cheaper than scanning the whole grid, which matters at search depth.
        /// </summary>
        bool FindLineThrough(int column, int row, Connect4Player player)
        {
            for (int d = 0; d < 4; d++)
            {
                int dx = Directions[d, 0];
                int dy = Directions[d, 1];

                int count = 1;
                int forward = Run(column, row, dx, dy, player);
                int backward = Run(column, row, -dx, -dy, player);
                count += forward + backward;

                if (count < 4) continue;

                // Record exactly the four that won, walking from the far end of the run.
                winningCells.Clear();
                int startColumn = column - dx * backward;
                int startRow = row - dy * backward;
                for (int i = 0; i < count && winningCells.Count < 4; i++)
                {
                    winningCells.Add(IndexOf(startColumn + dx * i, startRow + dy * i));
                }
                return true;
            }
            return false;
        }

        /// <summary>How many of the player's discs run consecutively in one direction.</summary>
        int Run(int column, int row, int dx, int dy, Connect4Player player)
        {
            int count = 0;
            int c = column + dx;
            int r = row + dy;
            while (c >= 0 && c < Columns && r >= 0 && r < Rows && cells[IndexOf(c, r)] == player)
            {
                count++;
                c += dx;
                r += dy;
            }
            return count;
        }

        /// <summary>
        /// The grid as text, bottom row last so it reads the way the board looks. Used for
        /// test fixtures and for reporting a desynced online game.
        /// </summary>
        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int row = Rows - 1; row >= 0; row--)
            {
                for (int column = 0; column < Columns; column++)
                {
                    Connect4Player p = cells[IndexOf(column, row)];
                    sb.Append(p == Connect4Player.None ? '.' : p == Connect4Player.Red ? 'R' : 'Y');
                }
                if (row > 0) sb.Append('/');
            }
            return sb.ToString();
        }

        /// <summary>
        /// Fills a board from the text <see cref="ToString"/> produces. Tests are far easier
        /// to read as a picture of the grid than as a list of columns to drop into.
        ///
        /// Whose turn it is is inferred from the disc counts, which only works for positions
        /// a real game could reach. Pass <paramref name="sideToMove"/> to state it outright
        /// when the point of the fixture is a shape rather than a legal history.
        /// </summary>
        public static Connect4Board Parse(string text, Connect4Player sideToMove = Connect4Player.None)
        {
            var board = new Connect4Board();
            string[] rows = text.Split('/');

            int red = 0;
            int yellow = 0;
            for (int i = 0; i < rows.Length; i++)
            {
                int row = Rows - 1 - i;
                for (int column = 0; column < rows[i].Length && column < Columns; column++)
                {
                    char c = rows[i][column];
                    if (c == 'R') { board.cells[IndexOf(column, row)] = Connect4Player.Red; red++; }
                    else if (c == 'Y') { board.cells[IndexOf(column, row)] = Connect4Player.Yellow; yellow++; }
                }
            }

            for (int column = 0; column < Columns; column++)
            {
                int height = 0;
                while (height < Rows && board.cells[IndexOf(column, height)] != Connect4Player.None) height++;
                board.heights[column] = height;
            }

            board.MoveCount = red + yellow;
            // Red always starts, so an equal count means it is Red's turn again.
            board.SideToMove = sideToMove != Connect4Player.None
                ? sideToMove
                : red == yellow ? Connect4Player.Red : Connect4Player.Yellow;
            return board;
        }
    }
}
