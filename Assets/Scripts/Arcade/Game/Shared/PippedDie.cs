using UnityEngine;

namespace LightningForge.Arcade.Game
{
    /// <summary>
    /// Builds a six sided die with pips on every face, and reads which face is up.
    ///
    /// Shared because two games want dice for different reasons: Yahtzee throws them and
    /// lets physics decide, backgammon spins them on the spot and settles on a number it
    /// already has. Both want the same object, and neither should own the other's copy of
    /// what a die looks like.
    /// </summary>
    public static class PippedDie
    {
        /// <summary>
        /// The number on each face, by the direction that face points in the die's own
        /// space. Opposite faces sum to seven, as on a real die.
        /// </summary>
        public static readonly Vector3[] FaceNormals =
        {
            Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
        };

        public static readonly int[] FaceValues = { 1, 6, 2, 5, 3, 4 };

        /// <summary>Whichever face of a given rotation points closest to straight up.</summary>
        public static int ValueShowing(Quaternion rotation)
        {
            int best = 1;
            float bestDot = float.NegativeInfinity;
            for (int i = 0; i < FaceNormals.Length; i++)
            {
                float dot = Vector3.Dot(rotation * FaceNormals[i], Vector3.up);
                if (dot > bestDot)
                {
                    bestDot = dot;
                    best = FaceValues[i];
                }
            }
            return best;
        }

        /// <summary>A rotation that puts the given number face up.</summary>
        public static Quaternion RotationShowing(int value)
        {
            switch (value)
            {
                case 2: return Quaternion.Euler(-90f, 0f, 0f);
                case 3: return Quaternion.Euler(0f, 0f, -90f);
                case 4: return Quaternion.Euler(0f, 0f, 90f);
                case 5: return Quaternion.Euler(90f, 0f, 0f);
                case 6: return Quaternion.Euler(180f, 0f, 0f);
                default: return Quaternion.identity;
            }
        }

        /// <summary>Adds pips to all six faces of a cube, as a child of it.</summary>
        public static void BuildPips(Transform die, Color pipColour)
        {
            var root = new GameObject("Pips");
            root.transform.SetParent(die, false);

            for (int i = 0; i < FaceNormals.Length; i++)
            {
                Vector3 normal = FaceNormals[i];
                // Any two axes perpendicular to the face will do to lay the pips out on it.
                Vector3 right = normal == Vector3.up || normal == Vector3.down
                    ? Vector3.right
                    : Vector3.Cross(normal, Vector3.up).normalized;
                Vector3 up = Vector3.Cross(right, normal).normalized;

                AddFace(root.transform, normal, right, up, FaceValues[i], pipColour);
            }
        }

        /// <summary>The pip layout for each number, in face-local units of a quarter width.</summary>
        static Vector2[] LayoutFor(int value)
        {
            switch (value)
            {
                case 1: return new[] { Vector2.zero };
                case 2: return new[] { new Vector2(-1, 1), new Vector2(1, -1) };
                case 3: return new[] { new Vector2(-1, 1), Vector2.zero, new Vector2(1, -1) };
                case 4: return new[]
                {
                    new Vector2(-1, 1), new Vector2(1, 1),
                    new Vector2(-1, -1), new Vector2(1, -1),
                };
                case 5: return new[]
                {
                    new Vector2(-1, 1), new Vector2(1, 1), Vector2.zero,
                    new Vector2(-1, -1), new Vector2(1, -1),
                };
                default: return new[]
                {
                    new Vector2(-1, 1), new Vector2(1, 1),
                    new Vector2(-1, 0), new Vector2(1, 0),
                    new Vector2(-1, -1), new Vector2(1, -1),
                };
            }
        }

        static void AddFace(Transform parent, Vector3 normal, Vector3 right, Vector3 up,
            int value, Color pipColour)
        {
            const float spread = 0.26f;
            Material material = ArcadeMaterials.Get(pipColour, 0.2f);

            foreach (Vector2 offset in LayoutFor(value))
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Pip" + value;
                pip.transform.SetParent(parent, false);
                // Just proud of the face, so it catches the light like a real pip.
                pip.transform.localPosition =
                    normal * 0.5f + right * (offset.x * spread) + up * (offset.y * spread);
                pip.transform.localScale = Vector3.one * 0.17f;
                Object.Destroy(pip.GetComponent<Collider>());
                pip.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }
    }
}
