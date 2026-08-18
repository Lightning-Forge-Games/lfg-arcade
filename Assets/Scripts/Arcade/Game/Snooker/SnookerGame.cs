using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core.Snooker;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightningForge.Arcade.Game.Snooker
{
    /// <summary>
    /// Snooker on a full table.
    ///
    /// The balls are real rigid bodies rather than a bespoke solver, which is what makes
    /// cannons, doubles and screw off a cushion behave the way they should without any of
    /// it being written down. What that costs is that the table has to wait for everything
    /// to stop before it can say what the shot did.
    ///
    /// A shot is a direction and a power, deliberately: it is the whole input, so sending
    /// one across a network later is a small change rather than a rewrite. What stops that
    /// being enough on its own is that PhysX is not deterministic across platforms, so an
    /// online frame would also have to relay the settled positions afterwards and let the
    /// shooter's table be the one that counts.
    /// </summary>
    public class SnookerGame : ArcadeGame
    {
        const float TableLength = 7f;
        const float TableWidth = 3.5f;
        const float BallRadius = 0.075f;
        const float CushionHeight = 0.22f;
        // A real pocket is a little over one and a half balls wide.
        const float PocketRadius = 0.15f;

        /// <summary>Half the gap left in a cushion at each pocket, so a ball can get in.</summary>
        const float PocketGap = 0.19f;
        const float MaxImpulse = 2.7f;
        const float ChargeSeconds = 1.1f;

        static readonly Color Cloth = new Color(0.09f, 0.26f, 0.16f);
        static readonly Color Rail = new Color(0.20f, 0.12f, 0.07f);
        static readonly Color CueTip = new Color(0.85f, 0.82f, 0.72f);

        readonly Dictionary<SnookerBall, Vector3> spots = new Dictionary<SnookerBall, Vector3>();
        readonly List<SnookerBallView> balls = new List<SnookerBallView>();
        readonly List<SnookerBall> pottedThisShot = new List<SnookerBall>();

        SnookerFrame frame;
        SnookerBallView cueBall;
        Transform root;
        Transform cueStick;
        LineRenderer aimLine;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine settling;

        SnookerBall? firstContact;
        bool contactRecorded;
        bool shotInFlight;
        float charge;
        bool charging;
        Vector3 aimDirection = Vector3.right;
        string lastOutcome = string.Empty;

        public override ArcadeGameId Id => ArcadeGameId.Snooker;

        public override bool IsFinished => frame != null && frame.IsFinished;

        public override string DebugState =>
            frame == null ? "no frame" : "reds" + frame.RedsRemaining + " score" + frame.ScoreOf(0);

        public override string StatusText
        {
            get
            {
                if (frame == null) return string.Empty;
                if (frame.IsFinished)
                {
                    return frame.Players > 1
                        ? (frame.ScoreOf(0) == frame.ScoreOf(1)
                            ? "Frame tied at " + frame.ScoreOf(0)
                            : (frame.ScoreOf(0) > frame.ScoreOf(1) ? "Player 1 wins " : "Player 2 wins ")
                              + Mathf.Max(frame.ScoreOf(0), frame.ScoreOf(1)) + " to "
                              + Mathf.Min(frame.ScoreOf(0), frame.ScoreOf(1)))
                        : "Frame over. " + frame.ScoreOf(0) + " points";
                }

                string score = frame.Players > 1
                    ? "P1 " + frame.ScoreOf(0) + "  P2 " + frame.ScoreOf(1) + "   "
                    : "Score " + frame.ScoreOf(0) + "   ";

                string who = frame.Players > 1 ? "Player " + (frame.CurrentPlayer + 1) + ", on " : "On ";
                string breaking = frame.Break > 0 ? "   Break " + frame.Break : string.Empty;
                string tail = string.IsNullOrEmpty(lastOutcome) ? string.Empty : "   " + lastOutcome;

                if (settling != null) return score + "..." + tail;
                return score + who + frame.BallOnName + breaking + tail;
            }
        }

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
        }

        protected override void OnBegin()
        {
            // Hot seat is two players sharing the table. There is no computer opponent, so
            // single player is a solo frame rather than a match against anything.
            frame = new SnookerFrame(Setup.Mode == GameMode.HotSeat ? 2 : 1);
            lastOutcome = string.Empty;

            BuildTable();
            RackBalls();

            if (cameraRig != null)
            {
                cameraRig.OverrideFraming(new BoardFraming
                {
                    Focus = Vector3.zero,
                    Height = 6.6f,
                    Distance = 4.4f,
                    Pitch = 58f,
                    Fov = 42f,
                    HalfExtent = TableLength * 0.56f,
                });
            }

            Raise();
        }

        public override void End()
        {
            if (settling != null) { StopCoroutine(settling); settling = null; }
            if (cameraRig != null) cameraRig.ClearFramingOverride();
            if (root != null)
            {
                Destroy(root.gameObject);
                root = null;
            }
            balls.Clear();
        }

        public override void Restart()
        {
            Begin(Setup);
        }

        /// <summary>
        /// Snooker has no online mode yet, but a shot is already a direction and a power,
        /// so the encoding exists and is what an online frame would relay.
        /// </summary>
        public override bool ApplyRemoteMove(string encoded)
        {
            string[] parts = encoded.Split(',');
            if (parts.Length != 3) return false;
            if (!float.TryParse(parts[0], out float x)) return false;
            if (!float.TryParse(parts[1], out float z)) return false;
            if (!float.TryParse(parts[2], out float power)) return false;

            // A shot arriving while the table is still moving cannot be played, and saying
            // otherwise would leave the two sides believing different things happened.
            if (settling != null || cueBall == null) return false;

            Fire(new Vector3(x, 0f, z).normalized, Mathf.Clamp01(power));
            return true;
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || frame == null || IsFinished) return;
            if (settling != null) { UpdateCue(0f); return; }

            AimAtPointer();

            bool held = IsPressed();
            if (held && !charging)
            {
                charging = true;
                charge = 0f;
            }
            else if (held)
            {
                charge = Mathf.Min(1f, charge + Time.deltaTime / ChargeSeconds);
            }
            else if (charging)
            {
                charging = false;
                // A tap with no hold still plays a gentle shot rather than nothing.
                Fire(aimDirection, Mathf.Max(0.12f, charge));
                charge = 0f;
            }

            UpdateCue(charge);
        }

        static bool IsPressed()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.isPressed;
        }

        void AimAtPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || cueBall == null) return;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return;

            // Intersect the pointer ray with the cloth, which is the plane the balls sit on.
            Ray ray = targetCamera.ScreenPointToRay(pointer);
            var cloth = new Plane(Vector3.up, new Vector3(0f, BallRadius, 0f));
            if (!cloth.Raycast(ray, out float distance)) return;

            Vector3 target = ray.GetPoint(distance);
            Vector3 direction = target - cueBall.transform.position;
            direction.y = 0f;

            // Below a threshold the direction is noise, so the old aim is kept.
            if (direction.sqrMagnitude > 0.0004f) aimDirection = direction.normalized;
        }

        void Fire(Vector3 direction, float power)
        {
            if (cueBall == null || settling != null) return;

            pottedThisShot.Clear();
            firstContact = null;
            contactRecorded = false;
            lastOutcome = string.Empty;

            shotInFlight = true;
            cueBall.Strike(direction * (power * MaxImpulse));
            settling = StartCoroutine(WaitForBallsToStop());
            Raise();
        }

        IEnumerator WaitForBallsToStop()
        {
            // A shot needs a moment to get going before "everything is still" means anything.
            yield return new WaitForSeconds(0.12f);

            float deadline = Time.time + 20f;
            while (Time.time < deadline)
            {
                bool moving = false;
                foreach (SnookerBallView ball in balls)
                {
                    if (ball.IsPotted || !ball.gameObject.activeSelf) continue;
                    if (!ball.IsAtRest) { moving = true; break; }
                }
                if (!moving) break;
                yield return new WaitForSeconds(0.08f);
            }

            foreach (SnookerBallView ball in balls) ball.Halt();
            settling = null;
            ResolveShot();
        }

        void ResolveShot()
        {
            shotInFlight = false;

            var shot = new SnookerShot
            {
                FirstContact = firstContact,
                Potted = new List<SnookerBall>(pottedThisShot),
            };

            SnookerOutcome outcome = frame.Apply(shot);
            lastOutcome = outcome.IsFoul
                ? outcome.Description + " (" + outcome.FoulPoints + " away)"
                : outcome.Scored > 0 ? outcome.Description + " for " + outcome.Scored : outcome.Description;

            // Colours the rules want back on the table go to their spots, or the nearest
            // free one behind, as they would in a real frame.
            if (outcome.Respot != null)
            {
                foreach (SnookerBall colour in outcome.Respot) Respot(colour);
            }

            // In off means the cue ball comes back in hand; it is placed on the brown spot
            // rather than offering ball in hand, which would need its own interface.
            if (pottedThisShot.Contains(SnookerBall.Cue) && cueBall != null)
            {
                cueBall.PlaceAt(FreeSpotNear(spots[SnookerBall.Brown]));
            }

            Raise();
        }

        void Respot(SnookerBall colour)
        {
            SnookerBallView view = Find(colour);
            if (view == null) return;
            view.PlaceAt(FreeSpotNear(spots[colour]));
        }

        /// <summary>
        /// The spot itself if it is clear, otherwise the nearest clear place along the table
        /// so a respotted ball never lands on top of another.
        /// </summary>
        Vector3 FreeSpotNear(Vector3 spot)
        {
            if (IsClear(spot)) return spot;

            for (float offset = BallRadius * 2.1f; offset < TableLength * 0.5f; offset += BallRadius * 2.1f)
            {
                Vector3 up = spot + Vector3.right * offset;
                if (Mathf.Abs(up.x) < TableLength * 0.5f - BallRadius && IsClear(up)) return up;

                Vector3 down = spot - Vector3.right * offset;
                if (Mathf.Abs(down.x) < TableLength * 0.5f - BallRadius && IsClear(down)) return down;
            }
            return spot;
        }

        bool IsClear(Vector3 position)
        {
            foreach (SnookerBallView ball in balls)
            {
                if (ball.IsPotted || !ball.gameObject.activeSelf) continue;
                if ((ball.transform.position - position).sqrMagnitude < BallRadius * BallRadius * 4.2f)
                {
                    return false;
                }
            }
            return true;
        }

        SnookerBallView Find(SnookerBall ball)
        {
            foreach (SnookerBallView view in balls)
            {
                if (view.Ball == ball) return view;
            }
            return null;
        }

        void OnBallContact(SnookerBallView cue, SnookerBallView struck)
        {
            // Only contacts made by a shot count. Respotting a ball, or putting the cue
            // ball back in hand after an in off, moves it in one step, and a swept move
            // past a neighbour would otherwise be recorded as having struck it.
            if (!shotInFlight) return;

            // Only the first contact of a shot decides legality.
            if (contactRecorded) return;
            contactRecorded = true;
            firstContact = struck.Ball;
        }

        void OnBallPotted(SnookerBallView ball)
        {
            pottedThisShot.Add(ball.Ball);
        }

        // Table ---------------------------------------------------------------------

        void BuildTable()
        {
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Snooker Table");
            go.transform.SetParent(transform, false);
            root = go.transform;

            float halfLength = TableLength * 0.5f;
            float halfWidth = TableWidth * 0.5f;

            AddBox("Cloth", new Vector3(0f, -0.05f, 0f),
                new Vector3(TableLength, 0.1f, TableWidth), Cloth, true);

            // Cushions run in segments with a gap at every pocket. A continuous rail looks
            // the same but makes potting impossible: the ball is held away from the pocket
            // mouth by the very cushion it is trying to pass.
            float longRun = halfLength - PocketGap * 2f;
            foreach (float side in new[] { 1f, -1f })
            {
                foreach (float half in new[] { 1f, -1f })
                {
                    AddBox("Cushion_Long", new Vector3(half * halfLength * 0.5f,
                            CushionHeight * 0.5f, side * (halfWidth + 0.15f)),
                        new Vector3(longRun, CushionHeight, 0.3f), Rail, true);
                }

                AddBox("Cushion_End", new Vector3(side * (halfLength + 0.15f), CushionHeight * 0.5f, 0f),
                    new Vector3(0.3f, CushionHeight, TableWidth - PocketGap * 2f), Rail, true);
            }

            // Behind the pockets, so a ball that misses one is turned back rather than lost.
            AddBox("Surround_Top", new Vector3(0f, CushionHeight * 0.5f, halfWidth + 0.55f),
                new Vector3(TableLength + 1.4f, CushionHeight, 0.3f), Rail, true);
            AddBox("Surround_Bottom", new Vector3(0f, CushionHeight * 0.5f, -halfWidth - 0.55f),
                new Vector3(TableLength + 1.4f, CushionHeight, 0.3f), Rail, true);
            AddBox("Surround_Left", new Vector3(-halfLength - 0.55f, CushionHeight * 0.5f, 0f),
                new Vector3(0.3f, CushionHeight, TableWidth + 1.4f), Rail, true);
            AddBox("Surround_Right", new Vector3(halfLength + 0.55f, CushionHeight * 0.5f, 0f),
                new Vector3(0.3f, CushionHeight, TableWidth + 1.4f), Rail, true);

            foreach (Vector3 pocket in new[]
                     {
                         new Vector3(-halfLength, 0f, -halfWidth),
                         new Vector3(-halfLength, 0f, halfWidth),
                         new Vector3(0f, 0f, -halfWidth),
                         new Vector3(0f, 0f, halfWidth),
                         new Vector3(halfLength, 0f, -halfWidth),
                         new Vector3(halfLength, 0f, halfWidth),
                     })
            {
                AddPocket(pocket);
            }

            BuildCue();
            ComputeSpots();
        }

        void AddPocket(Vector3 position)
        {
            var visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = "PocketMouth";
            visual.transform.SetParent(root, false);
            visual.transform.localPosition = position + Vector3.up * 0.005f;
            visual.transform.localScale = new Vector3(PocketRadius * 2.2f, 0.01f, PocketRadius * 2.2f);
            Destroy(visual.GetComponent<Collider>());
            visual.GetComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(new Color(0.02f, 0.02f, 0.02f), 0.1f);

            var trigger = new GameObject("Pocket");
            trigger.transform.SetParent(root, false);
            trigger.transform.localPosition = position;
            var collider = trigger.AddComponent<SphereCollider>();
            collider.radius = PocketRadius;
            collider.isTrigger = true;
            trigger.AddComponent<SnookerPocket>();
        }

        void BuildCue()
        {
            var cue = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cue.name = "Cue";
            cue.transform.SetParent(root, false);
            cue.transform.localScale = new Vector3(0.035f, 0.9f, 0.035f);
            Destroy(cue.GetComponent<Collider>());
            cue.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(CueTip, 0.5f);
            cueStick = cue.transform;

            var line = new GameObject("AimLine");
            line.transform.SetParent(root, false);
            aimLine = line.AddComponent<LineRenderer>();
            aimLine.widthMultiplier = 0.015f;
            aimLine.positionCount = 2;
            aimLine.material = ArcadeMaterials.Emissive(new Color(0.9f, 0.9f, 0.8f), 0.5f);
            aimLine.useWorldSpace = true;
        }

        /// <summary>
        /// Draws the cue behind the ball along the aim, pulled back by how much power is
        /// charged. The stick is the power meter, which keeps the reading where the player
        /// is already looking.
        /// </summary>
        void UpdateCue(float power)
        {
            bool show = cueBall != null && !cueBall.IsPotted && settling == null && !IsFinished;
            if (cueStick != null) cueStick.gameObject.SetActive(show);
            if (aimLine != null) aimLine.enabled = show;
            if (!show) return;

            Vector3 ballPosition = cueBall.transform.position;
            float pullBack = 0.28f + power * 0.55f;
            Vector3 behind = ballPosition - aimDirection * (pullBack + 0.9f);

            cueStick.position = behind + Vector3.up * 0.02f;
            // The cylinder's length runs along its local Y, so point that down the aim.
            cueStick.rotation = Quaternion.LookRotation(aimDirection) * Quaternion.Euler(90f, 0f, 0f);

            aimLine.SetPosition(0, ballPosition);
            aimLine.SetPosition(1, ballPosition + aimDirection * 1.6f);
        }

        void ComputeSpots()
        {
            float halfLength = TableLength * 0.5f;
            float baulkX = -halfLength + TableLength / 5f;
            float dRadius = TableWidth / 6f;

            spots[SnookerBall.Yellow] = new Vector3(baulkX, BallRadius, dRadius);
            spots[SnookerBall.Green] = new Vector3(baulkX, BallRadius, -dRadius);
            spots[SnookerBall.Brown] = new Vector3(baulkX, BallRadius, 0f);
            spots[SnookerBall.Blue] = new Vector3(0f, BallRadius, 0f);
            spots[SnookerBall.Pink] = new Vector3(halfLength * 0.5f, BallRadius, 0f);
            spots[SnookerBall.Black] = new Vector3(halfLength - TableLength * 0.0908f, BallRadius, 0f);
        }

        void RackBalls()
        {
            foreach (SnookerBallView ball in balls)
            {
                if (ball != null) Destroy(ball.gameObject);
            }
            balls.Clear();

            // In the D, which is behind the baulk line, not on it. Level with the line the
            // cue ball sits within a ball's width of the yellow and clips it on the break.
            float baulkX = -TableLength * 0.5f + TableLength / 5f;
            cueBall = MakeBall(SnookerBall.Cue,
                new Vector3(baulkX - 0.32f, BallRadius, TableWidth / 9f));
            cueBall.Contacted += OnBallContact;

            foreach (var pair in spots) MakeBall(pair.Key, pair.Value);

            // Fifteen reds in a triangle, apex just behind the pink.
            float spacing = BallRadius * 2.02f;
            float apexX = spots[SnookerBall.Pink].x + spacing;
            int index = 0;
            for (int row = 0; row < 5; row++)
            {
                for (int i = 0; i <= row; i++)
                {
                    float x = apexX + row * spacing * 0.87f;
                    float z = (i - row * 0.5f) * spacing;
                    MakeBall(SnookerBall.Red, new Vector3(x, BallRadius, z));
                    index++;
                }
            }
        }

        SnookerBallView MakeBall(SnookerBall kind, Vector3 position)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = kind.ToString();
            go.transform.SetParent(root, false);
            go.transform.localPosition = position;
            go.transform.localScale = Vector3.one * (BallRadius * 2f);

            var collider = go.GetComponent<SphereCollider>();
            collider.material = BallPhysics();

            var body = go.AddComponent<Rigidbody>();
            body.mass = 0.14f;
            // Cloth drag, which is what makes a ball roll to a stop rather than forever.
            body.linearDamping = 0.72f;
            body.angularDamping = 1.4f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            // Balls move fast enough to pass through a cushion in a single step otherwise.
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.constraints = RigidbodyConstraints.FreezePositionY;

            go.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(ColourOf(kind), 0.75f);

            var view = go.AddComponent<SnookerBallView>();
            view.Ball = kind;
            view.SpotPosition = position;
            view.Potted += OnBallPotted;

            balls.Add(view);
            return view;
        }

        static PhysicsMaterial ballPhysics;
        static PhysicsMaterial cushionPhysics;

        /// <summary>Ball on ball is nearly elastic, which is what makes a clean cannon work.</summary>
        static PhysicsMaterial BallPhysics()
        {
            if (ballPhysics != null) return ballPhysics;
            ballPhysics = new PhysicsMaterial("SnookerBall")
            {
                bounciness = 0.93f,
                dynamicFriction = 0.04f,
                staticFriction = 0.04f,
                frictionCombine = PhysicsMaterialCombine.Multiply,
                bounceCombine = PhysicsMaterialCombine.Multiply,
            };
            return ballPhysics;
        }

        /// <summary>A cushion gives some of the pace back, but nowhere near all of it.</summary>
        static PhysicsMaterial CushionPhysics()
        {
            if (cushionPhysics != null) return cushionPhysics;
            cushionPhysics = new PhysicsMaterial("SnookerCushion")
            {
                bounciness = 0.62f,
                dynamicFriction = 0.12f,
                staticFriction = 0.12f,
                frictionCombine = PhysicsMaterialCombine.Multiply,
                bounceCombine = PhysicsMaterialCombine.Multiply,
            };
            return cushionPhysics;
        }

        static Color ColourOf(SnookerBall ball)
        {
            switch (ball)
            {
                case SnookerBall.Red: return new Color(0.64f, 0.09f, 0.08f);
                case SnookerBall.Yellow: return new Color(0.86f, 0.72f, 0.12f);
                case SnookerBall.Green: return new Color(0.08f, 0.40f, 0.18f);
                case SnookerBall.Brown: return new Color(0.36f, 0.22f, 0.10f);
                case SnookerBall.Blue: return new Color(0.10f, 0.25f, 0.65f);
                case SnookerBall.Pink: return new Color(0.88f, 0.45f, 0.55f);
                case SnookerBall.Black: return new Color(0.05f, 0.05f, 0.05f);
                default: return new Color(0.94f, 0.93f, 0.89f);
            }
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour, bool collide)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;

            if (collide) box.GetComponent<Collider>().material = CushionPhysics();
            else Destroy(box.GetComponent<Collider>());

            box.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(colour, 0.2f);
        }
    }
}
