using System;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Connect4;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// Rules first, then the opponent.
    ///
    /// Grids are written as pictures rather than as sequences of drops, because a test that
    /// says which position it is about is worth far more when it fails than one that lists
    /// eleven columns and leaves you to work out what they build.
    /// </summary>
    public class Connect4Tests
    {
        static Connect4Board Empty() => new Connect4Board();

        [Test]
        public void RedMovesFirstAndTurnsAlternate()
        {
            Connect4Board board = Empty();
            Assert.AreEqual(Connect4Player.Red, board.SideToMove);

            board.Drop(3);
            Assert.AreEqual(Connect4Player.Yellow, board.SideToMove);

            board.Drop(3);
            Assert.AreEqual(Connect4Player.Red, board.SideToMove);
        }

        [Test]
        public void DiscsStackFromTheBottom()
        {
            Connect4Board board = Empty();
            Assert.AreEqual(0, board.Drop(2), "first disc should rest on the floor");
            Assert.AreEqual(1, board.Drop(2), "second should sit on the first");
            Assert.AreEqual(Connect4Player.Red, board[2, 0]);
            Assert.AreEqual(Connect4Player.Yellow, board[2, 1]);
            Assert.AreEqual(2, board.Height(2));
        }

        [Test]
        public void AFullColumnRefusesMoreDiscs()
        {
            Connect4Board board = Empty();
            for (int i = 0; i < Connect4Board.Rows; i++) board.Drop(0);

            Assert.AreEqual(Connect4Board.Rows, board.Height(0));
            Assert.IsFalse(board.IsPlayable(0));
            Assert.AreEqual(-1, board.Drop(0));
        }

        [Test]
        public void ColumnsOffTheBoardAreRejected()
        {
            Connect4Board board = Empty();
            Assert.AreEqual(-1, board.Drop(-1));
            Assert.AreEqual(-1, board.Drop(Connect4Board.Columns));
            Assert.AreEqual(0, board.MoveCount, "a rejected drop must not count as a move");
        }

        [TestCase("......./......./......./......./......./RRR....", 3, Connect4Status.RedWins, "horizontal")]
        [TestCase("......./......./......./R....../R....../R......", 0, Connect4Status.RedWins, "vertical")]
        public void FourInALineWins(string grid, int winningColumn, Connect4Status expected, string shape)
        {
            Connect4Board board = Connect4Board.Parse(grid, Connect4Player.Red);
            board.Drop(winningColumn);
            Assert.AreEqual(expected, board.Status, shape + " line should win");
            Assert.AreEqual(4, board.WinningCells.Count, "the winning four should be recorded");
        }

        [Test]
        public void ADiagonalWins()
        {
            // Red holds the bottom of the up-right diagonal at (0,0), (1,1) and (2,2). The
            // yellows are there to raise columns 1 to 3 so the next red lands on (3,3).
            Connect4Board board = Connect4Board.Parse(
                "......." +
                "/......." +
                "/......." +
                "/..RY..." +
                "/.RYY..." +
                "/RYYY...", Connect4Player.Red);

            Assert.AreEqual(Connect4Status.Ongoing, board.Status);
            Assert.AreEqual(3, board.Height(3), "column 3 must be three high for the drop to land on the diagonal");

            Assert.AreEqual(3, board.Drop(3), "the disc should come to rest on row 3");
            Assert.AreEqual(Connect4Status.RedWins, board.Status, "the rising diagonal should win");
            Assert.AreEqual(4, board.WinningCells.Count);
        }

        [Test]
        public void AFullBoardWithNoLineIsADraw()
        {
            // Colours run in horizontal bands two rows deep, alternating along each row.
            // Every column reads YYRRYY, every row alternates, and because stepping one
            // column always changes colour while stepping one row usually does not, no
            // diagonal manages more than two. The top left cell is left open for the last
            // disc, which is Yellow's, matching the counts.
            Connect4Board board = Connect4Board.Parse(
                ".RYRYRY" +
                "/YRYRYRY" +
                "/RYRYRYR" +
                "/RYRYRYR" +
                "/YRYRYRY" +
                "/YRYRYRY");

            Assert.AreEqual(Connect4Board.Cells - 1, board.MoveCount, "fixture should be one short of full");
            Assert.AreEqual(Connect4Status.Ongoing, board.Status);
            Assert.AreEqual(Connect4Player.Yellow, board.SideToMove);

            board.Drop(0);

            Assert.AreEqual(Connect4Status.Draw, board.Status, "a full board with no line is a draw");
            Assert.IsFalse(board.IsPlayable(0));
            Assert.AreEqual(Connect4Board.Cells, board.MoveCount);
        }

        [Test]
        public void UndoRestoresEverythingTheDropChanged()
        {
            Connect4Board board = Empty();
            board.Drop(3);
            board.Drop(3);
            string before = board.ToString();
            Connect4Player sideBefore = board.SideToMove;
            int countBefore = board.MoveCount;

            board.Drop(4);
            board.Undo(4);

            Assert.AreEqual(before, board.ToString(), "grid should be back as it was");
            Assert.AreEqual(sideBefore, board.SideToMove, "turn should be handed back");
            Assert.AreEqual(countBefore, board.MoveCount);
            Assert.AreEqual(0, board.Height(4));
        }

        [Test]
        public void UndoingAWinReopensTheGame()
        {
            // The search plays winning moves and takes them back constantly; if undo left
            // the board finished, every line after the first win would be unsearchable.
            Connect4Board board = Connect4Board.Parse(
                "......./......./......./......./......./RRR....", Connect4Player.Red);
            board.Drop(3);
            Assert.AreEqual(Connect4Status.RedWins, board.Status);

            board.Undo(3);
            Assert.AreEqual(Connect4Status.Ongoing, board.Status);
            Assert.AreEqual(Connect4Player.Red, board.SideToMove);
            Assert.AreEqual(0, board.WinningCells.Count);
        }

        [Test]
        public void ParseAndToStringAgree()
        {
            const string grid = "......./......./......./...R.../...Y.../..RYY..";
            Assert.AreEqual(grid, Connect4Board.Parse(grid).ToString());
        }

        // The opponent ------------------------------------------------------------

        [Test]
        public void TheSearchTakesAWinItCanSee()
        {
            Connect4Board board = Connect4Board.Parse(
                "......./......./......./......./......./RRR.YY.", Connect4Player.Red);
            var search = new Connect4Search();

            int column = search.ChooseColumn(board, Difficulty.Medium, null);
            Assert.AreEqual(3, column, "should complete its own four rather than anything else");
        }

        [Test]
        public void TheSearchBlocksAnImmediateThreat()
        {
            // Yellow to move, Red threatens to complete a row along the bottom.
            Connect4Board board = Connect4Board.Parse(
                "......./......./......./......./....Y../RRR.Y..", Connect4Player.Yellow);

            var search = new Connect4Search();
            int column = search.ChooseColumn(board, Difficulty.Medium, null);
            Assert.AreEqual(3, column, "must block the open end of Red's three");
        }

        [Test]
        public void TheSearchPrefersTheFasterWin()
        {
            // Red can win at once in column 3. Anything else lets the game continue.
            Connect4Board board = Connect4Board.Parse(
                "......./......./......./......./YY...../RRR.Y..", Connect4Player.Red);
            var search = new Connect4Search();

            int column = search.ChooseColumn(board, Difficulty.Hard, null);
            board.Drop(column);
            Assert.AreEqual(Connect4Status.RedWins, board.Status, "the win was available now");
        }

        [Test]
        public void TheSearchOnlyEverReturnsALegalColumn()
        {
            var random = new Random(7);
            var search = new Connect4Search();
            Connect4Board board = Empty();

            // Play a whole game out against itself; every choice must be playable.
            while (!Connect4Board.IsOver(board.Status))
            {
                int column = search.ChooseColumn(board, Difficulty.Easy, random);
                Assert.IsTrue(board.IsPlayable(column),
                    "chose column " + column + " which is not playable in " + board);
                board.Drop(column);
            }
        }

        [Test]
        public void HarderSettingsBeatEasierOnes()
        {
            // Not a strict guarantee for any single game, but Hard losing to Easy as Red
            // over a full game would mean the depth or the ordering is broken.
            var hard = new Connect4Search();
            var easy = new Connect4Search();
            Connect4Board board = Empty();
            var random = new Random(11);

            while (!Connect4Board.IsOver(board.Status))
            {
                bool hardToMove = board.SideToMove == Connect4Player.Red;
                int column = hardToMove
                    ? hard.ChooseColumn(board, Difficulty.Hard, null)
                    : easy.ChooseColumn(board, Difficulty.Easy, random);
                board.Drop(column);
            }

            Assert.AreEqual(Connect4Status.RedWins, board.Status,
                "Hard should beat Easy from an empty board; got " + board.Status);
        }

        [Test]
        public void TheSearchStaysWithinItsNodeBudget()
        {
            // The web build is single threaded, so an unbounded search freezes the tab.
            Connect4Board board = Empty();
            var search = new Connect4Search();

            int column = search.ChooseColumn(board, depth: 20, budget: 30000, random: null);

            Assert.IsTrue(board.IsPlayable(column), "must still return a usable move");
            Assert.LessOrEqual(search.NodesSearched, 30000 * 2,
                "budget should stop the search rather than being advisory");
        }
    }
}
