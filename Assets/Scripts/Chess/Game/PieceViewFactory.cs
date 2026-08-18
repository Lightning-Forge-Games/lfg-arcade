using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>Authored art for one piece. Leave the prefab null to fall back to a primitive stand-in.</summary>
    [System.Serializable]
    public struct PiecePrefabEntry
    {
        public PieceType Type;
        public GameObject WhitePrefab;
        public GameObject BlackPrefab;
    }

    /// <summary>
    /// Produces the visual for a piece. Real models are used when supplied; otherwise a
    /// proportioned primitive stand-in is generated so the game reads correctly on screen
    /// long before the art lands.
    /// </summary>
    public class PieceViewFactory : MonoBehaviour
    {
        [SerializeField] PiecePrefabEntry[] prefabs = new PiecePrefabEntry[0];

        [Header("Stand-in appearance")]
        [SerializeField] Material whiteMaterial;
        [SerializeField] Material blackMaterial;

        [Tooltip("World height of a pawn. Other pieces scale relative to this.")]
        [SerializeField] float pawnHeight = 0.5f;

        [SerializeField] float pieceRadius = 0.32f;

        public GameObject Create(PieceType type, PieceColor color, Transform parent)
        {
            GameObject prefab = FindPrefab(type, color);
            GameObject instance = prefab != null
                ? Instantiate(prefab, parent)
                : CreateStandIn(type, color, parent);

            instance.name = $"{color}_{type}";
            return instance;
        }

        GameObject FindPrefab(PieceType type, PieceColor color)
        {
            foreach (PiecePrefabEntry entry in prefabs)
            {
                if (entry.Type != type) continue;
                return color == PieceColor.White ? entry.WhitePrefab : entry.BlackPrefab;
            }
            return null;
        }

        /// <summary>
        /// Builds a stand-in from primitives. Silhouette matters more than detail here:
        /// the heights and crowns are what let you tell pieces apart at a glance.
        /// </summary>
        GameObject CreateStandIn(PieceType type, PieceColor color, Transform parent)
        {
            var root = new GameObject("StandIn");
            root.transform.SetParent(parent, false);

            float height = HeightFor(type);
            Material material = color == PieceColor.White ? whiteMaterial : blackMaterial;

            // Body: a tapered column standing on the square.
            var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, height * 0.5f, 0f);
            body.transform.localScale = new Vector3(pieceRadius * 2f, height * 0.5f, pieceRadius * 2f);
            ApplyMaterial(body, material);

            // Crown: distinguishes the piece at a glance.
            PrimitiveType crownShape = CrownFor(type);
            if (crownShape != PrimitiveType.Cylinder || type == PieceType.King)
            {
                var crown = GameObject.CreatePrimitive(crownShape);
                crown.transform.SetParent(root.transform, false);
                crown.transform.localPosition = new Vector3(0f, height + pieceRadius * 0.4f, 0f);
                float crownScale = pieceRadius * (type == PieceType.Pawn ? 1.1f : 1.5f);
                crown.transform.localScale = Vector3.one * crownScale;
                ApplyMaterial(crown, material);

                // Only the body needs a collider for picking.
                Collider crownCollider = crown.GetComponent<Collider>();
                if (crownCollider != null) DestroyComponent(crownCollider);
            }

            return root;
        }

        float HeightFor(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return pawnHeight;
                case PieceType.Knight: return pawnHeight * 1.35f;
                case PieceType.Bishop: return pawnHeight * 1.5f;
                case PieceType.Rook: return pawnHeight * 1.25f;
                case PieceType.Queen: return pawnHeight * 1.8f;
                case PieceType.King: return pawnHeight * 2f;
                default: return pawnHeight;
            }
        }

        PrimitiveType CrownFor(PieceType type)
        {
            switch (type)
            {
                case PieceType.Pawn: return PrimitiveType.Sphere;
                case PieceType.Knight: return PrimitiveType.Capsule;
                case PieceType.Bishop: return PrimitiveType.Sphere;
                case PieceType.Rook: return PrimitiveType.Cube;
                case PieceType.Queen: return PrimitiveType.Sphere;
                case PieceType.King: return PrimitiveType.Cube;
                default: return PrimitiveType.Cylinder;
            }
        }

        void ApplyMaterial(GameObject go, Material material)
        {
            if (material == null) return;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        void DestroyComponent(Object component)
        {
            if (Application.isPlaying) Destroy(component);
            else DestroyImmediate(component);
        }
    }
}
