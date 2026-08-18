using LightningForge.Chess.Core;
using TMPro;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Prints file letters and rank numbers onto the board frame as 3D text.
    ///
    /// All four edges are labelled, as on a real board, with each edge oriented to read
    /// from the player nearest it. That way both players always have coordinates and
    /// nothing has to be re-oriented when the view flips.
    ///
    /// Glyphs are centred on the frame band, taking the distance from the board view so
    /// they track the frame if its width ever changes.
    /// </summary>
    public class BoardCoordinates : MonoBehaviour
    {
        [SerializeField] ChessBoardView boardView;

        [SerializeField] float fontSize = 2.2f;
        [SerializeField] Color labelColor = new Color(0.74f, 0.70f, 0.62f, 1f);

        [Tooltip("Height of the glyphs. Must clear the frame rail, which stands proud of the squares.")]
        [SerializeField] float lift = 0.26f;

        void Awake()
        {
            if (boardView == null) boardView = FindFirstObjectByType<ChessBoardView>();
        }

        void Start()
        {
            Build();
        }

        void Build()
        {
            if (boardView == null) return;

            // Centre of the frame band, matching where the rails themselves are placed.
            float offset = boardView.FrameCenterDistance;

            // Text lies flat: local +Z points down into the board so the readable face
            // looks up, and local +Y (the top of the letters) points away from the reader.
            Quaternion facingWhite = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            Quaternion facingBlack = Quaternion.LookRotation(Vector3.down, Vector3.back);

            for (int i = 0; i < 8; i++)
            {
                float along = (i - 3.5f) * boardView.SquareSize;
                string file = ((char)('A' + i)).ToString();
                string rank = (i + 1).ToString();

                // Files on the near and far edges.
                MakeLabel(file, new Vector3(along, lift, -offset), facingWhite);
                MakeLabel(file, new Vector3(along, lift, offset), facingBlack);

                // Ranks on the left and right edges.
                MakeLabel(rank, new Vector3(-offset, lift, along), facingWhite);
                MakeLabel(rank, new Vector3(offset, lift, along), facingBlack);
            }
        }

        void MakeLabel(string text, Vector3 localPosition, Quaternion localRotation)
        {
            var go = new GameObject("Coord_" + text);
            go.transform.SetParent(boardView.transform, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = localRotation;

            var label = go.AddComponent<TextMeshPro>();
            label.text = text;
            label.fontSize = fontSize;
            label.color = labelColor;
            label.alignment = TextAlignmentOptions.Center;
            label.enableWordWrapping = false;

            // Rect matches the frame band so the glyph centres within it both ways.
            label.rectTransform.sizeDelta = new Vector2(boardView.SquareSize, boardView.FrameWidth);
        }
    }
}
