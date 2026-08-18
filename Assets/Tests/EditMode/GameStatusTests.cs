using LightningForge.Arcade.Core.Chess;
using LightningForge.Arcade.Core;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    public class GameStatusTests
    {
        [TestCase("rnb1kbnr/pppp1ppp/8/4p3/6Pq/5P2/PPPPP2P/RNBQKBNR w KQkq - 1 3", GameStatus.Checkmate, TestName = "Fools mate")]
        [TestCase("R5k1/5ppp/8/8/8/8/8/6K1 b - - 0 1", GameStatus.Checkmate, TestName = "Back rank mate")]
        [TestCase("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1", GameStatus.Stalemate, TestName = "Stalemate")]
        [TestCase("rnbqkbnr/ppp1pppp/8/1B1p4/8/4P3/PPPP1PPP/RNBQK1NR b KQkq - 1 2", GameStatus.Check, TestName = "Check but not mate")]
        [TestCase("8/8/4k3/8/8/4K3/8/8 w - - 0 1", GameStatus.DrawByInsufficientMaterial, TestName = "Bare kings")]
        [TestCase("8/8/4k3/8/8/4KB2/8/8 w - - 0 1", GameStatus.DrawByInsufficientMaterial, TestName = "King and bishop")]
        [TestCase("8/8/4k3/8/8/4KN2/8/8 w - - 0 1", GameStatus.DrawByInsufficientMaterial, TestName = "King and knight")]
        [TestCase("8/8/4k3/8/8/4KR2/8/8 w - - 0 1", GameStatus.Ongoing, TestName = "King and rook can still mate")]
        [TestCase("8/8/4k3/8/8/4KP2/8/8 w - - 0 1", GameStatus.Ongoing, TestName = "Pawn can promote so not a draw")]
        [TestCase("4k3/8/8/8/8/8/4P3/4K3 w - - 100 80", GameStatus.DrawByFiftyMoveRule, TestName = "Fifty move rule")]
        public void EvaluatesPosition(string fen, GameStatus expected)
        {
            Assert.AreEqual(expected, GameStatusEvaluator.Evaluate(new Board(fen)));
        }

        [Test]
        public void CastlingRightsAreLostWhenRookIsCaptured()
        {
            // Black bishop on c3 takes the rook on a1, which must clear White's queenside right.
            var board = new Board("r3k2r/8/8/8/8/2b5/8/R3K2R b KQkq - 0 1");
            Assert.IsTrue(board.TryFindMove("c3a1", out Move capture), "expected Bxa1 to be legal");

            board.MakeMove(capture);
            Assert.AreEqual(CastlingRights.None, board.Castling & CastlingRights.WhiteQueenSide);
            Assert.AreNotEqual(CastlingRights.None, board.Castling & CastlingRights.WhiteKingSide);
        }

        [Test]
        public void EnPassantCaptureRemovesTheCorrectPawn()
        {
            var board = new Board("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
            Assert.IsTrue(board.TryFindMove("e5d6", out Move enPassant), "expected exd6 e.p. to be legal");
            Assert.IsTrue(enPassant.IsEnPassant);

            board.MakeMove(enPassant);
            Assert.IsTrue(board[Square.FromAlgebraic("d5")].IsNone, "captured pawn should be gone from d5");
            Assert.AreEqual("4k3/8/3P4/8/8/8/8/4K3 b - - 0 1", board.ToFen());
        }

        [Test]
        public void CastlingThroughCheckIsIllegal()
        {
            // Black rook on f8 covers f1, so White may not castle kingside.
            var board = new Board("4kr2/8/8/8/8/8/8/4K2R w K - 0 1");
            Assert.IsFalse(board.TryFindMove("e1g1", out _), "castling through an attacked square must be illegal");
        }

        [Test]
        public void PromotionOffersAllFourPieces()
        {
            var board = new Board("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
            int promotions = 0;
            foreach (Move move in MoveGenerator.GenerateLegalMoves(board))
            {
                if (move.IsPromotion) promotions++;
            }
            Assert.AreEqual(4, promotions);
        }
    }
}
