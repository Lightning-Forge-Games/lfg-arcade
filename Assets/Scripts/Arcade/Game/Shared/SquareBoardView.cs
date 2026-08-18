using System.Collections.Generic;
using LightningForge.Arcade.Core;
using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// Owns a physical 8x8 board: square geometry, the mapping between square indices and
    /// world space, and square highlighting. Built procedurally so the game is playable
    /// before any authored art exists; swap <see cref="squarePrefab"/> in once it does.
    /// </summary>
    public class SquareBoardView : MonoBehaviour
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

        [Header("Frame")]
        [SerializeField] bool buildFrame = true;
        [SerializeField] Material frameMaterial;

        [Tooltip("Width of the border running around the playing surface.")]
        [SerializeField] float frameWidth = 0.55f;

        [Tooltip("Thickness of the slab the squares sit on.")]
        [SerializeField] float plinthThickness = 0.22f;

        readonly Transform[] squareTransforms = new Transform[Square.Count];
        readonly Renderer[] squareRenderers = new Renderer[Square.Count];
        readonly Material[] baseMaterials = new Material[Square.Count];
        readonly HashSet<int> highlighted = new HashSet<int>();

        int selectedSquare = Square.None;
        Transform frameTransform;

        public float SquareSize => squareSize;
        public int SelectedSquare => selectedSquare;

        public float FrameWidth => frameWidth;

        /// <summary>Local height of the surface pieces stand on.</summary>
        public float SquareSurfaceHeight => squareThickness * 0.5f;

        /// <summary>
        /// Local height of the underside of the plinth, which is the ground the board
        /// rests on. Anything placed beside the board should stand here, not at board
        /// height, or it floats with nothing under it.
        /// </summary>
        public float GroundHeight => -(squareThickness * 0.5f + plinthThickness);

        /// <summary>
        /// Distance from the board centre to the middle of the frame band. Mirrors how the
        /// rails are positioned in <see cref="BuildFrame"/>, so anything placed here sits
        /// centred on the border rather than guessing an offset.
        /// </summary>
        public float FrameCenterDistance => (8f * squareSize + frameWidth) * 0.5f;

        void Awake()
        {
            if (squareTransforms[0] == null) Build();
        }

        /// <summary>
        /// Supplies the board's materials in code and rebuilds.
        ///
        /// Chess assigns these through the inspector because it had authored materials
        /// before the arcade existed. Games added since build their look procedurally, and
        /// without this they would each need a set of material assets created and wired by
        /// hand before anything could appear on screen.
        /// </summary>
        public void Configure(Material light, Material dark, Material highlight,
            Material selection, Material frame)
        {
            lightSquareMaterial = light;
            darkSquareMaterial = dark;
            highlightMaterial = highlight;
            selectionMaterial = selection;
            frameMaterial = frame;
            Build();
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
                    // A softened edge on every square is what stops a procedural board
                    // reading as sixty four flat tiles.
                    go.transform.localScale = Vector3.one;
                    var size = new Vector3(squareSize, squareThickness, squareSize);
                    ArcadeMeshes.ApplyMesh(go, ArcadeMeshes.RoundedBox(size, squareThickness * 0.11f, 4));
                    go.GetComponent<BoxCollider>().size = size;
                }

                var renderer = go.GetComponentInChildren<Renderer>();
                Material material = Square.IsLight(square) ? lightSquareMaterial : darkSquareMaterial;
                if (renderer != null && material != null) renderer.sharedMaterial = material;

                squareTransforms[square] = go.transform;
                squareRenderers[square] = renderer;
                baseMaterials[square] = renderer != null ? renderer.sharedMaterial : null;
            }

            if (buildFrame) BuildFrame();
        }

        /// <summary>
        /// Surrounds the playing surface with a border and sets it on a plinth, so the board
        /// reads as a single object rather than 64 tiles floating in space.
        /// </summary>
        void BuildFrame()
        {
            if (frameTransform != null) DestroySquare(frameTransform.gameObject);

            var frame = new GameObject("Frame");
            frame.transform.SetParent(transform, false);
            frameTransform = frame.transform;

            float board = 8f * squareSize;
            float outer = board + frameWidth * 2f;
            float railY = squareThickness * 0.5f;
            float railHeight = squareThickness * 1.35f;
            float offset = (board + frameWidth) * 0.5f;

            // Four rails. The long pair spans the full outer width so the corners meet cleanly.
            AddFramePart(frame.transform, "Rail_North",
                new Vector3(0f, railY, offset), new Vector3(outer, railHeight, frameWidth));
            AddFramePart(frame.transform, "Rail_South",
                new Vector3(0f, railY, -offset), new Vector3(outer, railHeight, frameWidth));
            AddFramePart(frame.transform, "Rail_East",
                new Vector3(offset, railY, 0f), new Vector3(frameWidth, railHeight, board));
            AddFramePart(frame.transform, "Rail_West",
                new Vector3(-offset, railY, 0f), new Vector3(frameWidth, railHeight, board));

            // Plinth underneath everything.
            AddFramePart(frame.transform, "Plinth",
                new Vector3(0f, -squareThickness * 0.5f - plinthThickness * 0.5f, 0f),
                new Vector3(outer, plinthThickness, outer));
        }

        void AddFramePart(Transform parent, string name, Vector3 localPosition, Vector3 scale)
        {
            ArcadeMeshes.Box(parent, name, localPosition, scale,
                Mathf.Min(0.06f, Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)) * 0.3f),
                frameMaterial, false);
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
