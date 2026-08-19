using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Yahtzee;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LightningForge.Arcade.Game.Yahtzee
{
    /// <summary>
    /// Yahtzee: five dice thrown from a cup into a tray, and a scorecard beside them.
    ///
    /// The dice are genuinely rolled. The cup lifts, swings over the tray and tips, the
    /// dice tumble out and bounce off the walls, and whatever they come to rest showing is
    /// the roll. Nothing decides the numbers in advance and then poses the dice to match,
    /// which means the faces and the score can never disagree.
    ///
    /// The card is a table of thirteen numbers and would be worse in 3D, so it stays as UI,
    /// with both players' cards visible because half the game is watching which boxes your
    /// opponent has left.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class YahtzeeGame : ArcadeGame
    {
        const int DiceCount = 5;
        const int RollsPerTurn = 3;

        // Half again as large as it was, so a throw has room to scatter rather than
        // landing in a heap.
        const float TrayWidth = 5.1f;
        const float TrayDepth = 3.6f;
        const float WallHeight = 0.46f;

        const float CupRadius = 0.38f;
        const float CupHeight = 0.92f;

        /// <summary>How high the cup rides while it is being carried.</summary>
        const float CarryHeight = 1.15f;

        /// <summary>Unity's built in layer 2, which the default raycast mask excludes.</summary>
        const int IgnoreRaycastLayer = 2;

        static readonly Color TrayFelt = new Color(0.11f, 0.19f, 0.14f);
        static readonly Color TrayWall = new Color(0.22f, 0.14f, 0.09f);
        static readonly Color CupColour = new Color(0.26f, 0.16f, 0.10f);
        static readonly Color DieColour = new Color(0.92f, 0.90f, 0.85f);
        static readonly Color PipColour = new Color(0.11f, 0.10f, 0.10f);
        static readonly Color HeldColour = new Color(0.85f, 0.66f, 0.28f);

        readonly YahtzeeScorecard[] cards =
        {
            new YahtzeeScorecard(),
            new YahtzeeScorecard(),
        };

        readonly int[] dice = new int[DiceCount];
        readonly List<YahtzeeDie> dieViews = new List<YahtzeeDie>(DiceCount);

        YahtzeePlayer opponent;
        Transform root;
        Transform cup;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine thinking;
        Coroutine rolling;
        UIDocument document;
        AudioSource rattleSource;
        AudioSource impactSource;

        /// <summary>The dice waiting in the cup, loaded when a roll begins.</summary>
        readonly List<YahtzeeDie> loaded = new List<YahtzeeDie>();

        bool cupHeld;
        bool countedThisRoll;
        Vector3 cupTarget;
        Vector3 lastCupPosition;
        float swirl;

        VisualElement panel;
        readonly Dictionary<YahtzeeCategory, Button>[] boxButtons =
        {
            new Dictionary<YahtzeeCategory, Button>(),
            new Dictionary<YahtzeeCategory, Button>(),
        };
        Label[] totalLabels = new Label[2];
        Button rollButton;

        int seat;
        int rollsUsed;
        bool rolledThisTurn;

        public override ArcadeGameId Id => ArcadeGameId.Yahtzee;

        public override bool IsFinished => cards[0].IsComplete && cards[1].IsComplete;

        public override string DebugState =>
            "seat" + seat + " rolls" + rollsUsed + " dice" + string.Join("", System.Array.ConvertAll(dice, d => d.ToString()));

        public override string StatusText
        {
            get
            {
                if (IsFinished)
                {
                    int a = cards[0].Total;
                    int b = cards[1].Total;
                    if (a == b) return "A tie at " + a;
                    return (a > b ? "Player 1 wins " : "Player 2 wins ")
                        + Mathf.Max(a, b) + " to " + Mathf.Min(a, b);
                }

                string who = "Player " + (seat + 1);
                if (cupHeld) return who + ", swirl the cup then click to throw";
                if (rolling != null) return who + " rolling...";
                if (!rolledThisTurn) return who + " to roll, or pick up the cup";
                if (rollsUsed >= RollsPerTurn) return who + ", pick a box";
                return who + ", roll " + rollsUsed + " of " + RollsPerTurn + ". Click a die to keep it";
            }
        }

        bool FirstSeatToPlay => seat == 0;

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
            document = GetComponent<UIDocument>();
        }

        protected override void OnBegin()
        {
            opponent = new YahtzeePlayer(new System.Random());

            cards[0].Reset();
            cards[1].Reset();
            seat = 0;
            rollsUsed = 0;
            rolledThisTurn = false;
            for (int i = 0; i < DiceCount; i++) dice[i] = 1;

            ArcadeAudio.EnsureListener();
            BuildTable();
            BuildCard();

            if (cameraRig != null)
            {
                cameraRig.OverrideFraming(new BoardFraming
                {
                    // Over the tray and steep, because the number that matters is the one
                    // on top and a low angle hides it behind the near face.
                    Focus = new Vector3(-1.4f, 0f, -0.3f),
                    Height = 9.6f,
                    Distance = 4.4f,
                    Pitch = 64f,
                    Fov = 42f,
                    HalfExtent = 5.4f,
                });
            }

            RefreshCard();
            BeginTurn();
        }

        public override void End()
        {
            StopWork();
            if (cameraRig != null) cameraRig.ClearFramingOverride();
            DestroyDice();
            if (root != null)
            {
                Destroy(root.gameObject);
                root = null;
            }
            if (document != null && document.rootVisualElement != null) document.rootVisualElement.Clear();
        }

        public override void Restart()
        {
            Begin(Setup);
        }

        void StopWork()
        {
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
            if (rolling != null) { StopCoroutine(rolling); rolling = null; }
            if (rattleSource != null) rattleSource.Stop();
            cupHeld = false;
            loaded.Clear();
        }

        // Turn flow -----------------------------------------------------------------

        void BeginTurn()
        {
            rollsUsed = 0;
            rolledThisTurn = false;
            foreach (YahtzeeDie die in dieViews) die.SetHeld(false);

            Raise();
            RefreshCard();

            if (IsFinished) return;

            if (Setup.Mode == GameMode.SinglePlayer && !LocalControls(FirstSeatToPlay))
            {
                thinking = StartCoroutine(OpponentTurn());
            }
        }

        /// <summary>The Roll button: loads the cup and throws it, all in one.</summary>
        void Roll()
        {
            if (!CanRoll()) return;
            LoadCup();
            rolling = StartCoroutine(RollRoutine());
        }

        bool CanRoll() =>
            rollsUsed < RollsPerTurn && rolling == null && thinking == null && !cupHeld
            && !IsFinished && LocalControls(FirstSeatToPlay);

        /// <summary>
        /// Gathers the dice that are going to be thrown into the cup.
        ///
        /// This is what stops a throw landing on dice still lying in the tray. They are
        /// released over the course of the tip, so without collecting them first the ones
        /// still waiting are sitting exactly where the first ones out are about to land.
        /// </summary>
        void LoadCup()
        {
            loaded.Clear();
            foreach (YahtzeeDie die in dieViews)
            {
                if (die.Held) continue;
                loaded.Add(die);
            }

            for (int i = 0; i < loaded.Count; i++)
            {
                // Stacked loosely down the inside of the cup.
                float angle = i * 2.4f;
                loaded[i].StowIn(cup, new Vector3(
                    Mathf.Cos(angle) * 0.11f,
                    -0.3f + i * 0.085f,
                    Mathf.Sin(angle) * 0.11f));
            }
        }

        /// <summary>
        /// The throw. The cup lifts, swings over the tray and tips; the dice leave its mouth
        /// with the spin it gave them and are left to settle on their own.
        /// </summary>
        IEnumerator RollRoutine()
        {
            // The hand throw counts its own roll before it starts, so it is only counted
            // here for the ones that come straight from the button.
            if (!countedThisRoll)
            {
                rollsUsed++;
                rolledThisTurn = true;
            }
            countedThisRoll = false;
            Raise();
            RefreshCard();

            var loose = new List<YahtzeeDie>(loaded);

            Vector3 rest = CupRest();
            Vector3 over = new Vector3(TrayCentre().x - 0.7f, 1.55f, TrayCentre().z - 0.15f);

            // Lift and carry the cup over the tray.
            yield return MoveCup(rest, over, Quaternion.identity, Quaternion.Euler(0f, 0f, -34f), 0.32f);

            // Tip it, and let the dice go part way through the tip so they pour rather than
            // all appearing at the moment it finishes.
            Quaternion tipped = Quaternion.Euler(0f, 0f, -132f);
            float duration = 0.42f;
            float elapsed = 0f;
            int released = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cup.rotation = Quaternion.Slerp(Quaternion.Euler(0f, 0f, -34f), tipped, t * t);

                int shouldHaveReleased = Mathf.FloorToInt(Mathf.InverseLerp(0.25f, 0.85f, t) * loose.Count);
                while (released < shouldHaveReleased && released < loose.Count)
                {
                    Vector3 mouth = cup.position + cup.up * (CupHeight * 0.72f);
                    Vector3 velocity = new Vector3(1.5f, -1.4f, 0f)
                        + new Vector3(Random.Range(-0.4f, 0.4f), 0f, Random.Range(-0.9f, 0.9f));
                    loose[released].Throw(mouth, velocity);
                    released++;
                }
                yield return null;
            }

            // Anything the loop did not get to.
            while (released < loose.Count)
            {
                loose[released].Throw(cup.position + cup.up * (CupHeight * 0.72f),
                    new Vector3(1.5f, -1.4f, 0f));
                released++;
            }

            yield return MoveCup(over, rest, tipped, Quaternion.identity, 0.3f);

            yield return Settle(loose);
        }

        /// <summary>Waits for the thrown dice to stop, then reads what they are showing.</summary>
        IEnumerator Settle(List<YahtzeeDie> loose)
        {
            // Let them tumble and stop.
            float deadline = Time.time + 6f;
            yield return new WaitForSeconds(0.35f);
            while (Time.time < deadline)
            {
                bool moving = false;
                foreach (YahtzeeDie die in loose)
                {
                    if (!die.IsAtRest) { moving = true; break; }
                }
                if (!moving) break;
                yield return new WaitForSeconds(0.08f);
            }

            foreach (YahtzeeDie die in loose) die.Halt();

            // The dice are the source of truth: read what they are actually showing.
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dice[i] = dieViews[i].Value;

            loaded.Clear();
            rolling = null;
            Raise();
            RefreshCard();
        }

        /// <summary>
        /// The hand throw: the cup is already where the player left it, so it only has to
        /// tip from there.
        /// </summary>
        IEnumerator PourFromHand()
        {
            var loose = new List<YahtzeeDie>(loaded);

            // Last guard against pouring outside the tray. The pointer is clamped while
            // swirling, but nothing else guarantees where the cup ended up.
            Vector3 centre = TrayCentre();
            Vector3 safe = cup.position;
            safe.x = Mathf.Clamp(safe.x, centre.x - TrayWidth * 0.35f, centre.x + TrayWidth * 0.35f);
            safe.z = Mathf.Clamp(safe.z, centre.z - TrayDepth * 0.35f, centre.z + TrayDepth * 0.35f);
            safe.y = Mathf.Max(safe.y, CarryHeight);
            cup.position = safe;

            Quaternion upright = cup.rotation;
            Quaternion tipped = Quaternion.Euler(0f, 0f, -128f);
            float duration = 0.4f;
            float elapsed = 0f;
            int released = 0;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                cup.rotation = Quaternion.Slerp(upright, tipped, t * t);

                int shouldHaveReleased =
                    Mathf.FloorToInt(Mathf.InverseLerp(0.2f, 0.8f, t) * loose.Count);
                while (released < shouldHaveReleased && released < loose.Count)
                {
                    Release(loose[released]);
                    released++;
                }
                yield return null;
            }

            while (released < loose.Count)
            {
                Release(loose[released]);
                released++;
            }

            yield return MoveCup(cup.position, CupRest(), cup.rotation, Quaternion.identity, 0.32f);
            yield return Settle(loose);
        }

        void Release(YahtzeeDie die)
        {
            // Clear of the rim. Half the cup's height is exactly the rim, so anything less
            // than that spawns the die inside the vessel it is meant to be leaving.
            Vector3 mouth = cup.position + cup.up * (CupHeight * 0.72f);
            Vector3 velocity = cup.up * -1.4f
                + new Vector3(Random.Range(-0.5f, 0.5f), -0.5f, Random.Range(-0.5f, 0.5f));
            die.Throw(mouth, velocity);
        }

        /// <summary>The computer throws by the same route, without the CanRoll gate.</summary>
        void OpponentRoll()
        {
            if (rolling != null) return;
            LoadCup();
            rolling = StartCoroutine(RollRoutine());
        }

        IEnumerator MoveCup(Vector3 from, Vector3 to, Quaternion fromRotation, Quaternion toRotation,
            float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);
                cup.position = Vector3.Lerp(from, to, eased);
                cup.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);
                yield return null;
            }
            cup.position = to;
            cup.rotation = toRotation;
        }

        void Score(YahtzeeCategory category, bool local)
        {
            if (!cards[seat].Fill(category, dice)) return;

            if (local)
            {
                var sb = new System.Text.StringBuilder();
                sb.Append((int)category).Append(':');
                foreach (int die in dice) sb.Append(die);
                RaiseMovePlayed(sb.ToString());
            }

            seat = 1 - seat;
            Raise();
            BeginTurn();
        }

        public override bool ApplyRemoteMove(string encoded)
        {
            if (rolling != null) return false;

            int colon = encoded.IndexOf(':');
            if (colon <= 0) return false;
            if (!int.TryParse(encoded.Substring(0, colon), out int categoryIndex)) return false;

            string faces = encoded.Substring(colon + 1);
            if (faces.Length != DiceCount) return false;

            for (int i = 0; i < DiceCount; i++)
            {
                if (!int.TryParse(faces[i].ToString(), out int face)) return false;
                dice[i] = face;
            }

            // The opponent's throw happened on their table. Rolling here would produce
            // different numbers, so the dice are placed showing what they actually rolled.
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++)
            {
                dieViews[i].Park(DieRestPosition(i));
                dieViews[i].Park(DieRestPosition(i));
            }
            ShowParkedDice();

            var category = (YahtzeeCategory)categoryIndex;
            if (cards[seat].IsFilled(category)) return false;

            Score(category, false);
            return true;
        }

        /// <summary>Places every die flat, showing the value recorded for it.</summary>
        void ShowParkedDice()
        {
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++)
            {
                dieViews[i].Park(DieRestPosition(i));
                dieViews[i].transform.rotation = PippedDie.RotationShowing(dice[i]);
            }
        }

        IEnumerator OpponentTurn()
        {
            yield return new WaitForSeconds(0.5f);

            OpponentRoll();
            while (rolling != null) yield return null;

            for (int reroll = 0; reroll < RollsPerTurn - 1; reroll++)
            {
                yield return new WaitForSeconds(0.5f);

                bool[] keep = opponent.ChooseKeeps(dice, cards[seat], Setup.Difficulty);
                for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dieViews[i].SetHeld(keep[i]);
                LiftHeldDice();

                yield return new WaitForSeconds(0.45f);
                OpponentRoll();
                while (rolling != null) yield return null;
            }

            yield return new WaitForSeconds(0.7f);
            YahtzeeCategory choice = opponent.ChooseCategory(dice, cards[seat], Setup.Difficulty);
            thinking = null;
            Score(choice, false);
        }

        /// <summary>
        /// Held dice are lifted onto the rail at the back of the tray, out of the way of the
        /// next throw. That is also what stops a thrown die knocking a kept one over.
        /// </summary>
        void LiftHeldDice()
        {
            int slot = 0;
            for (int i = 0; i < dieViews.Count; i++)
            {
                if (!dieViews[i].Held) continue;
                int showing = dieViews[i].Value;
                dieViews[i].Park(RailPosition(slot++));
                dieViews[i].transform.rotation = PippedDie.RotationShowing(showing);
            }
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || IsFinished) return;

            if (cupHeld)
            {
                SwirlCup();
                if (WasPressedThisFrame()) Pour();
                return;
            }

            if (thinking != null || rolling != null) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!WasPressedThisFrame()) return;

            // Picking the cup up is how you roll by hand.
            if (CanRoll() && CupUnderPointer())
            {
                PickUpCup();
                return;
            }

            if (!rolledThisTurn || rollsUsed >= RollsPerTurn) return;

            YahtzeeDie die = DieUnderPointer();
            if (die == null) return;

            die.SetHeld(!die.Held);
            LiftHeldDice();
            Raise();
        }

        void PickUpCup()
        {
            LoadCup();
            cupHeld = true;

            // Straight over the tray, not merely lifted from where it was resting. The cup
            // sits beside the tray, so a player who picks it up and clicks again without
            // moving the mouse would otherwise tip five dice onto the floor.
            Vector3 over = TrayCentre() + new Vector3(0f, CarryHeight, 0f);
            cup.position = over;
            cupTarget = over;
            lastCupPosition = over;
            swirl = 0f;

            if (rattleSource != null)
            {
                rattleSource.volume = 0f;
                rattleSource.Play();
            }
            Raise();
        }

        /// <summary>
        /// Follows the pointer while the cup is held, and turns how fast it is being moved
        /// into how loudly the dice rattle. A cup that made the same noise however it was
        /// moved would be worse than a silent one.
        /// </summary>
        void SwirlCup()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return;

            // Move on a plane at the height the cup is carried at.
            Ray ray = targetCamera.ScreenPointToRay(pointer);
            var plane = new Plane(Vector3.up, new Vector3(0f, CarryHeight, 0f));
            if (plane.Raycast(ray, out float distance))
            {
                Vector3 wanted = ray.GetPoint(distance);
                // Kept over the tray, so the dice cannot be poured onto the floor.
                Vector3 centre = TrayCentre();
                wanted.x = Mathf.Clamp(wanted.x, centre.x - TrayWidth * 0.4f, centre.x + TrayWidth * 0.4f);
                wanted.z = Mathf.Clamp(wanted.z, centre.z - TrayDepth * 0.4f, centre.z + TrayDepth * 0.4f);
                wanted.y = CarryHeight;
                cupTarget = wanted;
            }

            cup.position = Vector3.Lerp(cup.position, cupTarget, 1f - Mathf.Exp(-14f * Time.deltaTime));

            // Tilt into the movement, the way a hand carrying a cup would.
            Vector3 travel = cup.position - lastCupPosition;
            float speed = travel.magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            lastCupPosition = cup.position;

            swirl = Mathf.Lerp(swirl, Mathf.Clamp01(speed / 6f), 1f - Mathf.Exp(-9f * Time.deltaTime));

            Vector3 lean = new Vector3(travel.z, 0f, -travel.x) * 26f;
            cup.rotation = Quaternion.Slerp(cup.rotation,
                Quaternion.Euler(Mathf.Clamp(lean.x, -22f, 22f), 0f, Mathf.Clamp(lean.z, -22f, 22f)),
                1f - Mathf.Exp(-8f * Time.deltaTime));

            if (rattleSource != null)
            {
                rattleSource.volume = Mathf.Clamp01(swirl) * 0.85f;
                rattleSource.pitch = 0.85f + swirl * 0.5f;
            }
        }

        void Pour()
        {
            cupHeld = false;
            if (rattleSource != null) rattleSource.Stop();

            rollsUsed++;
            rolledThisTurn = true;
            countedThisRoll = true;
            Raise();
            RefreshCard();

            rolling = StartCoroutine(PourFromHand());
        }

        bool CupUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || cup == null) return false;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return false;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            return Physics.Raycast(ray, out RaycastHit hit, 200f)
                && hit.collider.transform.IsChildOf(cup);
        }

        static bool WasPressedThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        YahtzeeDie DieUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return null;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return null;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            return Physics.Raycast(ray, out RaycastHit hit, 200f)
                ? hit.collider.GetComponentInParent<YahtzeeDie>()
                : null;
        }

        // Presentation --------------------------------------------------------------

        static Vector3 TrayCentre() => new Vector3(-1.5f, 0f, 0f);

        /// <summary>
        /// The cup waits to the left of the tray. The scorecard occupies the right of the
        /// screen, and a cup over there is simply behind it.
        /// </summary>
        Vector3 CupRest() =>
            new Vector3(TrayCentre().x - TrayWidth * 0.5f - 0.75f, 0.5f, -0.4f);

        static Vector3 DieRestPosition(int index) =>
            TrayCentre() + new Vector3(-1.3f + index * 0.65f, 0.2f, 0.4f);

        static Vector3 RailPosition(int slot) =>
            TrayCentre() + new Vector3(-1.3f + slot * 0.65f, 0.2f, -TrayDepth * 0.5f - 0.5f);

        void BuildTable()
        {
            DestroyDice();
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Yahtzee Table");
            go.transform.SetParent(transform, false);
            root = go.transform;

            Vector3 centre = TrayCentre();

            // The tray: a felt floor inside four walls, which is what the dice bounce off.
            AddBox("Felt", centre + new Vector3(0f, -0.06f, 0f),
                new Vector3(TrayWidth, 0.12f, TrayDepth), TrayFelt, true, 0.04f);

            float halfW = TrayWidth * 0.5f;
            float halfD = TrayDepth * 0.5f;
            AddBox("Wall_Left", centre + new Vector3(-halfW - 0.12f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.24f, WallHeight, TrayDepth + 0.48f), TrayWall, true, 0.08f);
            AddBox("Wall_Right", centre + new Vector3(halfW + 0.12f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.24f, WallHeight, TrayDepth + 0.48f), TrayWall, true, 0.08f);
            AddBox("Wall_Far", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, halfD + 0.12f),
                new Vector3(TrayWidth, WallHeight, 0.24f), TrayWall, true, 0.08f);
            AddBox("Wall_Near", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, -halfD - 0.12f),
                new Vector3(TrayWidth, WallHeight, 0.24f), TrayWall, true, 0.08f);

            // The rail behind the tray, where kept dice sit out of the throw.
            AddBox("Rail", centre + new Vector3(0f, -0.06f, -halfD - 0.5f),
                new Vector3(TrayWidth, 0.12f, 0.68f), TrayWall, false, 0.05f);

            // Invisible walls well above the wooden ones. A die thrown hard clears a rail
            // it can bounce as high as, and a die that leaves the tray is gone: it lands on
            // the floor showing a number nobody can see and the roll is lost.
            const float containment = 2.4f;
            AddContainment("Contain_Left", centre + new Vector3(-halfW - 0.12f, containment * 0.4f, 0f),
                new Vector3(0.24f, containment, TrayDepth + 0.48f));
            AddContainment("Contain_Right", centre + new Vector3(halfW + 0.12f, containment * 0.4f, 0f),
                new Vector3(0.24f, containment, TrayDepth + 0.48f));
            AddContainment("Contain_Far", centre + new Vector3(0f, containment * 0.4f, halfD + 0.12f),
                new Vector3(TrayWidth, containment, 0.24f));
            AddContainment("Contain_Near", centre + new Vector3(0f, containment * 0.4f, -halfD - 0.12f),
                new Vector3(TrayWidth, containment, 0.24f));

            impactSource = ArcadeAudio.AddSource(go, ArcadeAudio.Knock(), false);
            impactSource.volume = 1f;

            BuildCup();

            for (int i = 0; i < DiceCount; i++)
            {
                YahtzeeDie die = YahtzeeDie.Create(root, DieColour, PipColour, HeldColour);
                die.Struck += OnDieStruck;
                die.Park(DieRestPosition(i));
                dieViews.Add(die);
            }
            ShowParkedDice();
        }

        /// <summary>
        /// The cup, built as a ring of staves around an open mouth. A solid cylinder would
        /// read as a mug; the gap down the middle is what makes it look like something the
        /// dice come out of.
        /// </summary>
        void BuildCup()
        {
            var go = new GameObject("Cup");
            go.transform.SetParent(root, false);
            cup = go.transform;
            cup.position = CupRest();

            var shell = new GameObject("Shell");
            shell.transform.SetParent(cup, false);
            shell.AddComponent<MeshFilter>().sharedMesh =
                ArcadeMeshes.Tube(CupRadius, CupRadius - 0.055f, CupHeight, 44);
            shell.AddComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(CupColour, 0.35f);

            // A capsule stood in for the cup would be picked at the wrong place; this
            // matches what the player sees closely enough to click.
            var picker = cup.gameObject.AddComponent<CapsuleCollider>();
            picker.radius = CupRadius;
            picker.height = CupHeight;
            picker.direction = 1;

            // A trigger, not a solid body. This exists only so the cup can be clicked, and
            // raycasts still find triggers; leaving it solid means a die released at the
            // mouth is overlapping it the instant it stops being kinematic, and the solver
            // ejects it whichever way is cheapest, which is often out through the base.
            picker.isTrigger = true;

            // Loud enough to hear over the dice, quiet enough not to be the whole scene.
            rattleSource = ArcadeAudio.AddSource(cup.gameObject, ArcadeAudio.Rattle(), true);
        }

        /// <summary>A collider with nothing to look at, purely to keep the dice in.</summary>
        void AddContainment(string name, Vector3 localPosition, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPosition;

            // Unity's built in Ignore Raycast layer. These walls are taller than everything
            // else and stand between the camera and the table, so while they are raycastable
            // they swallow every click meant for the cup or a die. The layer only affects
            // queries: the collision matrix is untouched, so dice still bounce off them.
            go.layer = IgnoreRaycastLayer;
            go.AddComponent<BoxCollider>().size = size;
        }

        /// <summary>
        /// Clears the dice out by hand rather than relying on the table taking them with
        /// it. A thrown die is reparented, and anything that has left the table root is
        /// invisible to destroying the table.
        /// </summary>
        void DestroyDice()
        {
            foreach (YahtzeeDie die in dieViews)
            {
                if (die == null) continue;
                die.Struck -= OnDieStruck;
                Destroy(die.gameObject);
            }
            dieViews.Clear();
            loaded.Clear();
        }

        /// <summary>A die landing. Louder for a harder knock, so a clatter reads as one.</summary>
        void OnDieStruck(float force)
        {
            if (impactSource == null) return;

            impactSource.pitch = Random.Range(0.86f, 1.18f);
            impactSource.PlayOneShot(impactSource.clip, Mathf.Clamp01(force / 5f) * 0.55f);
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour,
            bool collide, float bevel)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = localPosition;
            // The mesh carries the real size so the bevel stays the same width on every
            // face; scaling a unit cube would stretch it.
            box.transform.localScale = Vector3.one;
            ArcadeMeshes.ApplyMesh(box, ArcadeMeshes.RoundedBox(scale, bevel, 5));

            if (collide) box.GetComponent<BoxCollider>().size = scale;
            else Destroy(box.GetComponent<Collider>());

            box.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(colour, 0.2f);
        }

        // Scorecard -----------------------------------------------------------------

        void BuildCard()
        {
            VisualElement rootElement = document.rootVisualElement;
            if (rootElement == null) return;

            rootElement.Clear();
            rootElement.pickingMode = PickingMode.Ignore;

            panel = ArcadeTheme.Panel(12f);
            panel.style.position = Position.Absolute;
            panel.style.right = 22f;
            panel.style.top = 70f;
            panel.style.minWidth = 300f;
            rootElement.Add(panel);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.marginBottom = 4f;
            panel.Add(header);
            header.Add(Cell(ArcadeTheme.Heading("Box", 12f), 150f));
            header.Add(Cell(ArcadeTheme.Heading("P1", 12f), 66f));
            header.Add(Cell(ArcadeTheme.Heading("P2", 12f), 66f));

            for (int i = 0; i < YahtzeeScorecard.CategoryCount; i++)
            {
                var category = (YahtzeeCategory)i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;
                row.style.marginBottom = 1f;
                panel.Add(row);

                row.Add(Cell(ArcadeTheme.Body(YahtzeeScorecard.NameOf(category), 12f), 150f));

                for (int player = 0; player < 2; player++)
                {
                    int captured = player;
                    YahtzeeCategory capturedCategory = category;
                    Button button = ArcadeTheme.MakeButton(string.Empty,
                        () => OnBoxClicked(captured, capturedCategory), 62f);
                    button.style.marginTop = 0f;
                    button.style.paddingTop = 2f;
                    button.style.paddingBottom = 2f;
                    button.style.fontSize = 12f;
                    boxButtons[player][category] = button;
                    row.Add(Cell(button, 66f));
                }
            }

            var totals = new VisualElement();
            totals.style.flexDirection = FlexDirection.Row;
            totals.style.marginTop = 8f;
            panel.Add(totals);
            totals.Add(Cell(ArcadeTheme.Heading("Total", 13f), 150f));
            for (int player = 0; player < 2; player++)
            {
                totalLabels[player] = ArcadeTheme.Body("0", 14f);
                totalLabels[player].style.unityFontStyleAndWeight = FontStyle.Bold;
                totals.Add(Cell(totalLabels[player], 66f));
            }

            rollButton = ArcadeTheme.MakeButton("Roll", Roll, 150f);
            rollButton.style.marginTop = 12f;
            panel.Add(rollButton);
        }

        static VisualElement Cell(VisualElement content, float width)
        {
            var cell = new VisualElement();
            cell.style.width = width;
            cell.Add(content);
            return cell;
        }

        void OnBoxClicked(int player, YahtzeeCategory category)
        {
            if (IsFinished || thinking != null || rolling != null) return;
            if (player != seat) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!rolledThisTurn) return;
            if (cards[player].IsFilled(category)) return;

            Score(category, true);
        }

        void RefreshCard()
        {
            if (panel == null) return;

            bool localTurn = !IsFinished && thinking == null && rolling == null
                && LocalControls(FirstSeatToPlay);

            for (int player = 0; player < 2; player++)
            {
                foreach (var pair in boxButtons[player])
                {
                    Button button = pair.Value;
                    int? filled = cards[player][pair.Key];

                    if (filled.HasValue)
                    {
                        button.text = filled.Value.ToString();
                        button.style.color = ArcadeTheme.TextBright;
                        button.SetEnabled(false);
                        continue;
                    }

                    bool offer = localTurn && player == seat && rolledThisTurn;
                    button.text = offer
                        ? YahtzeeScorecard.ScoreFor(pair.Key, dice, cards[player]).ToString()
                        : string.Empty;
                    button.style.color = ArcadeTheme.TextDim;
                    button.SetEnabled(offer);
                }

                totalLabels[player].text = cards[player].Total.ToString();
            }

            rollButton.SetEnabled(localTurn && rollsUsed < RollsPerTurn);
            rollButton.text = rollsUsed == 0
                ? "Roll"
                : "Roll again (" + (RollsPerTurn - rollsUsed) + " left)";
        }
    }
}
