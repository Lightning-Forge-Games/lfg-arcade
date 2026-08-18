using System.Collections.Generic;
using LightningForge.Arcade.Core.Chess;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// Perft (performance test) counts leaf nodes of the move tree to a fixed depth. The
    /// expected counts below are the long-published values for these positions, so an exact
    /// match is strong evidence that castling, en passant, promotion, pins and check
    /// evasion are all handled correctly.
    /// </summary>
    public class PerftTests
    {
        static long Perft(Board board, int depth)
        {
            if (depth == 0) return 1;

            var moves = MoveGenerator.GenerateLegalMoves(board);
            if (depth == 1) return moves.Count;

            long nodes = 0;
            foreach (Move move in moves)
            {
                Undo undo = board.MakeMove(move);
                nodes += Perft(board, depth - 1);
                board.UnmakeMove(move, undo);
            }
            return nodes;
        }

        // Depths kept modest so the suite stays fast enough to run on every change.
        static readonly object[] PerftCases =
        {
            new object[] { "startpos", Board.StartFen, 4, 197281L },
            new object[] { "kiwipete", "r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1", 3, 97862L },
            new object[] { "position 3", "8/2p5/3p4/KP5r/1R3p1k/8/4P1P1/8 w - - 0 1", 4, 43238L },
            new object[] { "position 4", "r3k2r/Pppp1ppp/1b3nbN/nP6/BBP1P3/q4N2/Pp1P2PP/R2Q1RK1 w kq - 0 1", 3, 9467L },
            new object[] { "position 5", "rnbq1k1r/pp1Pbppp/2p5/8/2B5/8/PPP1NnPP/RNBQK2R w KQ - 1 8", 3, 62379L },
            new object[] { "position 6", "r4rk1/1pp1qppp/p1np1n2/2b1p1B1/2B1P1b1/P1NP1N2/1PP1QPPP/R4RK1 w - - 0 10", 3, 89890L }
        };

        [TestCaseSource(nameof(PerftCases))]
        public void MatchesPublishedNodeCounts(string name, string fen, int depth, long expected)
        {
            var board = new Board(fen);
            Assert.AreEqual(expected, Perft(board, depth), $"perft({depth}) mismatch for {name}");
        }

        [TestCaseSource(nameof(PerftCases))]
        public void FenRoundTripsLosslessly(string name, string fen, int depth, long expected)
        {
            Assert.AreEqual(fen, new Board(fen).ToFen(), $"FEN round trip failed for {name}");
        }

        [TestCaseSource(nameof(PerftCases))]
        public void MakeUnmakeLeavesPositionUnchanged(string name, string fen, int depth, long expected)
        {
            var board = new Board(fen);
            string before = board.ToFen();

            foreach (Move move in MoveGenerator.GenerateLegalMoves(board))
            {
                Undo undo = board.MakeMove(move);
                board.UnmakeMove(move, undo);
                Assert.AreEqual(before, board.ToFen(), $"{move.ToUci()} corrupted the position in {name}");
            }
        }

        [Test]
        public void StartingPositionHasTwentyMoves()
        {
            var moves = MoveGenerator.GenerateLegalMoves(new Board());
            Assert.AreEqual(20, moves.Count);

            var uci = new List<string>();
            foreach (Move move in moves) uci.Add(move.ToUci());

            CollectionAssert.Contains(uci, "e2e4");
            CollectionAssert.Contains(uci, "g1f3");
        }
    }
}
