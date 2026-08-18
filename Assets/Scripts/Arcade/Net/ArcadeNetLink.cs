using Fusion;
using LightningForge.Arcade.Game;
using UnityEngine;

namespace LightningForge.Arcade.Net
{
    /// <summary>
    /// Carries moves between the two players, for whichever game is being played.
    ///
    /// Both clients run the same rules engine and validate locally, so this only has to
    /// relay the move that was made. Moves travel as opaque strings that the game itself
    /// encodes and decodes: chess sends UCI ("e2e4"), Connect 4 sends a column, backgammon
    /// sends a roll and the points moved. Nothing here needs to understand any of them,
    /// which is what lets one link serve every game in the arcade.
    ///
    /// In Shared Mode the client that spawned this object has state authority, and that is
    /// what decides which player takes the first seat.
    /// </summary>
    public class ArcadeNetLink : NetworkBehaviour
    {
        ArcadeGame game;
        bool applyingRemoteMove;

        /// <summary>True when this client took the first seat: White, Red, or Player 1.</summary>
        public bool IsFirstSeat { get; private set; }

        public override void Spawned()
        {
            ArcadeShell shell = FindFirstObjectByType<ArcadeShell>();
            game = shell != null ? shell.Current : null;

            if (game == null)
            {
                // Nothing is running to relay for, which means the match connected before a
                // game was started. The lobby is only offered from inside a game, so this
                // is a wiring fault rather than something a player can do.
                Debug.LogError("ArcadeNetLink: no game is running to attach to.");
                return;
            }

            // Whoever spawned this took the first seat; whoever received it takes the second.
            IsFirstSeat = Object.HasStateAuthority;
            game.AssignOnlineSide(IsFirstSeat);
            game.MovePlayed += OnLocalMovePlayed;

            Debug.Log("ArcadeNetLink spawned for " + game.Id + ". Playing "
                + (IsFirstSeat ? "first seat" : "second seat") + ".");
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (game == null) return;

            game.MovePlayed -= OnLocalMovePlayed;
            // Fall back to hot seat so the game stays playable after a disconnect, rather
            // than locking the player out of a side nobody is left to play.
            game.ReleaseOnlineSide();
            game = null;
        }

        void OnLocalMovePlayed(string encoded)
        {
            // Moves being applied on behalf of the opponent must not be sent back.
            if (applyingRemoteMove) return;
            RpcPlayMove(encoded);
        }

        [Rpc(RpcSources.All, RpcTargets.All)]
        public void RpcPlayMove(string encoded, RpcInfo info = default)
        {
            // RpcTargets.All includes the sender, who has already played this locally.
            if (info.Source == Runner.LocalPlayer) return;
            if (game == null) return;

            applyingRemoteMove = true;
            bool applied = game.ApplyRemoteMove(encoded);
            applyingRemoteMove = false;

            if (!applied)
            {
                // Both sides run identical rules, so a rejection means the two games have
                // drifted apart. Dropping it silently would leave the boards disagreeing
                // with nothing to say why.
                Debug.LogError("ArcadeNetLink: " + game.Id + " rejected remote move '"
                    + encoded + "'. Local state: " + game.DebugState);
            }
        }
    }
}
