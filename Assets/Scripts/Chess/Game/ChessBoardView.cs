using System.Collections.Generic;
using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Owns the physical board: square geometry, the mapping between square indices and
    /// world space, and square highlighting. Built procedurally so the game is playable
    /// before any authored art exists; swap <see cref="squarePrefab"/> in once it does.
    /// </summary>
    public class ChessBoardView : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Edge length of a single square in world units.")]
        [SerializeField] float squareSize = 1f;

        [Tooltip("Thickness of the procedurally generated squares.")]
        [SerializeField] float squareThickness = 0.2f;

        [Header("Appearance")]
        [SerializeField] Material lightSquareMaterial;
        [SerializeField] Material darkSquareMaterial;
        [SerializeField] Material highlightMaterial;
        [SerializeField] Material selectionMaterial;

        [Tooltip("Optional authored square. When unset, squares are built from primitives.")]
        [SerializeField] GameObject squarePrefab;

        readonly Transform[] squareTransforms = new Transform[Square.Count];
        readonly Renderer[] squareRenderers = new Renderer[Square.Count];
        readonly Material[] baseMaterials = new Material[Square.Count];
        readonly HashSet<int> highlighted = new HashSet<int>();

        int selectedSquare = Square.None;

        public float SquareSize => squareSize;
        public int SelectedSquare => selectedSquare;

        void Awake()
        {
            if (squareTransforms[0] == null) Build();
        }

        /// <summary>Creates the 64 squares. Safe to call again; existing squares are replaced.</summary>
        public void Build()
        {
            for (int i = 0; i < Square.Count; i++)
            {
                if (squareTransforms[i] != null) DestroySquare(squareTransforms[i].gameObject);
            }

            for (int square = 0; square < Square.Count; square++)
            {
                GameObject go = squarePrefab != null
                    ? Instantiate(squarePrefab, transform)
                    : CreatePrimitiveSquare();

                go.name = $"Square_{Square.ToAlgebraic(square)}";
                go.transform.SetParent(transform, false);
                go.transform.localPosition = SquareToLocal(square);

                if (squarePrefab == null)
                {
                    go.transform.localScale = new Vector3(squareSize, squareThickness, squareSize);
                }

                var renderer = go.GetComponentInChildren<Renderer>();
                Material material = Square.IsLight(square) ? lightSquareMaterial : darkSquareMaterial;
                if (renderer != null && material != null) renderer.sharedMaterial = material;

                squareTransforms[square] = go.transform;
                squareRenderers[square] = renderer;
                baseMaterials[square] = renderer != null ? renderer.sharedMaterial : null;
            }
        }

        GameObject CreatePrimitiveSquare()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            // The collider is what pointer picking raycasts against, so keep it.
            return go;
        }

        void DestroySquare(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        /// <summary>Board-local centre of a square, sitting on the board's top surface.</summary>
        public Vector3 SquareToLocal(int square)
        {
            // Centre the board on the transform: files and ranks run -3.5..3.5 in square units.
            float x = (Square.FileOf(square) - 3.5f) * squareSize;
            float z = (Square.RankOf(square) - 3.5f) * squareSize;
            return new Vector3(x, 0f, z);
        }

        public Vector3 SquareToWorld(int square) => transform.TransformPoint(SquareToLocal(square));

        /// <summary>Top surface of a square, where pieces stand.</summary>
        public Vector3 SquareSurface(int square)
        {
            Vector3 local = SquareToLocal(square);
            local.y += squareThickness * 0.5f;
            return transform.TransformPoint(local);
        }

        /// <summary>Square under a world point, or <see cref="Square.None"/> if off board.</summary>
        public int WorldToSquare(Vector3 world)
        {
            Vector3 local = transform.InverseTransformPoint(world);
            int file = Mathf.FloorToInt(local.x / squareSize + 4f);
            int rank = Mathf.FloorToInt(local.z / squareSize + 4f);
            if (file < 0 || file > 7 || rank < 0 || rank > 7) return Square.None;
            return Square.Of(file, rank);
        }

        public Transform GetSquareTransform(int square) =>
            Square.IsValid(square) ? squareTransforms[square] : null;

        public void SetSelected(int square)
        {
            if (selectedSquare == square) return;

            int previous = selectedSquare;
            selectedSquare = square;

            if (Square.IsValid(previous)) RefreshSquare(previous);
            if (Square.IsValid(selectedSquare)) RefreshSquare(selectedSquare);
        }

        public void SetHighlights(IEnumerable<int> squares)
        {
            var previous = new List<int>(highlighted);
            highlighted.Clear();
            if (squares != null)
            {
                foreach (int square in squares)
                {
                    if (Square.IsValid(square)) highlighted.Add(square);
                }
            }

            foreach (int square in previous) RefreshSquare(square);
            foreach (int square in highlighted) RefreshSquare(square);
        }

        public void ClearHighlights() => SetHighlights(null);

        void RefreshSquare(int square)
        {
            Renderer renderer = squareRenderers[square];
            if (renderer == null) return;

            if (square == selectedSquare && selectionMaterial != null)
            {
                renderer.sharedMaterial = selectionMaterial;
            }
            else if (highlighted.Contains(square) && highlightMaterial != null)
            {
                renderer.sharedMaterial = highlightMaterial;
            }
            else
            {
                renderer.sharedMaterial = baseMaterials[square];
            }
        }
    }
}
