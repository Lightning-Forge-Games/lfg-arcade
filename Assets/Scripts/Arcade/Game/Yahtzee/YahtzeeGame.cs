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

        const float TrayWidth = 3.4f;
        const float TrayDepth = 2.4f;
        const float WallHeight = 0.42f;

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
                if (rolling != null) return who + " rolling...";
                if (!rolledThisTurn) return who + " to roll";
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

            BuildTable();
            BuildCard();

            if (cameraRig != null)
            {
                cameraRig.OverrideFraming(new BoardFraming
                {
                    // Over the tray and steep, because the number that matters is the one
                    // on top and a low angle hides it behind the near face.
                    Focus = new Vector3(-1.5f, 0f, -0.35f),
                    Height = 6.4f,
                    Distance = 2.9f,
                    Pitch = 66f,
                    Fov = 42f,
                    HalfExtent = 3.6f,
                });
            }

            RefreshCard();
            BeginTurn();
        }

        public override void End()
        {
            StopWork();
            if (cameraRig != null) cameraRig.ClearFramingOverride();
            if (root != null)
            {
                Destroy(root.gameObject);
                root = null;
            }
            dieViews.Clear();
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

        void Roll()
        {
            if (rollsUsed >= RollsPerTurn || rolling != null) return;
            rolling = StartCoroutine(RollRoutine());
        }

        /// <summary>
        /// The throw. The cup lifts, swings over the tray and tips; the dice leave its mouth
        /// with the spin it gave them and are left to settle on their own.
        /// </summary>
        IEnumerator RollRoutine()
        {
            rollsUsed++;
            rolledThisTurn = true;
            Raise();
            RefreshCard();

            var loose = new List<YahtzeeDie>();
            foreach (YahtzeeDie die in dieViews)
            {
                if (!die.Held) loose.Add(die);
            }

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
                    Vector3 mouth = cup.position + cup.up * 0.42f;
                    Vector3 velocity = new Vector3(2.4f, -0.6f, 0f)
                        + new Vector3(Random.Range(-0.5f, 0.5f), 0f, Random.Range(-1.1f, 1.1f));
                    loose[released].Throw(mouth, velocity);
                    released++;
                }
                yield return null;
            }

            // Anything the loop did not get to.
            while (released < loose.Count)
            {
                loose[released].Throw(cup.position + cup.up * 0.42f, new Vector3(2.4f, -0.6f, 0f));
                released++;
            }

            yield return MoveCup(over, rest, tipped, Quaternion.identity, 0.3f);

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

            rolling = null;
            Raise();
            RefreshCard();
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

            Roll();
            while (rolling != null) yield return null;

            for (int reroll = 0; reroll < RollsPerTurn - 1; reroll++)
            {
                yield return new WaitForSeconds(0.5f);

                bool[] keep = opponent.ChooseKeeps(dice, cards[seat], Setup.Difficulty);
                for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dieViews[i].SetHeld(keep[i]);
                LiftHeldDice();

                yield return new WaitForSeconds(0.45f);
                Roll();
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
            if (root == null || IsFinished || thinking != null || rolling != null) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!rolledThisTurn || rollsUsed >= RollsPerTurn) return;
            if (!WasPressedThisFrame()) return;

            YahtzeeDie die = DieUnderPointer();
            if (die == null) return;

            die.SetHeld(!die.Held);
            LiftHeldDice();
            Raise();
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
        Vector3 CupRest() => new Vector3(TrayCentre().x - TrayWidth * 0.5f - 0.8f, 0.55f, -0.5f);

        static Vector3 DieRestPosition(int index) =>
            TrayCentre() + new Vector3(-1.1f + index * 0.55f, 0.24f, 0.35f);

        static Vector3 RailPosition(int slot) =>
            TrayCentre() + new Vector3(-1.15f + slot * 0.56f, 0.24f, -TrayDepth * 0.5f - 0.45f);

        void BuildTable()
        {
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Yahtzee Table");
            go.transform.SetParent(transform, false);
            root = go.transform;

            Vector3 centre = TrayCentre();

            // The tray: a felt floor inside four walls, which is what the dice bounce off.
            AddBox("Felt", centre + new Vector3(0f, -0.06f, 0f),
                new Vector3(TrayWidth, 0.12f, TrayDepth), TrayFelt, true);

            float halfW = TrayWidth * 0.5f;
            float halfD = TrayDepth * 0.5f;
            AddBox("Wall_Left", centre + new Vector3(-halfW - 0.11f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.22f, WallHeight, TrayDepth + 0.44f), TrayWall, true);
            AddBox("Wall_Right", centre + new Vector3(halfW + 0.11f, WallHeight * 0.5f - 0.06f, 0f),
                new Vector3(0.22f, WallHeight, TrayDepth + 0.44f), TrayWall, true);
            AddBox("Wall_Far", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, halfD + 0.11f),
                new Vector3(TrayWidth, WallHeight, 0.22f), TrayWall, true);
            AddBox("Wall_Near", centre + new Vector3(0f, WallHeight * 0.5f - 0.06f, -halfD - 0.11f),
                new Vector3(TrayWidth, WallHeight, 0.22f), TrayWall, true);

            // The rail behind the tray, where kept dice sit out of the throw.
            AddBox("Rail", centre + new Vector3(0f, -0.06f, -halfD - 0.45f),
                new Vector3(TrayWidth, 0.12f, 0.6f), TrayWall, false);

            BuildCup();

            dieViews.Clear();
            for (int i = 0; i < DiceCount; i++)
            {
                YahtzeeDie die = YahtzeeDie.Create(root, DieColour, PipColour, HeldColour);
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

            const int staves = 12;
            const float radius = 0.34f;
            Material material = ArcadeMaterials.Get(CupColour, 0.35f);

            for (int i = 0; i < staves; i++)
            {
                float angle = i * Mathf.PI * 2f / staves;
                var stave = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stave.name = "Stave" + i;
                stave.transform.SetParent(cup, false);
                stave.transform.localPosition =
                    new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                stave.transform.localRotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                stave.transform.localScale = new Vector3(0.07f, 0.86f, 0.2f);
                Destroy(stave.GetComponent<Collider>());
                stave.GetComponent<MeshRenderer>().sharedMaterial = material;
            }

            var baseDisc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            baseDisc.name = "Base";
            baseDisc.transform.SetParent(cup, false);
            baseDisc.transform.localPosition = new Vector3(0f, -0.44f, 0f);
            baseDisc.transform.localScale = new Vector3(radius * 2.1f, 0.04f, radius * 2.1f);
            Destroy(baseDisc.GetComponent<Collider>());
            baseDisc.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour, bool collide)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;
            if (!collide) Destroy(box.GetComponent<Collider>());
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
