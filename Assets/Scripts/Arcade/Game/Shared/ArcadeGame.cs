using System;
using LightningForge.Arcade.Core;
using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>How a game was started.</summary>
    public struct GameSetup
    {
        public GameMode Mode;
        public Difficulty Difficulty;

        /// <summary>Which seat the local player takes. Ignored in hot seat.</summary>
        public ControlMode Control;

        public static GameSetup HotSeat() =>
            new GameSetup { Mode = GameMode.HotSeat, Control = ControlMode.Both };
    }

    /// <summary>
    /// The contract between the arcade shell and a game.
    ///
    /// The shell owns the menu, the camera and the surrounding chrome; a game owns its
    /// board, its rules and its opponent. Keeping the seam here is what lets the shell
    /// start, stop and describe six different games without knowing anything about any of
    /// them, and it is what the online layer relays across: a game turns a move into a
    /// string and back, and never touches Photon itself.
    /// </summary>
    public abstract class ArcadeGame : MonoBehaviour
    {
        public GameSetup Setup { get; private set; }

        /// <summary>Which catalogue entry this is, so the shell can find it by id.</summary>
        public abstract ArcadeGameId Id { get; }

        /// <summary>Raised whenever the status line should change.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised when the local player commits a move, carrying the encoded form for the
        /// online layer to relay. Not raised for moves arriving from the opponent, or the
        /// two clients would echo each other forever.
        /// </summary>
        public event Action<string> MovePlayed;

        /// <summary>Short line describing whose turn it is, or how the game ended.</summary>
        public abstract string StatusText { get; }

        /// <summary>True once the game is over, so the shell can offer a rematch.</summary>
        public abstract bool IsFinished { get; }

        /// <summary>Builds the board and starts a game under the given setup.</summary>
        public void Begin(GameSetup setup)
        {
            Setup = setup;
            OnBegin();
            Raise();
        }

        /// <summary>Tears the board down. The shell calls this on quit to the menu.</summary>
        public virtual void End()
        {
        }

        /// <summary>
        /// Applies a move that arrived from the opponent. Returns false if the move was not
        /// legal here, which means the two sides have diverged and is worth surfacing
        /// rather than silently ignoring.
        /// </summary>
        public abstract bool ApplyRemoteMove(string encoded);

        /// <summary>Restarts under the same setup.</summary>
        public abstract void Restart();

        /// <summary>
        /// Narrows control to one seat once a match has assigned one.
        ///
        /// Online games start with both seats available, because which one this player gets
        /// is not known until the link object arrives, and that is several seconds after
        /// the board is on screen.
        /// </summary>
        public virtual void AssignOnlineSide(bool firstSeat)
        {
            Setup = new GameSetup
            {
                Mode = Setup.Mode,
                Difficulty = Setup.Difficulty,
                Control = firstSeat ? ControlMode.WhiteOnly : ControlMode.BlackOnly,
            };
            Raise();
        }

        /// <summary>Hands both seats back after a disconnect, so the board stays playable.</summary>
        public virtual void ReleaseOnlineSide()
        {
            Setup = new GameSetup
            {
                Mode = Setup.Mode,
                Difficulty = Setup.Difficulty,
                Control = ControlMode.Both,
            };
            Raise();
        }

        /// <summary>
        /// A compact description of the current position, used only to make a rejected
        /// remote move diagnosable. Two clients that have drifted apart are almost
        /// impossible to debug without seeing both states at the moment they disagreed.
        /// </summary>
        public virtual string DebugState => string.Empty;

        protected abstract void OnBegin();

        protected void Raise() => Changed?.Invoke();

        protected void RaiseMovePlayed(string encoded) => MovePlayed?.Invoke(encoded);

        /// <summary>
        /// Whether the local player is allowed to act right now. Games use this to ignore
        /// input while the opponent or the computer is thinking.
        /// </summary>
        protected bool LocalControls(bool whiteToMove)
        {
            switch (Setup.Control)
            {
                case ControlMode.WhiteOnly: return whiteToMove;
                case ControlMode.BlackOnly: return !whiteToMove;
                default: return true;
            }
        }
    }
}
