using System.Collections.Generic;
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
        VisualElement promotionPanel;
        ScrollView moveList;
        int renderedMoveCount = -1;

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
                controller.PromotionRequested += OnPromotionRequested;
            }
            Refresh();
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.StatusChanged -= OnStatusChanged;
                controller.MoveMade -= OnMoveMade;
                controller.PromotionRequested -= OnPromotionRequested;
            }
        }

        void OnPromotionRequested(int from, int to) => ShowPromotionPicker(true);

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

            BuildPromotionPicker(root);
            BuildMoveList(root);
        }

        /// <summary>Scrolling list of the game so far, paired up as White/Black per move.</summary>
        void BuildMoveList(VisualElement root)
        {
            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = 74f;
            panel.style.right = 22f;
            panel.style.width = 150f;
            panel.style.maxHeight = 320f;
            panel.style.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.72f);
            panel.style.paddingLeft = 10f;
            panel.style.paddingRight = 6f;
            panel.style.paddingTop = 8f;
            panel.style.paddingBottom = 8f;
            SetBorderRadius(panel, 6f);
            root.Add(panel);

            var heading = new Label("Moves");
            heading.style.color = new Color(0.72f, 0.68f, 0.60f);
            heading.style.fontSize = 11f;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.marginBottom = 5f;
            panel.Add(heading);

            moveList = new ScrollView(ScrollViewMode.Vertical);
            moveList.style.flexGrow = 1f;
            moveList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            panel.Add(moveList);
        }

        void RefreshMoveList()
        {
            if (moveList == null || controller == null) return;

            IReadOnlyList<string> history = controller.MoveHistory;
            if (history.Count == renderedMoveCount) return;
            renderedMoveCount = history.Count;

            moveList.Clear();

            // Two plies per numbered move, as a scoresheet reads.
            for (int i = 0; i < history.Count; i += 2)
            {
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1f;

                var number = new Label(((i / 2) + 1) + ".");
                number.style.color = new Color(0.55f, 0.52f, 0.47f);
                number.style.fontSize = 12f;
                number.style.width = 24f;
                row.Add(number);

                var white = new Label(history[i]);
                white.style.color = new Color(0.93f, 0.90f, 0.84f);
                white.style.fontSize = 12f;
                white.style.width = 52f;
                row.Add(white);

                if (i + 1 < history.Count)
                {
                    var black = new Label(history[i + 1]);
                    black.style.color = new Color(0.78f, 0.76f, 0.72f);
                    black.style.fontSize = 12f;
                    black.style.width = 52f;
                    row.Add(black);
                }

                moveList.Add(row);
            }

            // Keep the latest move in view.
            moveList.schedule.Execute(() => moveList.scrollOffset =
                new Vector2(0f, moveList.contentContainer.layout.height)).ExecuteLater(16);
        }

        void BuildPromotionPicker(VisualElement root)
        {
            promotionPanel = new VisualElement();
            promotionPanel.style.position = Position.Absolute;
            promotionPanel.style.left = 0f;
            promotionPanel.style.right = 0f;
            promotionPanel.style.bottom = 90f;
            promotionPanel.style.alignItems = Align.Center;
            promotionPanel.style.display = DisplayStyle.None;
            root.Add(promotionPanel);

            var box = new VisualElement();
            box.style.backgroundColor = new Color(0.05f, 0.05f, 0.06f, 0.88f);
            box.style.paddingLeft = 16f;
            box.style.paddingRight = 16f;
            box.style.paddingTop = 12f;
            box.style.paddingBottom = 12f;
            box.style.alignItems = Align.Center;
            SetBorderRadius(box, 7f);
            SetBorderWidth(box, 1f);
            SetBorderColor(box, new Color(0.42f, 0.33f, 0.22f, 1f));
            promotionPanel.Add(box);

            var prompt = new Label("Promote to");
            prompt.style.color = new Color(0.93f, 0.90f, 0.84f);
            prompt.style.fontSize = 15f;
            prompt.style.marginBottom = 8f;
            box.Add(prompt);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            box.Add(row);

            AddPromotionButton(row, "Queen", PieceType.Queen);
            AddPromotionButton(row, "Rook", PieceType.Rook);
            AddPromotionButton(row, "Bishop", PieceType.Bishop);
            AddPromotionButton(row, "Knight", PieceType.Knight);
        }

        void AddPromotionButton(VisualElement parent, string label, PieceType type)
        {
            var button = new Button(() =>
            {
                if (controller != null) controller.CompletePromotion(type);
                ShowPromotionPicker(false);
                Refresh();
            });
            button.text = label;
            button.style.marginLeft = 4f;
            button.style.marginRight = 4f;
            button.style.paddingLeft = 14f;
            button.style.paddingRight = 14f;
            button.style.paddingTop = 8f;
            button.style.paddingBottom = 8f;
            button.style.fontSize = 13f;
            button.style.color = new Color(0.93f, 0.90f, 0.84f);
            button.style.backgroundColor = new Color(0.14f, 0.13f, 0.12f, 1f);
            SetBorderRadius(button, 4f);
            SetBorderWidth(button, 1f);
            SetBorderColor(button, new Color(0.35f, 0.28f, 0.19f, 1f));
            parent.Add(button);
        }

        void ShowPromotionPicker(bool visible)
        {
            if (promotionPanel == null) return;
            promotionPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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

            RefreshMoveList();
        }
    }
}
