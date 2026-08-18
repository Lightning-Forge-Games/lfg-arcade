using LightningForge.Chess.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Draws file letters and rank numbers around the board.
    ///
    /// The labels are UI elements projected from world space rather than 3D text. That
    /// avoids depending on TextMeshPro's imported font assets, which a clean clone of the
    /// project would not have, and it keeps the labels upright and readable from any
    /// camera angle, including after the view flips for Black.
    ///
    /// All four edges are labelled so the board reads correctly from either side.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class BoardCoordinateLabels : MonoBehaviour
    {
        [SerializeField] ChessBoardView boardView;
        [SerializeField] Camera targetCamera;

        [Tooltip("How far outside the playing surface the labels sit, in squares.")]
        [SerializeField] float margin = 0.72f;

        [SerializeField] float fontSize = 13f;
        [SerializeField] Color labelColor = new Color(0.78f, 0.74f, 0.66f, 1f);

        UIDocument document;
        readonly Label[] fileLabelsSouth = new Label[8];
        readonly Label[] fileLabelsNorth = new Label[8];
        readonly Label[] rankLabelsWest = new Label[8];
        readonly Label[] rankLabelsEast = new Label[8];

        void Awake()
        {
            document = GetComponent<UIDocument>();
            if (boardView == null) boardView = FindFirstObjectByType<ChessBoardView>();
            if (targetCamera == null) targetCamera = Camera.main;
        }

        void OnEnable()
        {
            if (document == null) document = GetComponent<UIDocument>();
            Build();
        }

        void Build()
        {
            VisualElement root = document.rootVisualElement;
            if (root == null) return;

            root.Clear();
            root.pickingMode = PickingMode.Ignore;

            for (int i = 0; i < 8; i++)
            {
                string file = ((char)('a' + i)).ToString();
                string rank = (i + 1).ToString();
                fileLabelsSouth[i] = MakeLabel(root, file);
                fileLabelsNorth[i] = MakeLabel(root, file);
                rankLabelsWest[i] = MakeLabel(root, rank);
                rankLabelsEast[i] = MakeLabel(root, rank);
            }
        }

        Label MakeLabel(VisualElement root, string text)
        {
            var label = new Label(text);
            label.style.position = Position.Absolute;
            label.style.color = labelColor;
            label.style.fontSize = fontSize;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.pickingMode = PickingMode.Ignore;
            root.Add(label);
            return label;
        }

        void LateUpdate()
        {
            if (boardView == null || document == null) return;
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            IPanel panel = document.rootVisualElement != null ? document.rootVisualElement.panel : null;
            if (panel == null) return;

            float offset = boardView.SquareSize * (3.5f + margin);

            // Only label the two edges nearest the camera. Labelling all four puts letters
            // among the far pieces, which reads as clutter rather than as a board edge.
            Vector3 cameraLocal = boardView.transform.InverseTransformPoint(targetCamera.transform.position);
            bool fromWhiteSide = cameraLocal.z < 0f;

            for (int i = 0; i < 8; i++)
            {
                float along = (i - 3.5f) * boardView.SquareSize;

                Place(panel, fileLabelsSouth[i], new Vector3(along, 0f, -offset), fromWhiteSide);
                Place(panel, fileLabelsNorth[i], new Vector3(along, 0f, offset), !fromWhiteSide);
                Place(panel, rankLabelsWest[i], new Vector3(-offset, 0f, along), fromWhiteSide);
                Place(panel, rankLabelsEast[i], new Vector3(offset, 0f, along), !fromWhiteSide);
            }
        }

        void Place(IPanel panel, Label label, Vector3 boardLocal, bool visible)
        {
            if (label == null) return;

            if (!visible)
            {
                label.style.display = DisplayStyle.None;
                return;
            }

            Vector3 world = boardView.transform.TransformPoint(boardLocal);

            // Behind the camera: hide rather than draw a mirrored ghost.
            Vector3 viewport = targetCamera.WorldToViewportPoint(world);
            if (viewport.z <= 0f)
            {
                label.style.display = DisplayStyle.None;
                return;
            }
            label.style.display = DisplayStyle.Flex;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, targetCamera);

            // Centre the glyph on the point; the element is measured after its first layout.
            float halfWidth = label.resolvedStyle.width * 0.5f;
            float halfHeight = label.resolvedStyle.height * 0.5f;
            label.style.left = panelPoint.x - (float.IsNaN(halfWidth) ? 4f : halfWidth);
            label.style.top = panelPoint.y - (float.IsNaN(halfHeight) ? 8f : halfHeight);
        }
    }
}
