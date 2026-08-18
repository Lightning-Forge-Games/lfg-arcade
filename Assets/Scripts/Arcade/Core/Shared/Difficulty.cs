namespace LightningForge.Arcade.Core
{
    /// <summary>
    /// How strong the computer opponent plays. Every game in the arcade offers the same
    /// three settings; what each one means is left to that game's opponent, since a ply of
    /// search is worth very different amounts in chess and in Connect 4.
    /// </summary>
    public enum Difficulty
    {
        Easy,
        Medium,
        Hard
    }
}
