using Fusion;
using LightningForge.Chess.Core;
using LightningForge.Chess.Game;
using UnityEngine;

namespace LightningForge.Chess.Net
{
    /// <summary>
    /// Carries moves between the two players.
    ///
    /// Both clients run the same perft-verified rules engine and validate locally, so this
    /// only needs to relay the move that was played. Moves travel as UCI strings ("e2e4",
    /// "e7e8q"), which are compact, human readable in logs, and already produced by the
    /// core, so there is no separate wire format to keep in sync.
    ///
    /// In Shared Mode the client that spawned this object has state authority, and that is
    /// what decides who plays White.
    /// </summary>
    public class ChessNetLink : NetworkBehaviour
    {
        ChessGameController controller;
        bool applyingRemoteMove;

        public bool IsWhite { get; private set; }

        public override void Spawned()
        {
            controller = FindFirstObjectByType<ChessGameController>();
            if (controller == null)
            {
                Debug.LogError("ChessNetLink: no ChessGameController in the scene.");
                return;
            }

            // Whoever spawned this is White; whoever received it is Black.
            IsWhite = Object.HasStateAuthority;
            controller.Control = IsWhite ? ControlMode.WhiteOnly : ControlMode.BlackOnly;

            // Put the camera behind whichever side we are playing.
            BoardCameraRig rig = FindFirstObjectByType<BoardCameraRig>();
            if (rig != null) rig.SetViewpoint(IsWhite ? PieceColor.White : PieceColor.Black);

            controller.MoveMade += OnLocalMoveMade;

            Debug.Log("ChessNetLink spawned. Playing as " + (IsWhite ? "White" : "Black"));
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (controller != null)
            {
                controller.MoveMade -= OnLocalMoveMade;
                // Fall back to hot seat so the game stays playable after a disconnect.
                controller.Control = ControlMode.Both;
            }

            BoardCameraRig rig = FindFirstObjectByType<BoardCameraRig>();
            if (rig != null) rig.SetViewpoint(PieceColor.White);
        }

        void OnLocalMoveMade(Move move)
        {
            // Ignore moves we are applying on behalf of the opponent, or we would echo them.
            if (applyingRemoteMove) return;
            RpcPlayMove(move.ToUci());
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RpcPlayMove(string uci, RpcInfo info = default)
        {
            // RpcTargets.All includes the sender, who has already played this locally.
            if (info.Source == Runner.LocalPlayer) return;
            if (controller == null) return;

            applyingRemoteMove = true;
            bool applied = controller.TryPlayUci(uci);
            applyingRemoteMove = false;

            if (!applied)
            {
                // Both sides run identical rules, so this means the games have diverged.
                Debug.LogError("ChessNetLink: rejected remote move '" + uci
                    + "'. Local position: " + controller.Board.ToFen());
            }
        }
    }
}

