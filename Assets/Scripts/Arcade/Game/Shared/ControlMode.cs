namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// Which side the local player is allowed to move.
    ///
    /// Named for chess, draughts and backgammon, which all have a white and a black side.
    /// Games without colours map onto the same two seats: White is whoever moves first,
    /// Black is the other player.
    /// </summary>
    public enum ControlMode
    {
        /// <summary>Hot seat: one machine plays both sides.</summary>
        Both,
        WhiteOnly,
        BlackOnly
    }
}
