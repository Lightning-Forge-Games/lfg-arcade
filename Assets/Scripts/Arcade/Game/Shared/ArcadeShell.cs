using System;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// The arcade front end: a grid of games, then a short setup step, then out of the way.
    ///
    /// Every game lives in the one scene as an inactive root object and is switched on when
    /// chosen, rather than each having a scene of its own. The camera, lights, ground and
    /// post processing are shared, so a scene per game would mean six copies of the same
    /// lighting rig drifting apart, and a scene load between the menu and the board.
    ///
    /// The shell deliberately knows nothing about any particular game. It finds them by the
    /// <see cref="ArcadeGame"/> component, describes them from <see cref="ArcadeCatalog"/>,
    /// and talks to them only through <see cref="ArcadeGame.Begin"/> and
    /// <see cref="ArcadeGame.End"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ArcadeShell : MonoBehaviour
    {
        [SerializeField] BoardCameraRig cameraRig;
        [SerializeField] string arcadeTitle = "LFG ARCADE";

        readonly Dictionary<ArcadeGameId, ArcadeGame> games = new Dictionary<ArcadeGameId, ArcadeGame>();

        UIDocument document;
        VisualElement scrim;
        VisualElement gridPage;
        VisualElement setupPage;

        ArcadeGameId chosen = ArcadeGameId.Chess;
        Difficulty chosenDifficulty = Difficulty.Medium;
        ControlMode chosenSide = ControlMode.WhiteOnly;
        Label setupSummary;
        VisualElement difficultyRow;
        VisualElement sideRow;
        Label difficultyHeading;
        Label sideHeading;
        Button singleButton;

        public GameMode Mode { get; private set; } = GameMode.None;
        public bool IsShowing => Mode == GameMode.None;
        public ArcadeGame Current { get; private set; }
        public ArcadeGameId CurrentGame => chosen;

        /// <summary>Raised once a game has actually started.</summary>
        public event Action<GameMode> ModeChosen;

        /// <summary>Raised when the player leaves a game, so systems can tear down.</summary>
        public event Action Quit;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BoardCameraRig>();

            // Inactive included: every game sits switched off until it is picked.
            foreach (ArcadeGame game in FindObjectsByType<ArcadeGame>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                games[game.Id] = game;
                game.gameObject.SetActive(false);
            }
        }

        void OnEnable()
        {
            Build();
            Show();
        }

        // Menu construction ---------------------------------------------------------

        void Build()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null) return;
            root.Clear();

            scrim = new VisualElement();
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.right = 0; scrim.style.top = 0; scrim.style.bottom = 0;
            scrim.style.backgroundColor = ArcadeTheme.Scrim;
            scrim.style.alignItems = Align.Center;
            scrim.style.justifyContent = Justify.Center;
            root.Add(scrim);

            var title = new Label(arcadeTitle);
            title.style.color = ArcadeTheme.TextBright;
            title.style.fontSize = 52f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 12f;
            scrim.Add(title);

            var subtitle = new Label("Lightning Forge Games");
            subtitle.style.color = ArcadeTheme.TextDim;
            subtitle.style.fontSize = 13f;
            subtitle.style.letterSpacing = 3f;
            subtitle.style.marginBottom = 26f;
            scrim.Add(subtitle);

            BuildGrid(scrim);
            BuildSetup(scrim);
            GoToGrid();
        }

        void BuildGrid(VisualElement parent)
        {
            gridPage = new VisualElement();
            gridPage.style.alignItems = Align.Center;
            parent.Add(gridPage);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.justifyContent = Justify.Center;
            row.style.maxWidth = 720f;
            gridPage.Add(row);

            foreach (ArcadeGameInfo info in ArcadeCatalog.Games) row.Add(MakeTile(info));
        }

        /// <summary>
        /// One game tile. Unbuilt games are still shown, greyed and unclickable, so the
        /// arcade reads as a collection with more coming rather than hiding what is planned.
        /// </summary>
        VisualElement MakeTile(ArcadeGameInfo info)
        {
            bool ready = info.Playable && games.ContainsKey(info.Id);

            var tile = new VisualElement();
            tile.style.width = 210f;
            tile.style.height = 116f;
            tile.style.marginLeft = 7f; tile.style.marginRight = 7f;
            tile.style.marginTop = 7f; tile.style.marginBottom = 7f;
            tile.style.paddingLeft = 14f; tile.style.paddingRight = 14f;
            tile.style.paddingTop = 12f; tile.style.paddingBottom = 12f;
            tile.style.backgroundColor = ready
                ? new Color(0.11f, 0.10f, 0.10f, 0.95f)
                : new Color(0.08f, 0.08f, 0.08f, 0.80f);
            ArcadeTheme.Round(tile, 6f);
            ArcadeTheme.Border(tile, ready ? info.Accent * 0.7f : new Color(0.20f, 0.19f, 0.18f), 1f);

            var name = new Label(info.Title);
            name.style.color = ready ? ArcadeTheme.TextBright : new Color(0.45f, 0.43f, 0.40f);
            name.style.fontSize = 20f;
            name.style.unityFontStyleAndWeight = FontStyle.Bold;
            tile.Add(name);

            var blurb = new Label(ready ? info.Blurb : "Coming soon.");
            blurb.style.color = ready ? ArcadeTheme.TextDim : new Color(0.35f, 0.34f, 0.32f);
            blurb.style.fontSize = 11f;
            blurb.style.whiteSpace = WhiteSpace.Normal;
            blurb.style.marginTop = 6f;
            blurb.style.flexGrow = 1f;
            tile.Add(blurb);

            if (!ready) return tile;

            // A hover lift, so the grid feels like something you are choosing from.
            tile.RegisterCallback<PointerEnterEvent>(_ =>
            {
                tile.style.backgroundColor = new Color(0.16f, 0.14f, 0.13f, 0.98f);
                ArcadeTheme.Border(tile, info.Accent, 1f);
            });
            tile.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                tile.style.backgroundColor = new Color(0.11f, 0.10f, 0.10f, 0.95f);
                ArcadeTheme.Border(tile, info.Accent * 0.7f, 1f);
            });
            tile.RegisterCallback<PointerDownEvent>(_ => GoToSetup(info.Id));

            return tile;
        }

        void BuildSetup(VisualElement parent)
        {
            setupPage = new VisualElement();
            setupPage.style.alignItems = Align.Center;
            parent.Add(setupPage);

            setupSummary = new Label();
            setupSummary.style.color = ArcadeTheme.TextBright;
            setupSummary.style.fontSize = 24f;
            setupSummary.style.unityFontStyleAndWeight = FontStyle.Bold;
            setupSummary.style.marginBottom = 14f;
            setupPage.Add(setupSummary);

            difficultyHeading = ArcadeTheme.Heading("Difficulty");
            setupPage.Add(difficultyHeading);
            difficultyRow = new VisualElement();
            difficultyRow.style.flexDirection = FlexDirection.Row;
            setupPage.Add(difficultyRow);
            foreach (Difficulty level in (Difficulty[])Enum.GetValues(typeof(Difficulty)))
            {
                Difficulty captured = level;
                Button b = ArcadeTheme.MakeButton(level.ToString(),
                    () => { chosenDifficulty = captured; RefreshSetup(); }, 104f);
                b.style.marginLeft = 4f; b.style.marginRight = 4f;
                difficultyRow.Add(b);
            }

            sideHeading = ArcadeTheme.Heading("Play as");
            sideHeading.style.marginTop = 12f;
            setupPage.Add(sideHeading);
            sideRow = new VisualElement();
            sideRow.style.flexDirection = FlexDirection.Row;
            setupPage.Add(sideRow);

            var actions = new VisualElement();
            actions.style.flexDirection = FlexDirection.Row;
            actions.style.marginTop = 18f;
            setupPage.Add(actions);

            singleButton = ArcadeTheme.MakeButton("Single Player",
                () => StartGame(GameMode.SinglePlayer), 150f);
            singleButton.style.marginLeft = 4f; singleButton.style.marginRight = 4f;
            actions.Add(singleButton);

            Button hotseat = ArcadeTheme.MakeButton("Hot Seat",
                () => StartGame(GameMode.HotSeat), 130f);
            hotseat.style.marginLeft = 4f; hotseat.style.marginRight = 4f;
            actions.Add(hotseat);

            Button online = ArcadeTheme.MakeButton("Play Online",
                () => StartGame(GameMode.Online), 150f);
            online.style.marginLeft = 4f; online.style.marginRight = 4f;
            online.name = "online-button";
            actions.Add(online);

            Button back = ArcadeTheme.MakeButton("Back", GoToGrid, 100f);
            back.style.marginTop = 16f;
            setupPage.Add(back);
        }

        void RefreshSetup()
        {
            ArcadeGameInfo info = ArcadeCatalog.Get(chosen);
            if (info == null) return;

            setupSummary.text = info.Title;

            // With no computer opponent there is nothing to set a difficulty for and no
            // side to take, so both pickers go rather than sitting there doing nothing.
            DisplayStyle versusComputer = info.SupportsSinglePlayer
                ? DisplayStyle.Flex
                : DisplayStyle.None;
            difficultyHeading.style.display = versusComputer;
            difficultyRow.style.display = versusComputer;
            sideHeading.style.display = versusComputer;
            sideRow.style.display = versusComputer;
            singleButton.text = info.SupportsSinglePlayer ? "Single Player" : "Solo";

            // The side picker only names seats the game actually has.
            sideRow.Clear();
            foreach (var pair in new[]
                     {
                         new KeyValuePair<string, ControlMode>(info.FirstSeat, ControlMode.WhiteOnly),
                         new KeyValuePair<string, ControlMode>(info.SecondSeat, ControlMode.BlackOnly),
                     })
            {
                ControlMode captured = pair.Value;
                Button b = ArcadeTheme.MakeButton(pair.Key,
                    () => { chosenSide = captured; RefreshSetup(); }, 128f);
                b.style.marginLeft = 4f; b.style.marginRight = 4f;
                if (chosenSide == captured) ArcadeTheme.Border(b, ArcadeTheme.BrassBright, 2f);
                sideRow.Add(b);
            }

            foreach (VisualElement child in difficultyRow.Children())
            {
                if (child is Button b)
                {
                    bool selected = b.text == chosenDifficulty.ToString();
                    ArcadeTheme.Border(b, selected ? ArcadeTheme.BrassBright : ArcadeTheme.Brass,
                        selected ? 2f : 1f);
                }
            }

            var onlineButton = setupPage.Q<Button>("online-button");
            if (onlineButton != null)
            {
                onlineButton.style.display = info.SupportsOnline ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        // Navigation ----------------------------------------------------------------

        void GoToGrid()
        {
            if (gridPage != null) gridPage.style.display = DisplayStyle.Flex;
            if (setupPage != null) setupPage.style.display = DisplayStyle.None;
        }

        void GoToSetup(ArcadeGameId id)
        {
            chosen = id;
            if (gridPage != null) gridPage.style.display = DisplayStyle.None;
            if (setupPage != null) setupPage.style.display = DisplayStyle.Flex;
            RefreshSetup();
        }

        // Running a game ------------------------------------------------------------

        void StartGame(GameMode mode)
        {
            if (!games.TryGetValue(chosen, out ArcadeGame game) || game == null)
            {
                Debug.LogError("ArcadeShell: no game object for " + chosen);
                return;
            }

            Mode = mode;
            Current = game;
            game.gameObject.SetActive(true);

            var setup = new GameSetup
            {
                Mode = mode,
                Difficulty = chosenDifficulty,
                // Hot seat drives both sides. Online starts driving both and narrows to one
                // once the match assigns a colour, which is the point the link knows.
                Control = mode == GameMode.SinglePlayer ? chosenSide : ControlMode.Both,
            };

            game.Begin(setup);
            Hide();
            ModeChosen?.Invoke(mode);
        }

        /// <summary>Leaves the current game and returns to the grid.</summary>
        public void QuitToMenu()
        {
            Quit?.Invoke();

            if (Current != null)
            {
                Current.End();
                Current.gameObject.SetActive(false);
                Current = null;
            }

            if (cameraRig != null) cameraRig.SetViewpoint(Core.Chess.PieceColor.White);
            Show();
        }

        /// <summary>
        /// Drops straight into a game in online mode, bypassing the menu. Used when the
        /// page was opened from an invite link, where the choice has already been made.
        /// </summary>
        public void SkipToOnline(ArcadeGameId id)
        {
            chosen = id;
            StartGame(GameMode.Online);
        }

        public void Show()
        {
            Mode = GameMode.None;
            if (scrim != null) scrim.style.display = DisplayStyle.Flex;
            GoToGrid();
        }

        public void Hide()
        {
            if (scrim != null) scrim.style.display = DisplayStyle.None;
        }
    }
}
