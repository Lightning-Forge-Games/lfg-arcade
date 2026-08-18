using System;
using LightningForge.Chess.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Title and mode selection, shown over the board on launch and returned to when the
    /// player quits a game.
    ///
    /// Implemented as an overlay in the same scene rather than a separate scene so the
    /// board is already lit and composed behind it, and so an invite link can go straight
    /// into a match without a scene load in between.
    ///
    /// The flow is one panel at a time: Home, then Setup for single player, and Back
    /// always returns to the previous step rather than dumping the player at the start.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreen : MonoBehaviour
    {
        enum Page
        {
            Home,
            SinglePlayerSetup
        }

        [SerializeField] ChessGameController controller;
        [SerializeField] ChessAiPlayer ai;
        [SerializeField] ChessHud hud;
        [SerializeField] BoardCameraRig cameraRig;

        [SerializeField] string gameTitle = "CHESS";

        UIDocument document;
        VisualElement scrim;
        VisualElement homePage;
        VisualElement setupPage;
        Label setupSummary;

        Difficulty chosenDifficulty = Difficulty.Medium;
        PieceColor chosenColour = PieceColor.White;

        public GameMode Mode { get; private set; } = GameMode.None;
        public bool IsShowing => Mode == GameMode.None;

        public event Action<GameMode> ModeChosen;

        /// <summary>Raised when the player leaves a game, so systems can tear down.</summary>
        public event Action Quit;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (controller == null) controller = FindFirstObjectByType<ChessGameController>();
            if (ai == null) ai = FindFirstObjectByType<ChessAiPlayer>();
            if (hud == null) hud = FindFirstObjectByType<ChessHud>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BoardCameraRig>();
        }

        void OnEnable()
        {
            Build();
            Show();
        }

        void Build()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null) return;
            root.Clear();

            scrim = new VisualElement();
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.right = 0; scrim.style.top = 0; scrim.style.bottom = 0;
            scrim.style.backgroundColor = new Color(0.03f, 0.03f, 0.04f, 0.78f);
            scrim.style.alignItems = Align.Center;
            scrim.style.justifyContent = Justify.Center;
            root.Add(scrim);

            var title = new Label(gameTitle);
            title.style.color = new Color(0.95f, 0.92f, 0.86f);
            title.style.fontSize = 76f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 14f;
            scrim.Add(title);

            var subtitle = new Label("Lightning Forge Games");
            subtitle.style.color = new Color(0.62f, 0.58f, 0.52f);
            subtitle.style.fontSize = 14f;
            subtitle.style.letterSpacing = 3f;
            subtitle.style.marginBottom = 38f;
            scrim.Add(subtitle);

            BuildHome(scrim);
            BuildSetup(scrim);
            GoTo(Page.Home);
        }

        void BuildHome(VisualElement parent)
        {
            homePage = new VisualElement();
            homePage.style.alignItems = Align.Center;
            parent.Add(homePage);

            homePage.Add(MakeButton("Single Player", () => GoTo(Page.SinglePlayerSetup)));
            homePage.Add(MakeButton("Play Online", () => Begin(GameMode.Online)));
        }

        void BuildSetup(VisualElement parent)
        {
            setupPage = new VisualElement();
            setupPage.style.alignItems = Align.Center;
            parent.Add(setupPage);

            setupPage.Add(MakeHeading("Difficulty"));
            var difficultyRow = new VisualElement();
            difficultyRow.style.flexDirection = FlexDirection.Row;
            setupPage.Add(difficultyRow);

            foreach (Difficulty level in (Difficulty[])Enum.GetValues(typeof(Difficulty)))
            {
                Difficulty captured = level;
                Button b = MakeButton(level.ToString(), () => { chosenDifficulty = captured; RefreshSetup(); });
                b.style.width = 108f;
                b.style.marginLeft = 4f; b.style.marginRight = 4f;
                difficultyRow.Add(b);
            }

            setupPage.Add(MakeHeading("Play as"));
            var colourRow = new VisualElement();
            colourRow.style.flexDirection = FlexDirection.Row;
            setupPage.Add(colourRow);

            Button white = MakeButton("White", () => { chosenColour = PieceColor.White; RefreshSetup(); });
            white.style.width = 108f; white.style.marginLeft = 4f; white.style.marginRight = 4f;
            colourRow.Add(white);

            Button black = MakeButton("Black", () => { chosenColour = PieceColor.Black; RefreshSetup(); });
            black.style.width = 108f; black.style.marginLeft = 4f; black.style.marginRight = 4f;
            colourRow.Add(black);

            setupSummary = new Label();
            setupSummary.style.color = new Color(0.80f, 0.76f, 0.70f);
            setupSummary.style.fontSize = 13f;
            setupSummary.style.marginTop = 14f;
            setupPage.Add(setupSummary);

            Button start = MakeButton("Start Game", StartSinglePlayer);
            start.style.marginTop = 8f;
            setupPage.Add(start);

            setupPage.Add(MakeButton("Back", () => GoTo(Page.Home)));
        }

        Label MakeHeading(string text)
        {
            var heading = new Label(text);
            heading.style.color = new Color(0.66f, 0.62f, 0.56f);
            heading.style.fontSize = 12f;
            heading.style.letterSpacing = 2f;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginTop = 12f;
            heading.style.marginBottom = 5f;
            return heading;
        }

        Button MakeButton(string text, Action onClick)
        {
            var button = new Button(() => onClick());
            button.text = text;
            button.style.width = 232f;
            button.style.paddingTop = 10f;
            button.style.paddingBottom = 10f;
            button.style.marginTop = 5f;
            button.style.fontSize = 15f;
            button.style.color = new Color(0.93f, 0.90f, 0.84f);
            button.style.backgroundColor = new Color(0.11f, 0.10f, 0.10f, 0.95f);

            button.style.borderTopLeftRadius = 5f; button.style.borderTopRightRadius = 5f;
            button.style.borderBottomLeftRadius = 5f; button.style.borderBottomRightRadius = 5f;

            Color border = new Color(0.42f, 0.33f, 0.22f, 1f);
            button.style.borderTopColor = border; button.style.borderBottomColor = border;
            button.style.borderLeftColor = border; button.style.borderRightColor = border;
            button.style.borderTopWidth = 1f; button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f; button.style.borderRightWidth = 1f;
            UiButtonFeedback.Apply(button);
            return button;
        }

        void GoTo(Page page)
        {
            if (homePage != null) homePage.style.display = page == Page.Home ? DisplayStyle.Flex : DisplayStyle.None;
            if (setupPage != null) setupPage.style.display = page == Page.SinglePlayerSetup ? DisplayStyle.Flex : DisplayStyle.None;
            if (page == Page.SinglePlayerSetup) RefreshSetup();
        }

        void RefreshSetup()
        {
            if (setupSummary == null) return;
            setupSummary.text = chosenDifficulty + " . playing as " + chosenColour;
        }

        void StartSinglePlayer()
        {
            if (ai != null)
            {
                ai.Difficulty = chosenDifficulty;
                ai.Side = chosenColour == PieceColor.White ? PieceColor.Black : PieceColor.White;
                ai.enabled = true;
            }
            Begin(GameMode.SinglePlayer);
        }

        void Begin(GameMode mode)
        {
            // Cancel any thinking left over from a previous game before resetting.
            if (ai != null) ai.Stop();

            Mode = mode;

            if (controller != null)
            {
                controller.NewGame();
                // Online decides the restriction once the match assigns a colour.
                controller.Control = mode == GameMode.SinglePlayer
                    ? (chosenColour == PieceColor.White ? ControlMode.WhiteOnly : ControlMode.BlackOnly)
                    : ControlMode.Both;
            }

            if (ai != null && mode != GameMode.SinglePlayer) ai.enabled = false;

            // Single player sits behind the colour the human chose; online flips later.
            if (cameraRig != null)
            {
                cameraRig.SetViewpoint(mode == GameMode.SinglePlayer ? chosenColour : PieceColor.White);
            }

            Hide();

            Action<GameMode> handler = ModeChosen;
            if (handler != null) handler(mode);

            if (hud != null) hud.Refresh();
            if (ai != null && mode == GameMode.SinglePlayer) ai.Nudge();
        }

        /// <summary>Leaves the current game and returns to the title.</summary>
        public void QuitToMenu()
        {
            if (ai != null)
            {
                ai.Stop();
                ai.enabled = false;
            }

            Action quitHandler = Quit;
            if (quitHandler != null) quitHandler();

            if (controller != null)
            {
                controller.NewGame();
                controller.Control = ControlMode.Both;
            }
            if (cameraRig != null) cameraRig.SetViewpoint(PieceColor.White);

            Show();
        }

        public void Show()
        {
            Mode = GameMode.None;
            if (scrim != null) scrim.style.display = DisplayStyle.Flex;
            GoTo(Page.Home);
            if (ai != null) ai.enabled = false;
            if (hud != null) hud.SetVisible(false);
            // Stop clicks reaching the board through the overlay.
            if (controller != null) controller.AcceptsInput = false;
        }

        public void Hide()
        {
            if (scrim != null) scrim.style.display = DisplayStyle.None;
            if (hud != null) hud.SetVisible(true);
            if (controller != null) controller.AcceptsInput = true;
        }

        /// <summary>Used when an invite link should bypass the menu entirely.</summary>
        public void SkipToOnline()
        {
            Begin(GameMode.Online);
        }
    }
}
