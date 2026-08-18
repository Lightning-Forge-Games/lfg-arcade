namespace LightningForge.Arcade.Game
{
    /// <summary>How the current game is being played.</summary>
    public enum GameMode
    {
        /// <summary>No game started; the arcade menu is up.</summary>
        None,

        /// <summary>Against the computer.</summary>
        SinglePlayer,

        /// <summary>Two players sharing one device, passing it back and forth.</summary>
        HotSeat,

        /// <summary>Two players on separate devices, matched by code.</summary>
        Online
    }
}
