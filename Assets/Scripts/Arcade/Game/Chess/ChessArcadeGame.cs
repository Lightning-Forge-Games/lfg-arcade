using LightningForge.Arcade.Core.Chess;
using UnityEngine;

namespace LightningForge.Arcade.Game.Chess
{
    /// <summary>
    /// Presents chess to the arcade shell.
    ///
    /// Chess was built before the arcade existed and its controller, board view, AI and HUD
    /// all work; rather than rewrite them to a shared shape, this sits alongside them and
    /// speaks the shell's language. It is also the only thing that knows chess moves travel
    /// as UCI strings, which keeps that detail out of the online layer.
    /// </summary>
    [RequireComponent(typeof(ChessGameController))]
    public class ChessArcadeGame : ArcadeGame
    {
        [SerializeField] ChessGameController controller;
        [SerializeField] ChessAiPlayer ai;
        [SerializeField] BoardCameraRig cameraRig;
        [SerializeField] ChessHud hud;

        bool applyingRemoteMove;

        public override ArcadeGameId Id => ArcadeGameId.Chess;

        /// <summary>Chess has its own HUD, with a move list, a captured tray and promotion.</summary>
        public override bool UsesSharedHud => false;

        public override bool IsFinished =>
            controller != null && controller.Status != GameStatus.Ongoing;

        public override string StatusText
        {
            get
            {
                if (controller == null) return string.Empty;

                bool whiteToMove = controller.Board.SideToMove == PieceColor.White;
                string toMove = whiteToMove ? "White to move" : "Black to move";

                switch (controller.Status)
                {
                    // The side to move is the one with no escape, so the other side won.
                    case GameStatus.Checkmate: return whiteToMove ? "Black wins" : "White wins";
                    case GameStatus.Stalemate: return "Stalemate";
                    case GameStatus.DrawByFiftyMoveRule: return "Draw, fifty move rule";
                    case GameStatus.DrawByInsufficientMaterial: return "Draw, insufficient material";
                    case GameStatus.Check: return toMove + ", in check";
                    default: return toMove;
                }
            }
        }

        void Awake()
        {
            if (controller == null) controller = GetComponent<ChessGameController>();
            if (ai == null) ai = GetComponent<ChessAiPlayer>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BoardCameraRig>();
            if (hud == null) hud = FindFirstObjectByType<ChessHud>();
        }

        protected override void OnBegin()
        {
            if (controller == null) return;

            if (ai != null) ai.Stop();

            controller.MoveMade -= OnMoveMade;
            controller.MoveMade += OnMoveMade;
            controller.StatusChanged -= OnStatusChanged;
            controller.StatusChanged += OnStatusChanged;

            controller.NewGame();
            controller.Control = Setup.Control;

            bool versusComputer = Setup.Mode == GameMode.SinglePlayer;
            if (ai != null)
            {
                ai.enabled = versusComputer;
                if (versusComputer)
                {
                    ai.Difficulty = Setup.Difficulty;
                    // The computer takes whichever seat the player did not.
                    ai.Side = Setup.Control == ControlMode.WhiteOnly
                        ? PieceColor.Black
                        : PieceColor.White;
                }
            }

            // Sit behind the side being played. Online flips again once the match assigns
            // a colour, which is later than this.
            if (cameraRig != null)
            {
                cameraRig.SetViewpoint(Setup.Control == ControlMode.BlackOnly
                    ? PieceColor.Black
                    : PieceColor.White);
            }

            controller.AcceptsInput = true;
            if (hud != null)
            {
                hud.SetVisible(true);
                hud.Refresh();
            }

            // Nothing prompts the computer to move when it has the first turn.
            if (versusComputer && ai != null) ai.Nudge();
        }

        public override void End()
        {
            if (ai != null)
            {
                ai.Stop();
                ai.enabled = false;
            }
            if (controller != null)
            {
                controller.MoveMade -= OnMoveMade;
                controller.StatusChanged -= OnStatusChanged;
                controller.Control = ControlMode.Both;
                controller.AcceptsInput = false;
            }
            if (hud != null) hud.SetVisible(false);
        }

        public override void Restart()
        {
            Begin(Setup);
        }

        public override void AssignOnlineSide(bool firstSeat)
        {
            base.AssignOnlineSide(firstSeat);
            if (controller == null) return;

            controller.Control = Setup.Control;
            // Sit behind the side actually being played, which is only known now.
            if (cameraRig != null)
            {
                cameraRig.SetViewpoint(firstSeat ? PieceColor.White : PieceColor.Black);
            }
        }

        public override void ReleaseOnlineSide()
        {
            base.ReleaseOnlineSide();
            if (controller != null) controller.Control = ControlMode.Both;
            if (cameraRig != null) cameraRig.SetViewpoint(PieceColor.White);
        }

        public override string DebugState =>
            controller != null && controller.Board != null ? controller.Board.ToFen() : "no board";

        public override bool ApplyRemoteMove(string encoded)
        {
            if (controller == null) return false;

            applyingRemoteMove = true;
            bool applied = controller.TryPlayUci(encoded);
            applyingRemoteMove = false;

            if (applied) Raise();
            return applied;
        }

        void OnMoveMade(Move move)
        {
            // Moves arriving from the opponent must not be sent back out.
            if (!applyingRemoteMove) RaiseMovePlayed(move.ToUci());
            Raise();
        }

        void OnStatusChanged(GameStatus status) => Raise();
    }
}
