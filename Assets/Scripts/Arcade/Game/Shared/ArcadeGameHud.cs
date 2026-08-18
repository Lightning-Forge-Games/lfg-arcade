using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// The in-game chrome every game gets for free: whose turn it is, a way to start again,
    /// and a way back to the arcade.
    ///
    /// Chess brought its own HUD with a move list, captured pieces and a promotion picker,
    /// and opts out. Everything else would otherwise need its own copy of the same three
    /// controls, which is how six games end up with six slightly different quit buttons.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ArcadeGameHud : MonoBehaviour
    {
        [SerializeField] ArcadeShell shell;

        UIDocument document;
        VisualElement root;
        Label statusLabel;
        VisualElement resultPanel;
        Label resultLabel;

        ArcadeGame watched;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (shell == null) shell = FindFirstObjectByType<ArcadeShell>();
        }

        void OnEnable()
        {
            Build();
            if (shell != null)
            {
                shell.ModeChosen += OnModeChosen;
                shell.Quit += OnQuit;
            }
            Refresh();
        }

        void OnDisable()
        {
            if (shell != null)
            {
                shell.ModeChosen -= OnModeChosen;
                shell.Quit -= OnQuit;
            }
            Unwatch();
        }

        void OnModeChosen(GameMode mode)
        {
            Unwatch();
            watched = shell != null ? shell.Current : null;
            if (watched != null) watched.Changed += Refresh;
            Refresh();
        }

        void OnQuit()
        {
            Unwatch();
            Refresh();
        }

        void Unwatch()
        {
            if (watched != null) watched.Changed -= Refresh;
            watched = null;
        }

        void Build()
        {
            root = document.rootVisualElement;
            if (root == null) return;

            root.Clear();
            // Only the controls themselves should swallow clicks; the rest of the screen
            // belongs to the board underneath.
            root.pickingMode = PickingMode.Ignore;

            statusLabel = new Label();
            statusLabel.style.position = Position.Absolute;
            statusLabel.style.top = 18f;
            statusLabel.style.left = 0f;
            statusLabel.style.right = 0f;
            statusLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            statusLabel.style.fontSize = 20f;
            statusLabel.style.color = ArcadeTheme.TextBright;
            statusLabel.pickingMode = PickingMode.Ignore;
            root.Add(statusLabel);

            var controls = new VisualElement();
            controls.style.position = Position.Absolute;
            controls.style.right = 22f;
            controls.style.bottom = 22f;
            controls.style.alignItems = Align.FlexEnd;
            root.Add(controls);

            controls.Add(ArcadeTheme.MakeButton("Play Again", () =>
            {
                if (watched != null) watched.Restart();
                Refresh();
            }, 140f));

            controls.Add(ArcadeTheme.MakeButton("Quit to Arcade", () =>
            {
                if (shell != null) shell.QuitToMenu();
            }, 140f));

            resultPanel = ArcadeTheme.Panel(18f);
            resultPanel.style.position = Position.Absolute;
            resultPanel.style.left = 0f;
            resultPanel.style.right = 0f;
            resultPanel.style.top = 62f;
            resultPanel.style.alignSelf = Align.Center;
            resultPanel.style.maxWidth = 260f;
            resultPanel.style.marginLeft = StyleKeyword.Auto;
            resultPanel.style.marginRight = StyleKeyword.Auto;
            resultPanel.style.alignItems = Align.Center;
            resultPanel.style.display = DisplayStyle.None;
            root.Add(resultPanel);

            resultLabel = new Label();
            resultLabel.style.color = ArcadeTheme.TextBright;
            resultLabel.style.fontSize = 22f;
            resultLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            resultPanel.Add(resultLabel);
        }

        public void Refresh()
        {
            if (root == null) return;

            // Chess draws its own, and the arcade grid needs the screen to itself.
            bool show = watched != null
                && watched.UsesSharedHud
                && shell != null
                && !shell.IsShowing;

            root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            statusLabel.text = watched.StatusText;

            bool finished = watched.IsFinished;
            resultPanel.style.display = finished ? DisplayStyle.Flex : DisplayStyle.None;
            if (finished) resultLabel.text = watched.StatusText;
        }
    }
}
