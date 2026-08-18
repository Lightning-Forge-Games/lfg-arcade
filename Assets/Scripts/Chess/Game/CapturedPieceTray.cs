using System.Collections.Generic;
using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Stands captured pieces beside the board, each colour on its own side, so you can
    /// see the material balance without counting what is missing from the position.
    ///
    /// Rebuilt from the controller's capture list rather than driven by events, so it is
    /// always consistent after a new game, an undo of style, or a view flip, and cannot
    /// drift out of step with the board.
    /// </summary>
    public class CapturedPieceTray : MonoBehaviour
    {
        [SerializeField] ChessGameController controller;
        [SerializeField] ChessBoardView boardView;
        [SerializeField] PieceViewFactory factory;

        [Tooltip("Size of tray pieces relative to those on the board.")]
        [SerializeField] float scale = 1f;

        [Tooltip("Gap from the outside of the frame to the first column.")]
        [SerializeField] float gap = 0.8f;

        [SerializeField] float rowSpacing = 0.9f;
        [SerializeField] float columnSpacing = 0.9f;
        [SerializeField] int piecesPerColumn = 8;

        readonly List<GameObject> spawned = new List<GameObject>();
        int builtCount = -1;
        int builtGameId = -1;
        PieceStyle builtStyle = PieceStyle.Sculpted;

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<ChessGameController>();
            if (boardView == null) boardView = FindFirstObjectByType<ChessBoardView>();
            if (factory == null) factory = FindFirstObjectByType<PieceViewFactory>();
        }

        void LateUpdate()
        {
            if (controller == null || controller.Board == null) return;

            PieceStyle style = factory != null ? factory.Style : PieceStyle.Sculpted;
            if (controller.Captured.Count == builtCount
                && controller.GameId == builtGameId
                && style == builtStyle)
            {
                return;
            }

            builtCount = controller.Captured.Count;
            builtGameId = controller.GameId;
            builtStyle = style;
            Rebuild();
        }

        void Rebuild()
        {
            for (int i = 0; i < spawned.Count; i++)
            {
                if (spawned[i] == null) continue;
                if (Application.isPlaying) Destroy(spawned[i]);
                else DestroyImmediate(spawned[i]);
            }
            spawned.Clear();

            if (factory == null || boardView == null) return;

            int whiteTaken = 0;
            int blackTaken = 0;

            foreach (Piece piece in controller.Captured)
            {
                // A captured white piece is a trophy for Black, so it stands on Black's side.
                int index = piece.Color == PieceColor.White ? whiteTaken++ : blackTaken++;
                Place(piece, index);
            }
        }

        void Place(Piece piece, int index)
        {
            GameObject view = factory.Create(piece.Type, piece.Color, transform);
            if (view == null) return;

            spawned.Add(view);
            view.transform.localScale = Vector3.one * scale;

            int column = index / piecesPerColumn;
            int row = index % piecesPerColumn;

            // White's losses sit off the queenside edge, Black's off the kingside edge.
            float sign = piece.Color == PieceColor.White ? -1f : 1f;
            float x = sign * (boardView.FrameCenterDistance + boardView.FrameWidth * 0.5f
                              + gap + column * columnSpacing);
            float z = (row - (piecesPerColumn - 1) * 0.5f) * rowSpacing;

            // Beside the board there is no playing surface, so they stand on the ground
            // the board itself rests on. Board height would leave them hanging in the air.
            Vector3 local = new Vector3(x, boardView.GroundHeight, z);
            view.transform.position = boardView.transform.TransformPoint(local);
        }
    }
}
