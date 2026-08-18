using System;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Draughts;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// The rules worth being sure about are the compulsory capture, the chain that must be
    /// finished, and crowning ending the turn. Those three are where draughts engines
    /// usually go wrong, and all three change which moves are legal rather than just how
    /// good they are.
    ///
    /// Positions are written as pictures, top rank first, lower case for men and upper for
    /// kings, matching what the board prints.
    /// </summary>
    public class DraughtsTests
    {
        static List<string> Notations(DraughtsBoard board)
        {
            var list = new List<string>();
            foreach (DraughtsMove move in board.GenerateMoves()) list.Add(move.ToNotation());
            list.Sort(StringComparer.Ordinal);
            return list;
        }

        [Test]
        public void TheOpeningPositionHasTwelveEachAndSevenMoves()
        {
            var board = new DraughtsBoard();

            Assert.AreEqual(12, board.CountPieces(DraughtsSide.White));
            Assert.AreEqual(12, board.CountPieces(DraughtsSide.Black));
            Assert.AreEqual(DraughtsSide.White, board.SideToMove, "White moves first here");

            // Only the fourth rank is reachable, and the a-file man has one square, the
            // rest have two, minus the ones that would leave the board.
            Assert.AreEqual(7, board.GenerateMoves().Count);
        }

        [Test]
        public void PiecesOnlyEverSitOnDarkSquares()
        {
            var board = new DraughtsBoard();
            for (int square = 0; square < Square.Count; square++)
            {
                if (board[square].IsNone) continue;
                Assert.IsTrue(DraughtsBoard.IsPlayable(square),
                    Square.ToAlgebraic(square) + " is a light square");
            }
        }

        [Test]
        public void AManMovesForwardOnly()
        {
            // A lone white man in the middle of an empty board.
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/        " +
                "/   w    " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.White);

            List<string> moves = Notations(board);
            Assert.AreEqual(2, moves.Count, "two forward diagonals, no backward ones");
            CollectionAssert.AreEqual(new[] { "d5c6", "d5e6" }, moves);
        }

        [Test]
        public void AKingMovesInEveryDirection()
        {
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/        " +
                "/   W    " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.White);

            Assert.AreEqual(4, board.GenerateMoves().Count, "a king has all four diagonals");
        }

        [Test]
        public void ACaptureIsCompulsory()
        {
            // White could step quietly with the far man, but a jump exists so only the jump
            // is legal. This is the rule that most defines the game.
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/        " +
                "/        " +
                "/    b   " +
                "/   w    " +
                "/        " +
                "/ w      ", DraughtsSide.White);

            List<string> moves = Notations(board);
            Assert.AreEqual(1, moves.Count, "only the capture should be offered, got: " + string.Join(", ", moves));
            Assert.AreEqual("d3f5", moves[0]);
        }

        [Test]
        public void AChainOfJumpsIsOneMoveAndMustBeFinished()
        {
            // Two black men lined up so white jumps twice. The single jump must not be
            // offered as a move of its own.
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/    b   " +
                "/        " +
                "/  b     " +
                "/ w      " +
                "/        " +
                "/        ", DraughtsSide.White);

            List<string> moves = Notations(board);
            Assert.AreEqual(1, moves.Count, "the partial jump is not a legal move, got: " + string.Join(", ", moves));
            Assert.AreEqual("b3d5f7", moves[0], "both jumps belong to one move");

            DraughtsMove move = board.GenerateMoves()[0];
            Assert.AreEqual(2, move.Captured.Length, "both men should be taken");

            board.Play(move);
            Assert.AreEqual(0, board.CountPieces(DraughtsSide.Black), "both were captured");
        }

        [Test]
        public void ReachingTheFarRankCrowns()
        {
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/  w     " +
                "/        " +
                "/        " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.White);

            DraughtsMove move = board.GenerateMoves().Find(m => m.To == Square.FromAlgebraic("d8"));
            Assert.IsTrue(move.Crowns, "landing on the eighth rank should crown");

            board.Play(move);
            Assert.IsTrue(board[Square.FromAlgebraic("d8")].IsKing, "the man should now be a king");
        }

        [Test]
        public void CrowningEndsTheTurnEvenIfAnotherJumpExists()
        {
            // White jumps onto the back rank and crowns. A king could jump back down, but
            // in English draughts the move ends at the crown.
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/  b     " +
                "/ w      " +
                "/  b     " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.White);

            List<DraughtsMove> moves = board.GenerateMoves();
            Assert.AreEqual(1, moves.Count);

            DraughtsMove move = moves[0];
            Assert.IsTrue(move.Crowns, "should crown on the eighth rank");
            Assert.AreEqual(1, move.Captured.Length,
                "the move must stop at the crown rather than jumping on");
        }

        [Test]
        public void AManCannotJumpBackwardsButAKingCan()
        {
            // The black man sits diagonally behind the white one: d4 with c3 to take and
            // b2 to land on. Placed ahead instead, the jump would simply be a legal
            // forward capture and the test would prove nothing.
            const string grid =
                "        " +
                "/        " +
                "/        " +
                "/        " +
                "/   w    " +
                "/  b     " +
                "/        " +
                "/        ";

            DraughtsBoard withMan = DraughtsBoard.Parse(grid, DraughtsSide.White);
            foreach (DraughtsMove move in withMan.GenerateMoves())
            {
                Assert.IsFalse(move.IsCapture, "a man must not jump backwards");
            }

            DraughtsBoard withKing = DraughtsBoard.Parse(grid.Replace('w', 'W'), DraughtsSide.White);
            List<string> kingMoves = Notations(withKing);
            CollectionAssert.Contains(kingMoves, "d4b2", "a king should jump backwards");
        }

        [Test]
        public void ASideWithNoMovesLoses()
        {
            // Black is blocked into the corner with nowhere to go, which is a loss in
            // draughts rather than the stalemate draw chess would give.
            DraughtsBoard board = DraughtsBoard.Parse(
                "b       " +
                "/ w      " +
                "/  w     " +
                "/        " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.Black);

            Assert.AreEqual(0, board.GenerateMoves().Count, "black should be stuck");
            Assert.AreEqual(DraughtsStatus.WhiteWins, board.Status);
        }

        [Test]
        public void LosingEveryPieceIsALoss()
        {
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/        " +
                "/   w    " +
                "/        " +
                "/        " +
                "/        " +
                "/        ", DraughtsSide.Black);

            Assert.AreEqual(DraughtsStatus.WhiteWins, board.Status);
        }

        [Test]
        public void NotationRoundTripsThroughTheMoveList()
        {
            var board = new DraughtsBoard();
            foreach (DraughtsMove move in board.GenerateMoves())
            {
                Assert.IsTrue(board.TryFindMove(move.ToNotation(), out DraughtsMove found),
                    move.ToNotation() + " did not round trip");
                Assert.AreEqual(move.To, found.To);
            }

            Assert.IsFalse(board.TryFindMove("a1a2", out _), "an illegal move must not resolve");
        }

        [Test]
        public void PlayIsReflectedInTheClone()
        {
            var board = new DraughtsBoard();
            DraughtsBoard copy = board.Clone();

            copy.Play(copy.GenerateMoves()[0]);
            Assert.AreNotEqual(board.ToString(), copy.ToString(), "the clone must be independent");
            Assert.AreEqual(DraughtsSide.White, board.SideToMove, "the original must be untouched");
        }

        // The opponent ------------------------------------------------------------

        [Test]
        public void TheSearchTakesAFreeCapture()
        {
            DraughtsBoard board = DraughtsBoard.Parse(
                "        " +
                "/        " +
                "/        " +
                "/        " +
                "/    b   " +
                "/   w    " +
                "/        " +
                "/ w      ", DraughtsSide.White);

            var search = new DraughtsSearch();
            Assert.IsTrue(search.TryChooseMove(board, Difficulty.Medium, null, out DraughtsMove move));
            Assert.IsTrue(move.IsCapture, "the only legal move is a capture");
        }

        [Test]
        public void TheSearchOnlyEverReturnsALegalMove()
        {
            var random = new Random(3);
            var search = new DraughtsSearch();
            var board = new DraughtsBoard();

            int plies = 0;
            while (!DraughtsBoard.IsOver(board.Status) && plies++ < 120)
            {
                Assert.IsTrue(search.TryChooseMove(board, Difficulty.Easy, random, out DraughtsMove move));
                Assert.IsTrue(board.TryFindMove(move.ToNotation(), out _),
                    "chose " + move.ToNotation() + " which is not legal in " + board);
                board.Play(move);
            }
        }

        [Test]
        public void TheSearchStaysWithinItsNodeBudget()
        {
            var board = new DraughtsBoard();
            var search = new DraughtsSearch();

            Assert.IsTrue(search.TryChooseMove(board, depth: 20, budget: 25000, random: null,
                chosen: out DraughtsMove move));
            Assert.IsTrue(board.TryFindMove(move.ToNotation(), out _), "must still return a legal move");
            Assert.LessOrEqual(search.NodesSearched, 25000 * 2, "the budget should actually stop it");
        }
    }
}
