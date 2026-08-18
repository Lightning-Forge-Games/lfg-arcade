using UnityEngine;

namespace LightningForge.Arcade.Game.Yahtzee
{
    /// <summary>
    /// One die: a cube with pips on all six faces, turned to show the value rolled.
    ///
    /// Pips are placed once and the die is rotated, rather than the faces being repainted,
    /// because that is what a die is. It also means the value showing and the value stored
    /// cannot drift apart: the rotation is derived from the number every time it is set.
    /// </summary>
    public class YahtzeeDie : MonoBehaviour
    {
        // Opposite faces sum to seven, as on a real die.
        const int Up = 1, Down = 6, Forward = 2, Back = 5, Right = 3, Left = 4;

        public int Value { get; private set; } = 1;
        public bool Held { get; private set; }

        Transform pipRoot;
        MeshRenderer body;

        public static YahtzeeDie Create(Transform parent, Color faceColour, Color pipColour)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Die";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.8f;

            var die = go.AddComponent<YahtzeeDie>();
            die.body = go.GetComponent<MeshRenderer>();
            die.body.sharedMaterial = ArcadeMaterials.Get(faceColour, 0.45f);

            die.BuildPips(pipColour);
            die.SetValue(1);
            return die;
        }

        void BuildPips(Color pipColour)
        {
            var root = new GameObject("Pips");
            root.transform.SetParent(transform, false);
            pipRoot = root.transform;

            AddFace(Vector3.up, Vector3.right, Vector3.forward, Up, pipColour);
            AddFace(Vector3.down, Vector3.right, Vector3.back, Down, pipColour);
            AddFace(Vector3.forward, Vector3.right, Vector3.up, Forward, pipColour);
            AddFace(Vector3.back, Vector3.left, Vector3.up, Back, pipColour);
            AddFace(Vector3.right, Vector3.back, Vector3.up, Right, pipColour);
            AddFace(Vector3.left, Vector3.forward, Vector3.up, Left, pipColour);
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

        void AddFace(Vector3 normal, Vector3 right, Vector3 up, int value, Color pipColour)
        {
            const float spread = 0.26f;
            Material material = ArcadeMaterials.Get(pipColour, 0.2f);

            foreach (Vector2 offset in LayoutFor(value))
            {
                var pip = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                pip.name = "Pip" + value;
                pip.transform.SetParent(pipRoot, false);
                // Just proud of the face, so it catches the light like a real pip.
                pip.transform.localPosition =
                    normal * 0.5f + right * (offset.x * spread) + up * (offset.y * spread);
                pip.transform.localScale = Vector3.one * 0.17f;
                Destroy(pip.GetComponent<Collider>());
                pip.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        /// <summary>Turns the die so the given number faces up.</summary>
        public void SetValue(int value)
        {
            Value = Mathf.Clamp(value, 1, 6);
            transform.localRotation = RotationFor(Value);
        }

        static Quaternion RotationFor(int value)
        {
            switch (value)
            {
                case 2: return Quaternion.Euler(-90f, 0f, 0f);
                case 3: return Quaternion.Euler(0f, 0f, 90f);
                case 4: return Quaternion.Euler(0f, 0f, -90f);
                case 5: return Quaternion.Euler(90f, 0f, 0f);
                case 6: return Quaternion.Euler(180f, 0f, 0f);
                default: return Quaternion.identity;
            }
        }

        /// <summary>
        /// Held dice are lifted and lit, because a player has to be able to tell at a glance
        /// which of the five are coming back for the next roll.
        /// </summary>
        public void SetHeld(bool held, Color faceColour, Color heldColour)
        {
            Held = held;
            body.sharedMaterial = held
                ? ArcadeMaterials.Emissive(heldColour, 0.45f)
                : ArcadeMaterials.Get(faceColour, 0.45f);

            Vector3 p = transform.localPosition;
            p.y = held ? 0.62f : 0.4f;
            transform.localPosition = p;
        }
    }
}
