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
        /// <summary>Edge length. Deliberately small: the tray wants room to scatter.</summary>
        public const float Size = 0.34f;

        /// <summary>
        /// Where the die belongs when it is not in the cup.
        ///
        /// A thrown die used to be parented to nothing, which made it a root object that
        /// survived the table being torn down. Every new game left its dice behind and the
        /// tray filled up with the last game's roll.
        /// </summary>
        public Transform Home { get; set; }

        /// <summary>Raised when the die strikes something hard enough to hear.</summary>
        public event System.Action<float> Struck;

        Rigidbody body;
        MeshRenderer shell;
        float lastImpact;
        /// <summary>True while the die is up on the rail rather than down in the tray.</summary>
        public bool OnRail { get; private set; }

        Vector3 trayPosition;
        Quaternion trayRotation;
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
            go.transform.localScale = Vector3.one * Size;

            // A real die has softened edges, and a sharp one catches no light along its
            // length. The collider stays a plain box: physics does not care about a bevel.
            ArcadeMeshes.ApplyMesh(go, ArcadeMeshes.RoundedBox(Vector3.one, 0.13f, 6));

            var die = go.AddComponent<YahtzeeDie>();
            die.Home = parent;
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
                bounciness = 0.18f,
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
            // A thrown die is loose in the tray again, whatever it was doing before.
            OnRail = false;
            Reveal();
            transform.SetParent(Home, true);
            body.isKinematic = false;
            transform.position = from;
            transform.rotation = UnityEngine.Random.rotation;
            body.linearVelocity = velocity;
            // Without real spin a die slides out and lands on the face it started on, which
            // looks placed rather than thrown.
            body.angularVelocity = UnityEngine.Random.insideUnitSphere * 22f;
            body.WakeUp();
        }

        /// <summary>
        /// Tucks the die inside the cup and lets it ride along. Kinematic on purpose: dice
        /// bouncing around inside a moving cup is a fight with the physics engine for an
        /// effect nobody can see through the walls.
        /// </summary>
        public void StowIn(Transform cup, Vector3 localPosition)
        {
            body.isKinematic = true;
            Halt();
            transform.SetParent(cup, false);
            transform.localPosition = localPosition;
            transform.localRotation = Random.rotation;

            // Hidden rather than merely inside. A cup wide enough to hold five dice without
            // any of them poking through its wall would be a bucket, and nobody can see
            // into it anyway, so the honest thing is to take them off screen until they
            // pour out.
            shell.enabled = false;
            SetPipsVisible(false);
        }

        void SetPipsVisible(bool visible)
        {
            Transform pips = transform.Find("Pips");
            if (pips == null) return;
            foreach (MeshRenderer pip in pips.GetComponentsInChildren<MeshRenderer>(true))
            {
                pip.enabled = visible;
            }
        }

        void Reveal()
        {
            shell.enabled = true;
            SetPipsVisible(true);
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
            Reveal();
            transform.SetParent(Home, true);
            body.isKinematic = true;
            Halt();
            transform.position = position;
            transform.rotation = PippedDie.RotationShowing(showing);
        }

        /// <summary>
        /// Reports a landing worth hearing. Thresholded and rate limited, because a die
        /// settling generates a stream of tiny contacts and playing a knock for each turns
        /// one landing into a machine gun.
        /// </summary>
        void OnCollisionEnter(Collision collision)
        {
            if (body.isKinematic) return;

            float force = collision.relativeVelocity.magnitude;
            if (force < 0.7f) return;
            if (Time.time - lastImpact < 0.05f) return;

            lastImpact = Time.time;
            Struck?.Invoke(force);
        }

        /// <summary>
        /// Lifts the die out of the tray onto the rail, remembering exactly where and how
        /// it was lying so that changing your mind can put it back the way it landed.
        /// </summary>
        public void LiftToRail(Vector3 railPosition)
        {
            if (!OnRail)
            {
                trayPosition = transform.position;
                trayRotation = transform.rotation;
                OnRail = true;
            }

            int showing = Value;
            Park(railPosition);
            // Squared up on the rail, so a row of kept dice is easy to read at a glance.
            transform.rotation = PippedDie.RotationShowing(showing);
        }

        /// <summary>
        /// Puts the die back down where it was, at the angle it came to rest at. Returning
        /// it squared up would look like it had been rerolled rather than un-kept.
        /// </summary>
        public void ReturnToTray(Vector3 position)
        {
            if (!OnRail) return;
            OnRail = false;

            Park(position);
            transform.rotation = trayRotation;
        }

        /// <summary>Where this die was lying before it was lifted.</summary>
        public Vector3 TrayPosition => trayPosition;

        public void SetHeld(bool held)
        {
            Held = held;
            shell.sharedMaterial = held
                ? ArcadeMaterials.Emissive(heldColour, 0.4f)
                : ArcadeMaterials.Get(faceColour, 0.45f);
        }
    }
}
