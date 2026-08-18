using System.Collections.Generic;
using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>Every game the arcade knows about.</summary>
    public enum ArcadeGameId
    {
        Chess,
        Draughts,
        Connect4,
        Backgammon,
        Yahtzee,
        Snooker
    }

    /// <summary>What the menu needs to draw a tile, and what the setup screen needs to
    /// know about which modes are worth offering.</summary>
    public sealed class ArcadeGameInfo
    {
        public ArcadeGameId Id;
        public string Title;
        public string Blurb;

        /// <summary>Tile accent, used for the tile edge and its icon.</summary>
        public Color Accent;

        /// <summary>
        /// False when there is nobody to play against. Without a computer opponent the
        /// difficulty and the choice of seat are both meaningless, so the setup screen
        /// drops them and offers a solo game instead of a match.
        /// </summary>
        public bool SupportsSinglePlayer = true;

        /// <summary>False for games that have no online mode.</summary>
        public bool SupportsOnline = true;

        /// <summary>Off until the game is actually built, which keeps the menu honest.</summary>
        public bool Playable;

        /// <summary>Names the two seats, for the side picker and status text.</summary>
        public string FirstSeat = "White";
        public string SecondSeat = "Black";
    }

    /// <summary>
    /// The arcade's game list.
    ///
    /// Kept as data rather than as hand built menu tiles so that adding a game means adding
    /// one entry here plus its scripts, and the menu, the setup screen and the invite link
    /// handling all pick it up without being touched.
    /// </summary>
    public static class ArcadeCatalog
    {
        public static readonly ArcadeGameInfo[] Games =
        {
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Chess,
                Title = "Chess",
                Blurb = "The full game, with a search engine that plays a proper move.",
                Accent = new Color(0.78f, 0.62f, 0.35f),
                Playable = true,
            },
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Draughts,
                Title = "Draughts",
                Blurb = "English rules. Captures are forced, and reaching the back rank crowns.",
                Accent = new Color(0.65f, 0.35f, 0.30f),
                Playable = true,
            },
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Connect4,
                Title = "Connect 4",
                Blurb = "Drop a disc, take a line of four. Harder than it looks.",
                Accent = new Color(0.85f, 0.65f, 0.20f),
                FirstSeat = "Red",
                SecondSeat = "Yellow",
                Playable = true,
            },
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Backgammon,
                Title = "Backgammon",
                Blurb = "Race your checkers home. The dice decide, the choices matter.",
                Accent = new Color(0.55f, 0.45f, 0.65f),
                Playable = true,
            },
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Yahtzee,
                Title = "Yahtzee",
                Blurb = "Five dice, three rolls, thirteen boxes to fill.",
                Accent = new Color(0.40f, 0.65f, 0.55f),
                FirstSeat = "Player 1",
                SecondSeat = "Player 2",
                Playable = true,
            },
            new ArcadeGameInfo
            {
                Id = ArcadeGameId.Snooker,
                Title = "Snooker",
                Blurb = "Reds and colours on a full table. Solo or pass the cue.",
                Accent = new Color(0.35f, 0.60f, 0.40f),
                SupportsOnline = false,
                SupportsSinglePlayer = false,
                FirstSeat = "Player 1",
                SecondSeat = "Player 2",
                Playable = true,
            },
        };

        public static ArcadeGameInfo Get(ArcadeGameId id)
        {
            foreach (ArcadeGameInfo info in Games)
            {
                if (info.Id == id) return info;
            }
            return null;
        }

        /// <summary>
        /// Resolves the id carried in an invite link. Matching on the lower cased enum name
        /// keeps the link readable and means new games need nothing adding here.
        /// </summary>
        public static bool TryParse(string text, out ArcadeGameId id)
        {
            id = ArcadeGameId.Chess;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string wanted = text.Trim().ToLowerInvariant();
            foreach (ArcadeGameInfo info in Games)
            {
                if (info.Id.ToString().ToLowerInvariant() == wanted)
                {
                    id = info.Id;
                    return true;
                }
            }
            return false;
        }

        public static string ToSlug(ArcadeGameId id) => id.ToString().ToLowerInvariant();

        public static IEnumerable<ArcadeGameInfo> Playable()
        {
            foreach (ArcadeGameInfo info in Games)
            {
                if (info.Playable) yield return info;
            }
        }
    }
}
