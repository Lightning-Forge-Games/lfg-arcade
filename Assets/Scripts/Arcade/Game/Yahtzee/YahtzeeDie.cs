using UnityEngine;

namespace LightningForge.Arcade.Game.Yahtzee
{
    /// <summary>
    /// One die: a cube with pips on all six faces, thrown and read rather than set.
    ///
    /// The value is not chosen and then displayed. The die is thrown, it tumbles, and
    /// whichever face ends up pointing at the ceiling is the number. That is the whole
    /// point of rolling dice, and it means the face showing and the value scored cannot
    /// disagree, because there is only one of them.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
    public class YahtzeeDie : MonoBehaviour
    {
        Rigidbody body;
        MeshRenderer shell;
        Color faceColour;
        Color heldColour;

        public bool Held { get; private set; }

        /// <summary>Whichever face is pointing up right now.</summary>
        public int Value => PippedDie.ValueShowing(transform.rotation);

        public bool IsAtRest =>
            body == null || body.IsSleeping()
            || (body.linearVelocity.sqrMagnitude < 0.004f && body.angularVelocity.sqrMagnitude < 0.04f);

        public static YahtzeeDie Create(Transform parent, Color face, Color pip, Color held)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Die";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.42f;

            var die = go.AddComponent<YahtzeeDie>();
            die.shell = go.GetComponent<MeshRenderer>();
            die.faceColour = face;
            die.heldColour = held;
            die.shell.sharedMaterial = ArcadeMaterials.Get(face, 0.45f);

            // RequireComponent already put these on when the die was added, and asking for
            // a second Rigidbody returns null rather than another one.
            var body = go.GetComponent<Rigidbody>();
            body.mass = 0.05f;
            body.linearDamping = 0.2f;
            body.angularDamping = 0.35f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // A tumbling die is small and fast, and will pass through a tray wall otherwise.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            die.body = body;

            go.GetComponent<BoxCollider>().material = DicePhysics();
            PippedDie.BuildPips(go.transform, pip);
            return die;
        }

        static PhysicsMaterial dicePhysics;

        /// <summary>Enough bounce to tumble, enough friction to stop rather than slide.</summary>
        static PhysicsMaterial DicePhysics()
        {
            if (dicePhysics != null) return dicePhysics;
            dicePhysics = new PhysicsMaterial("YahtzeeDie")
            {
                bounciness = 0.32f,
                dynamicFriction = 0.42f,
                staticFriction = 0.5f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Average,
            };
            return dicePhysics;
        }

        /// <summary>Throws the die from a point, with enough spin to tumble properly.</summary>
        public void Throw(Vector3 from, Vector3 velocity)
        {
            body.isKinematic = false;
            transform.position = from;
            transform.rotation = UnityEngine.Random.rotation;
            body.linearVelocity = velocity;
            // Without real spin a die slides out and lands on the face it started on, which
            // looks placed rather than thrown.
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 22f;
            body.WakeUp();
        }

        public void Halt()
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        /// <summary>
        /// Lifts a held die out of the throw and parks it, squared up so its number stays
        /// readable while the others are rolling.
        /// </summary>
        public void Park(Vector3 position)
        {
            int showing = Value;
            body.isKinematic = true;
            Halt();
            transform.position = position;
            transform.rotation = PippedDie.RotationShowing(showing);
        }

        public void SetHeld(bool held)
        {
            Held = held;
            shell.sharedMaterial = held
                ? ArcadeMaterials.Emissive(heldColour, 0.4f)
                : ArcadeMaterials.Get(faceColour, 0.45f);
        }
    }
}
