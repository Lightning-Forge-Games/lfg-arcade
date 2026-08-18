using LightningForge.Chess.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// On-screen status and a new game control, built with UI Toolkit.
    ///
    /// The UI is constructed in code rather than from UXML so the scene needs no authored
    /// asset beyond PanelSettings, and UI Toolkit is used instead of TextMeshPro because it
    /// is built into the engine and needs no imported font essentials.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ChessHud : MonoBehaviour
    {
        [SerializeField] ChessGameController controller;

        UIDocument document;
        Label statusLabel;
        Label turnLabel;

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (controller == null) controller = FindFirstObjectByType<ChessGameController>();
        }

        void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            BuildUi();

            if (controller != null)
            {
                controller.StatusChanged += OnStatusChanged;
                controller.MoveMade += OnMoveMade;
            }
            Refresh();
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.StatusChanged -= OnStatusChanged;
                controller.MoveMade -= OnMoveMade;
            }
        }

        void OnStatusChanged(GameStatus status) => Refresh();
        void OnMoveMade(Move move) => Refresh();

        void BuildUi()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null) return;

            root.Clear();
            root.style.flexGrow = 1f;

            // Status bar across the top.
            var bar = new VisualElement();
            bar.style.position = Position.Absolute;
            bar.style.top = 18f;
            bar.style.left = 0f;
            bar.style.right = 0f;
            bar.style.alignItems = Align.Center;
            root.Add(bar);

            var panel = new VisualElement();
            panel.style.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.72f);
            panel.style.paddingLeft = 22f;
            panel.style.paddingRight = 22f;
            panel.style.paddingTop = 10f;
            panel.style.paddingBottom = 10f;
            SetBorderRadius(panel, 6f);
            panel.style.alignItems = Align.Center;
            bar.Add(panel);

            turnLabel = new Label("White to move");
            turnLabel.style.color = new Color(0.93f, 0.90f, 0.84f);
            turnLabel.style.fontSize = 20f;
            turnLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(turnLabel);

            statusLabel = new Label(string.Empty);
            statusLabel.style.color = new Color(0.95f, 0.72f, 0.30f);
            statusLabel.style.fontSize = 14f;
            statusLabel.style.marginTop = 2f;
            statusLabel.style.display = DisplayStyle.None;
            panel.Add(statusLabel);

            // New game button, bottom right.
            var button = new Button(() => { if (controller != null) controller.NewGame(); Refresh(); });
            button.text = "New Game";
            button.style.position = Position.Absolute;
            button.style.bottom = 22f;
            button.style.right = 22f;
            button.style.paddingLeft = 18f;
            button.style.paddingRight = 18f;
            button.style.paddingTop = 9f;
            button.style.paddingBottom = 9f;
            button.style.fontSize = 14f;
            button.style.color = new Color(0.93f, 0.90f, 0.84f);
            button.style.backgroundColor = new Color(0.12f, 0.11f, 0.11f, 0.88f);
            SetBorderColor(button, new Color(0.42f, 0.33f, 0.22f, 1f));
            SetBorderWidth(button, 1f);
            SetBorderRadius(button, 5f);
            root.Add(button);
        }

        static void SetBorderRadius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        static void SetBorderWidth(VisualElement element, float width)
        {
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
        }

        static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        public void Refresh()
        {
            if (controller == null || controller.Board == null || turnLabel == null) return;

            bool whiteToMove = controller.Board.SideToMove == PieceColor.White;
            GameStatus status = controller.Status;

            string headline;
            string detail = string.Empty;

            switch (status)
            {
                case GameStatus.Checkmate:
                    // The side to move is the one that has been mated.
                    headline = whiteToMove ? "Black wins" : "White wins";
                    detail = "Checkmate";
                    break;
                case GameStatus.Stalemate:
                    headline = "Draw";
                    detail = "Stalemate";
                    break;
                case GameStatus.DrawByFiftyMoveRule:
                    headline = "Draw";
                    detail = "Fifty move rule";
                    break;
                case GameStatus.DrawByInsufficientMaterial:
                    headline = "Draw";
                    detail = "Insufficient material";
                    break;
                case GameStatus.Check:
                    headline = whiteToMove ? "White to move" : "Black to move";
                    detail = "Check";
                    break;
                default:
                    headline = whiteToMove ? "White to move" : "Black to move";
                    break;
            }

            turnLabel.text = headline;
            statusLabel.text = detail;
            statusLabel.style.display = string.IsNullOrEmpty(detail)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }
    }
}
