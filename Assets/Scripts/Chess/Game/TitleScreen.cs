using System;
using LightningForge.Chess.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Title and mode selection, shown over the board on launch.
    ///
    /// Implemented as an overlay in the same scene rather than a separate scene so the
    /// board is already lit and composed behind it, and so an invite link can skip
    /// straight into a match without a scene load in between.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TitleScreen : MonoBehaviour
    {
        [SerializeField] ChessGameController controller;
        [SerializeField] ChessAiPlayer ai;
        [SerializeField] ChessHud hud;
        [SerializeField] BoardCameraRig cameraRig;

        [Tooltip("Title shown on the front screen.")]
        [SerializeField] string gameTitle = "CHESS";

        UIDocument document;
        VisualElement root;
        VisualElement titlePanel;
        VisualElement difficultyPanel;

        public GameMode Mode { get; private set; } = GameMode.None;

        /// <summary>Raised when a mode is chosen, so other systems can configure themselves.</summary>
        public event Action<GameMode> ModeChosen;

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
            root = document.rootVisualElement;
            if (root == null) return;
            root.Clear();

            // Dim the board behind so the title reads clearly.
            var scrim = new VisualElement();
            scrim.style.position = Position.Absolute;
            scrim.style.left = 0; scrim.style.right = 0; scrim.style.top = 0; scrim.style.bottom = 0;
            scrim.style.backgroundColor = new Color(0.03f, 0.03f, 0.04f, 0.72f);
            scrim.style.alignItems = Align.Center;
            scrim.style.justifyContent = Justify.Center;
            root.Add(scrim);
            titlePanel = scrim;

            var title = new Label(gameTitle);
            title.style.color = new Color(0.95f, 0.92f, 0.86f);
            title.style.fontSize = 76f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.letterSpacing = 14f;
            title.style.marginBottom = 4f;
            scrim.Add(title);

            var subtitle = new Label("Lightning Forge Games");
            subtitle.style.color = new Color(0.62f, 0.58f, 0.52f);
            subtitle.style.fontSize = 14f;
            subtitle.style.letterSpacing = 3f;
            subtitle.style.marginBottom = 40f;
            scrim.Add(subtitle);

            var single = MakeButton("Single Player", () => ShowDifficulty(true));
            scrim.Add(single);

            var hotSeat = MakeButton("Two Players, One Device", () => Begin(GameMode.HotSeat));
            scrim.Add(hotSeat);

            var online = MakeButton("Play Online", () => Begin(GameMode.Online));
            scrim.Add(online);

            BuildDifficultyPanel(scrim);
        }

        void BuildDifficultyPanel(VisualElement parent)
        {
            difficultyPanel = new VisualElement();
            difficultyPanel.style.marginTop = 14f;
            difficultyPanel.style.alignItems = Align.Center;
            difficultyPanel.style.display = DisplayStyle.None;
            parent.Add(difficultyPanel);

            var prompt = new Label("Choose a difficulty");
            prompt.style.color = new Color(0.80f, 0.76f, 0.70f);
            prompt.style.fontSize = 14f;
            prompt.style.marginBottom = 8f;
            difficultyPanel.Add(prompt);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            difficultyPanel.Add(row);

            foreach (Difficulty level in (Difficulty[])Enum.GetValues(typeof(Difficulty)))
            {
                Difficulty captured = level;
                var button = MakeButton(level.ToString(), () => StartSinglePlayer(captured));
                button.style.marginLeft = 5f;
                button.style.marginRight = 5f;
                button.style.width = 118f;
                row.Add(button);
            }

            var back = MakeButton("Back", () => ShowDifficulty(false));
            back.style.marginTop = 10f;
            back.style.width = 118f;
            difficultyPanel.Add(back);
        }

        Button MakeButton(string text, Action onClick)
        {
            var button = new Button(() => onClick());
            button.text = text;
            button.style.width = 260f;
            button.style.paddingTop = 11f;
            button.style.paddingBottom = 11f;
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
            return button;
        }

        void ShowDifficulty(bool visible)
        {
            if (difficultyPanel == null) return;
            difficultyPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void StartSinglePlayer(Difficulty level)
        {
            if (ai != null)
            {
                ai.Difficulty = level;
                ai.Side = PieceColor.Black;   // the human opens as White
                ai.enabled = true;
            }
            Begin(GameMode.SinglePlayer);
        }

        void Begin(GameMode mode)
        {
            Mode = mode;

            // Cancel any thinking left over from a previous game before resetting.
            if (ai != null) ai.Stop();

            if (controller != null)
            {
                controller.NewGame();
                // Online sets its own restriction once the link decides our colour.
                controller.Control = mode == GameMode.SinglePlayer
                    ? ControlMode.WhiteOnly
                    : ControlMode.Both;
            }

            if (ai != null && mode != GameMode.SinglePlayer) ai.enabled = false;
            if (cameraRig != null) cameraRig.SetViewpoint(PieceColor.White);

            Hide();

            Action<GameMode> handler = ModeChosen;
            if (handler != null) handler(mode);

            if (hud != null) hud.Refresh();
            if (ai != null && mode == GameMode.SinglePlayer) ai.Nudge();
        }

        public void Show()
        {
            Mode = GameMode.None;
            if (titlePanel != null) titlePanel.style.display = DisplayStyle.Flex;
            ShowDifficulty(false);
            if (ai != null) ai.enabled = false;
            if (hud != null) hud.SetVisible(false);
        }

        public void Hide()
        {
            if (titlePanel != null) titlePanel.style.display = DisplayStyle.None;
            if (hud != null) hud.SetVisible(true);
        }

        /// <summary>Used when an invite link should bypass the menu entirely.</summary>
        public void SkipToOnline()
        {
            Begin(GameMode.Online);
        }
    }
}
