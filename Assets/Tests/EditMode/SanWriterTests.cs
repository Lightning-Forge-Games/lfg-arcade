using LightningForge.Arcade.Core.Chess;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    public class SanWriterTests
    {
        static string San(string fen, string uci)
        {
            var board = new Board(fen);
            Assert.IsTrue(board.TryFindMove(uci, out Move move), uci + " should be legal in " + fen);
            string san = SanWriter.ToSan(board, move);
            Assert.AreEqual(fen, board.ToFen(), "ToSan must leave the position untouched");
            return san;
        }

        [Test]
        public void PlainPawnPush()
        {
            Assert.AreEqual("e4", San(Board.StartFen, "e2e4"));
        }

        [Test]
        public void KnightDevelopment()
        {
            Assert.AreEqual("Nf3", San(Board.StartFen, "g1f3"));
        }

        [Test]
        public void PawnCaptureNamesTheFileItLeft()
        {
            // White pawn e4, black pawn d5.
            Assert.AreEqual("exd5", San("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "e4d5"));
        }

        [Test]
        public void PieceCaptureUsesX()
        {
            Assert.AreEqual("Nxe5", San("rnbqkbnr/pppp1ppp/8/4p3/8/5N2/PPPPPPPP/RNBQKB1R w KQkq - 0 2", "f3e5"));
        }

        [Test]
        public void Castling()
        {
            Assert.AreEqual("O-O", San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1g1"));
            Assert.AreEqual("O-O-O", San("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "e1c1"));
        }

        [Test]
        public void PromotionUsesEquals()
        {
            // The new queen on a8 rakes the eighth rank and checks the king on e8, so the
            // '+' belongs here. A knight on a8 only covers b6 and c7, so it does not.
            Assert.AreEqual("a8=Q+", San("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8q"));
            Assert.AreEqual("a8=N", San("4k3/P7/8/8/8/8/8/4K3 w - - 0 1", "a7a8n"));
        }

        [Test]
        public void CheckGetsPlus()
        {
            // Rook lands on e8 giving check, and the king can still move.
            Assert.AreEqual("Re7+", San("4k3/8/8/8/8/8/8/4R2K w - - 0 1", "e1e7"));
        }

        [Test]
        public void MateGetsHash()
        {
            // Back rank mate: rook to a8 with the king boxed in by its own pawns.
            Assert.AreEqual("Ra8#", San("6k1/5ppp/8/8/8/8/8/R5K1 w - - 0 1", "a1a8"));
        }

        [Test]
        public void DisambiguatesByFile()
        {
            // Knights on b1 and f3 can both reach d2; files differ so the file suffices.
            Assert.AreEqual("Nbd2", San("4k3/8/8/8/8/5N2/8/1N2K3 w - - 0 1", "b1d2"));
        }

        [Test]
        public void DisambiguatesByRankWhenFilesMatch()
        {
            // Rooks on a1 and a5 share a file, so the rank identifies the mover.
            Assert.AreEqual("R1a3", San("4k3/8/8/R7/8/8/8/R3K3 w - - 0 1", "a1a3"));
        }

        [Test]
        public void NoDisambiguationWhenOnlyOnePieceCanReach()
        {
            Assert.AreEqual("Nd2", San("4k3/8/8/8/8/8/8/1N2K3 w - - 0 1", "b1d2"));
        }
    }
}
