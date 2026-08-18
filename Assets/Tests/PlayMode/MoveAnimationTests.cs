using LightningForge.Arcade.Core;
using System.Collections;
using LightningForge.Arcade.Core.Chess;
using LightningForge.Arcade.Game;
using LightningForge.Arcade.Game.Chess;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LightningForge.Arcade.Tests.PlayMode
{
    /// <summary>
    /// Covers the bookkeeping that keeps piece views aligned with the board across animated
    /// moves. Castling, en passant and promotion each move or remove a piece on a square
    /// other than the move's destination, which is exactly where a view table drifts.
    /// </summary>
    public class MoveAnimationTests
    {
        GameObject root;
        ChessGameController controller;

        static void SetPrivate(object target, string field, object value)
        {
            typeof(ChessGameController)
                .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(target, value);
        }

        static ChessGameController Build(string fen)
        {
            var go = new GameObject("TestGame");
            var boardView = go.AddComponent<SquareBoardView>();
            var factory = go.AddComponent<PieceViewFactory>();
            var controller = go.AddComponent<ChessGameController>();

            // These are private serialised fields that the scene normally supplies. Awake
            // has already run by now, so assign them and start the game over.
            SetPrivate(controller, "startingFen", fen);
            SetPrivate(controller, "boardView", boardView);
            SetPrivate(controller, "pieceFactory", factory);

            controller.NewGame();
            return controller;
        }

        [SetUp]
        public void SetUp()
        {
            root = null;
            controller = null;
        }

        [TearDown]
        public void TearDown()
        {
            if (controller != null) Object.DestroyImmediate(controller.gameObject);
            if (root != null) Object.DestroyImmediate(root);
        }

        IEnumerator PlayAndSettle(string uci)
        {
            Assert.IsTrue(controller.TryPlayUci(uci), "expected " + uci + " to be legal");
            yield return Settle(uci);
        }

        IEnumerator Settle(string what)
        {
            float timeout = 5f;
            while (controller.IsAnimating && timeout > 0f)
            {
                timeout -= Time.deltaTime;
                yield return null;
            }
            Assert.IsFalse(controller.IsAnimating, "animation did not finish for " + what);

            // Destroy is deferred to the end of the frame, so a captured view only compares
            // equal to null once another frame has ticked.
            yield return null;
        }

        static int Sq(string algebraic) => Square.FromAlgebraic(algebraic);

        [UnityTest]
        public IEnumerator SimpleMoveTransfersTheView()
        {
            controller = Build(Board.StartFen);
            GameObject pawn = controller.GetPieceView(Sq("e2"));
            Assert.IsNotNull(pawn);

            yield return PlayAndSettle("e2e4");

            Assert.IsNull(controller.GetPieceView(Sq("e2")), "origin square should be empty");
            Assert.AreSame(pawn, controller.GetPieceView(Sq("e4")), "same view should have moved");
        }

        [UnityTest]
        public IEnumerator CaptureRemovesTheTakenView()
        {
            // White pawn on e4, black pawn on d5.
            controller = Build("rnbqkbnr/ppp1pppp/8/3p4/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2");
            GameObject taken = controller.GetPieceView(Sq("d5"));
            GameObject taker = controller.GetPieceView(Sq("e4"));
            Assert.IsNotNull(taken);

            yield return PlayAndSettle("e4d5");

            Assert.AreSame(taker, controller.GetPieceView(Sq("d5")), "attacker should occupy d5");
            Assert.IsTrue(taken == null, "captured view should have been destroyed");
        }

        [UnityTest]
        public IEnumerator CastlingMovesTheRookView()
        {
            controller = Build("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            GameObject king = controller.GetPieceView(Sq("e1"));
            GameObject rook = controller.GetPieceView(Sq("h1"));

            yield return PlayAndSettle("e1g1");

            Assert.AreSame(king, controller.GetPieceView(Sq("g1")), "king should be on g1");
            Assert.AreSame(rook, controller.GetPieceView(Sq("f1")), "rook should have moved to f1");
            Assert.IsNull(controller.GetPieceView(Sq("h1")), "rook origin should be empty");
            Assert.IsNull(controller.GetPieceView(Sq("e1")), "king origin should be empty");
        }

        [UnityTest]
        public IEnumerator QueensideCastlingMovesTheRookView()
        {
            controller = Build("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");
            GameObject rook = controller.GetPieceView(Sq("a1"));

            yield return PlayAndSettle("e1c1");

            Assert.AreSame(rook, controller.GetPieceView(Sq("d1")), "rook should have moved to d1");
            Assert.IsNull(controller.GetPieceView(Sq("a1")));
        }

        [UnityTest]
        public IEnumerator EnPassantRemovesThePawnBesideTheDestination()
        {
            // White pawn e5, black pawn just played d7d5.
            controller = Build("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");
            GameObject victim = controller.GetPieceView(Sq("d5"));
            GameObject taker = controller.GetPieceView(Sq("e5"));
            Assert.IsNotNull(victim);

            yield return PlayAndSettle("e5d6");

            Assert.AreSame(taker, controller.GetPieceView(Sq("d6")), "pawn should stand on d6");
            Assert.IsTrue(victim == null, "the pawn on d5 should have been removed");
            Assert.IsNull(controller.GetPieceView(Sq("d5")), "d5 must be clear in the view table");
        }

        [UnityTest]
        public IEnumerator PromotionReplacesTheViewWithTheNewPiece()
        {
            controller = Build("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
            GameObject pawn = controller.GetPieceView(Sq("a7"));

            yield return PlayAndSettle("a7a8q");

            GameObject promoted = controller.GetPieceView(Sq("a8"));
            Assert.IsNotNull(promoted, "a queen view should stand on a8");
            Assert.AreNotSame(pawn, promoted, "the pawn view should have been replaced");
            Assert.AreEqual(PieceType.Queen, controller.Board[Sq("a8")].Type);
        }

        [UnityTest]
        public IEnumerator InputIsIgnoredWhileAMenuCoversTheBoard()
        {
            controller = Build(Board.StartFen);
            controller.AcceptsInput = false;

            controller.HandleSquarePicked(Sq("e2"));
            controller.HandleSquarePicked(Sq("e4"));
            yield return null;

            Assert.AreEqual(Board.StartFen, controller.Board.ToFen(),
                "clicks must not reach the board while a menu is up");

            controller.AcceptsInput = true;
            controller.HandleSquarePicked(Sq("e2"));
            controller.HandleSquarePicked(Sq("e4"));
            yield return Settle("e2e4 after re-enabling input");

            Assert.AreNotEqual(Board.StartFen, controller.Board.ToFen(), "input should work again");
        }

        [UnityTest]
        public IEnumerator PickerMakesUnderpromotionReachable()
        {
            controller = Build("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

            int requestedFrom = Square.None;
            int requestedTo = Square.None;
            controller.PromotionRequested += (from, to) => { requestedFrom = from; requestedTo = to; };

            controller.HandleSquarePicked(Sq("a7"));
            controller.HandleSquarePicked(Sq("a8"));

            Assert.AreEqual(Sq("a7"), requestedFrom, "picker should be asked about the pawn");
            Assert.AreEqual(Sq("a8"), requestedTo);
            Assert.IsTrue(controller.AwaitingPromotion, "controller should be waiting for a choice");
            Assert.IsTrue(controller.Board[Sq("a7")].IsSome, "the move must not play before choosing");

            Assert.IsTrue(controller.CompletePromotion(PieceType.Knight));
            yield return Settle("underpromotion");

            Assert.AreEqual(PieceType.Knight, controller.Board[Sq("a8")].Type,
                "underpromotion to a knight should be possible");
            Assert.IsFalse(controller.AwaitingPromotion);
        }

        [UnityTest]
        public IEnumerator PromotionAutoQueensWhenNothingIsListening()
        {
            controller = Build("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

            controller.HandleSquarePicked(Sq("a7"));
            controller.HandleSquarePicked(Sq("a8"));
            yield return Settle("auto promotion");

            Assert.AreEqual(PieceType.Queen, controller.Board[Sq("a8")].Type,
                "with no picker attached the pawn should still promote");
        }

        [UnityTest]
        public IEnumerator EveryOccupiedSquareHasExactlyOneView()
        {
            controller = Build(Board.StartFen);

            yield return PlayAndSettle("e2e4");
            yield return PlayAndSettle("d7d5");
            yield return PlayAndSettle("e4d5");
            yield return PlayAndSettle("g8f6");

            for (int square = 0; square < Square.Count; square++)
            {
                bool occupied = controller.Board[square].IsSome;
                GameObject view = controller.GetPieceView(square);
                Assert.AreEqual(occupied, view != null,
                    "mismatch at " + Square.ToAlgebraic(square) + " (occupied=" + occupied + ")");
            }
        }
    }
}
