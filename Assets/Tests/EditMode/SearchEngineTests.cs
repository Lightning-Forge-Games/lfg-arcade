using System;
using LightningForge.Chess.Core;
using NUnit.Framework;

namespace LightningForge.Chess.Tests
{
    /// <summary>
    /// Tests the search on positions with one clearly correct answer. A chess engine is
    /// hard to test exhaustively, but "does it see a mate in one" and "does it take a free
    /// queen" catch the failures that matter.
    /// </summary>
    public class SearchEngineTests
    {
        static Move Best(string fen, Difficulty difficulty = Difficulty.Hard)
        {
            var board = new Board(fen);
            string before = board.ToFen();

            var engine = new SearchEngine();
            Move move = engine.FindBestMove(board, difficulty, null);

            Assert.AreEqual(before, board.ToFen(), "search must leave the position untouched");
            return move;
        }

        [Test]
        public void FindsMateInOne()
        {
            // Rook to a8 is mate; the king is boxed in by its own pawns.
            Move move = Best("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
            Assert.AreEqual("a1a8", move.ToUci());
        }

        [Test]
        public void FindsBackRankMateOverMaterialGrab()
        {
            // Taking the rook on h8 wins material, but Ra8 is mate and must win out.
            Move move = Best("r5k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1");
            Assert.AreEqual("a1a8", move.ToUci(), "mate should be preferred over any capture");
        }

        [Test]
        public void CapturesAFreeQueen()
        {
            // Black queen on d5 is undefended and the rook on d1 can take it.
            Move move = Best("4k3/8/8/3q4/8/8/8/3RK3 w - - 0 1");
            Assert.AreEqual("d1d5", move.ToUci());
        }

        [Test]
        public void AvoidsHangingItsOwnQueen()
        {
            // White queen on d4 is attacked by the pawn on e5. Moving it must be preferred
            // to leaving it, so the chosen move should start from d4.
            Move move = Best("4k3/8/8/4p3/3Q4/8/8/4K3 w - - 0 1");
            Assert.AreEqual(Square.FromAlgebraic("d4"), move.From, "queen should move out of the pawn's reach");
        }

        [Test]
        public void ReturnsNoneWhenGameIsOver()
        {
            // Fool's mate: white is mated and has no legal move.
            Move move = Best("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3");
            Assert.AreEqual(Move.None, move);
        }

        [Test]
        public void EveryDifficultyReturnsALegalMove()
        {
            foreach (Difficulty difficulty in Enum.GetValues(typeof(Difficulty)))
            {
                var board = new Board();
                var engine = new SearchEngine();
                Move move = engine.FindBestMove(board, difficulty, new Random(1234));

                Assert.IsTrue(board.TryFindMove(move.ToUci(), out _),
                    difficulty + " produced an illegal move: " + move.ToUci());
            }
        }

        [Test]
        public void RespectsTheNodeBudget()
        {
            var board = new Board();
            string before = board.ToFen();
            var engine = new SearchEngine();

            // A deliberately tiny budget must still yield a legal move rather than hanging.
            Move move = engine.FindBestMove(board, 6, 500, 0, 0, null);

            // Aborting mid-search must not leave the board part way through a line.
            Assert.AreEqual(before, board.ToFen(), "position must be restored even when the budget aborts");
            Assert.IsTrue(board.TryFindMove(move.ToUci(), out _), "should fall back to a completed depth");
            Assert.LessOrEqual(engine.NodesSearched, 600, "budget should stop the search promptly");
        }

        [Test]
        public void EvaluationIsSymmetric()
        {
            // The opening position is balanced, so it must score zero from White's view.
            Assert.AreEqual(0, Evaluation.Evaluate(new Board()));
        }

        [Test]
        public void EvaluationFavoursTheSideWithMoreMaterial()
        {
            // White has an extra queen.
            int score = Evaluation.Evaluate(new Board("4k3/8/8/8/8/8/8/3QK3 w - - 0 1"));
            Assert.Greater(score, 500, "an extra queen should read strongly for White");
        }
    }
}
