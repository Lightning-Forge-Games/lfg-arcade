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
        [SerializeField] TitleScreen titleScreen;
        [SerializeField] BoardCameraRig cameraRig;
        [SerializeField] PieceViewFactory pieceFactory;

        Button viewButton;
        Button pieceButton;

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
            if (titleScreen == null) titleScreen = FindFirstObjectByType<TitleScreen>();
            if (cameraRig == null) cameraRig = FindFirstObjectByType<BoardCameraRig>();
            if (pieceFactory == null) pieceFactory = FindFirstObjectByType<PieceViewFactory>();
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

            // Controls, bottom right, stacked so they never overlap the board.
            var controls = new VisualElement();
            controls.style.position = Position.Absolute;
            controls.style.bottom = 22f;
            controls.style.right = 22f;
            controls.style.alignItems = Align.FlexEnd;
            root.Add(controls);

            viewButton = MakeControl("View: Angled", () =>
            {
                if (cameraRig != null) cameraRig.ToggleStyle();
                RefreshViewButton();
            });
            controls.Add(viewButton);

            pieceButton = MakeControl("Pieces: 3D", () =>
            {
                if (pieceFactory != null && controller != null)
                {
                    pieceFactory.Style = pieceFactory.Style == PieceStyle.Sculpted
                        ? PieceStyle.Token
                        : PieceStyle.Sculpted;
                    controller.RefreshPieceViews();
                }
                RefreshPieceButton();
            });
            controls.Add(pieceButton);

            // No New Game control here: Quit to Menu leads straight back to setting one up,
            // so a second route to the same thing is just clutter over the board.
            controls.Add(MakeControl("Quit to Menu", () =>
            {
                if (titleScreen != null) titleScreen.QuitToMenu();
            }));

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
            panel.style.width = 204f;
            panel.style.maxHeight = 340f;
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

            // The vertical scroller draws over the content, so keep the rows clear of it.
            moveList.contentContainer.style.paddingRight = 18f;
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
                number.style.width = 30f;
                row.Add(number);

                var white = new Label(history[i]);
                white.style.color = new Color(0.93f, 0.90f, 0.84f);
                white.style.fontSize = 12f;
                white.style.width = 63f;
                row.Add(white);

                if (i + 1 < history.Count)
                {
                    var black = new Label(history[i + 1]);
                    black.style.color = new Color(0.78f, 0.76f, 0.72f);
                    black.style.fontSize = 12f;
                    black.style.width = 63f;
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

        Button MakeControl(string text, System.Action onClick)
        {
            var button = new Button(() => onClick());
            button.text = text;
            // Sized for fingers, not just cursors: roughly a 44px touch target.
            button.style.minWidth = 148f;
            button.style.minHeight = 42f;
            button.style.paddingLeft = 18f;
            button.style.paddingRight = 18f;
            button.style.paddingTop = 11f;
            button.style.paddingBottom = 11f;
            button.style.marginTop = 6f;
            button.style.fontSize = 14f;
            button.style.color = new Color(0.93f, 0.90f, 0.84f);
            button.style.backgroundColor = new Color(0.12f, 0.11f, 0.11f, 0.88f);
            SetBorderColor(button, new Color(0.42f, 0.33f, 0.22f, 1f));
            SetBorderWidth(button, 1f);
            SetBorderRadius(button, 5f);
            return button;
        }

        void RefreshViewButton()
        {
            if (viewButton == null) return;
            BoardViewStyle current = cameraRig != null ? cameraRig.Style : BoardViewStyle.Angled;
            viewButton.text = current == BoardViewStyle.Angled ? "View: Angled" : "View: Overhead";
        }

        void RefreshPieceButton()
        {
            if (pieceButton == null) return;
            PieceStyle current = pieceFactory != null ? pieceFactory.Style : PieceStyle.Sculpted;
            pieceButton.text = current == PieceStyle.Sculpted ? "Pieces: 3D" : "Pieces: Tokens";
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

        /// <summary>Hides the whole in-game HUD, used while the title screen is up.</summary>
        public void SetVisible(bool visible)
        {
            if (document == null) document = GetComponent<UIDocument>();
            VisualElement root = document != null ? document.rootVisualElement : null;
            if (root != null) root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
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
            RefreshViewButton();
            RefreshPieceButton();
        }
    }
}
