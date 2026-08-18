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
    /// Yahtzee: five dice on a table, and a scorecard beside them.
    ///
    /// The dice are the one part that wants to be physical, so they are real objects you
    /// click to hold. The card is a table of thirteen numbers and would be worse in 3D, so
    /// it is UI. Both cards are on screen at once, because half the game is watching which
    /// boxes your opponent has left.
    ///
    /// Every open box shows what the current dice would score in it, including the zeroes.
    /// Taking a zero is often the right move and the card should not hide that.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class YahtzeeGame : ArcadeGame
    {
        const int DiceCount = 5;
        const int RollsPerTurn = 3;

        static readonly Color TableColour = new Color(0.17f, 0.13f, 0.10f);
        static readonly Color DieColour = new Color(0.90f, 0.88f, 0.82f);
        static readonly Color PipColour = new Color(0.12f, 0.11f, 0.11f);
        static readonly Color HeldColour = new Color(0.85f, 0.66f, 0.28f);

        readonly YahtzeeScorecard[] cards =
        {
            new YahtzeeScorecard(),
            new YahtzeeScorecard(),
        };

        readonly int[] dice = new int[DiceCount];
        readonly List<YahtzeeDie> dieViews = new List<YahtzeeDie>(DiceCount);

        System.Random random;
        YahtzeePlayer opponent;
        Transform root;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine thinking;
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

        /// <summary>Both cards full, so both players have had thirteen turns.</summary>
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
                if (!rolledThisTurn) return who + " to roll";
                if (rollsUsed >= RollsPerTurn) return who + ", pick a box";
                return who + ", roll " + rollsUsed + " of " + RollsPerTurn;
            }
        }

        /// <summary>Seat 0 is the first player, which maps onto White everywhere else.</summary>
        bool FirstSeatToPlay => seat == 0;

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
            document = GetComponent<UIDocument>();
        }

        protected override void OnBegin()
        {
            random = new System.Random();
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
                    // Close in on the dice, and steep, because the number that matters is
                    // the one on top and a low angle hides it behind the near face.
                    Focus = new Vector3(-1.6f, 0f, 0f),
                    Height = 7.4f,
                    Distance = 3.6f,
                    Pitch = 64f,
                    Fov = 40f,
                    HalfExtent = 4.2f,
                });
            }

            RefreshCard();
            BeginTurn();
        }

        public override void End()
        {
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
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

        // Turn flow -----------------------------------------------------------------

        void BeginTurn()
        {
            rollsUsed = 0;
            rolledThisTurn = false;
            foreach (YahtzeeDie die in dieViews) die.SetHeld(false, DieColour, HeldColour);

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
            if (rollsUsed >= RollsPerTurn) return;

            for (int i = 0; i < DiceCount; i++)
            {
                if (dieViews.Count > i && dieViews[i].Held) continue;
                dice[i] = random.Next(1, 7);
            }

            rollsUsed++;
            rolledThisTurn = true;
            ShowDice();
            Raise();
            RefreshCard();
        }

        /// <summary>
        /// Writes the dice into a box and hands over. The whole turn goes out as one
        /// message, since the rolls in between are not something the opponent acts on.
        /// </summary>
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

            ShowDice();
            var category = (YahtzeeCategory)categoryIndex;
            if (cards[seat].IsFilled(category)) return false;

            Score(category, false);
            return true;
        }

        IEnumerator OpponentTurn()
        {
            yield return new WaitForSeconds(0.5f);

            Roll();
            for (int reroll = 0; reroll < RollsPerTurn - 1; reroll++)
            {
                yield return new WaitForSeconds(0.7f);

                bool[] keep = opponent.ChooseKeeps(dice, cards[seat], Setup.Difficulty);
                for (int i = 0; i < DiceCount && i < dieViews.Count; i++)
                {
                    dieViews[i].SetHeld(keep[i], DieColour, HeldColour);
                }
                yield return new WaitForSeconds(0.35f);
                Roll();
            }

            yield return new WaitForSeconds(0.7f);
            YahtzeeCategory choice = opponent.ChooseCategory(dice, cards[seat], Setup.Difficulty);
            thinking = null;
            Score(choice, false);
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || IsFinished || thinking != null) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!rolledThisTurn) return;
            if (!WasPressedThisFrame()) return;

            YahtzeeDie die = DieUnderPointer();
            if (die == null) return;

            // Holding only means anything while a reroll is still coming.
            if (rollsUsed >= RollsPerTurn) return;
            die.SetHeld(!die.Held, DieColour, HeldColour);
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

        void BuildTable()
        {
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Yahtzee Table");
            go.transform.SetParent(transform, false);
            root = go.transform;

            var felt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            felt.name = "Felt";
            felt.transform.SetParent(root, false);
            felt.transform.localPosition = new Vector3(-1.6f, -0.05f, 0f);
            felt.transform.localScale = new Vector3(7.4f, 0.2f, 4.2f);
            Destroy(felt.GetComponent<Collider>());
            felt.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(TableColour, 0.15f);

            dieViews.Clear();
            for (int i = 0; i < DiceCount; i++)
            {
                YahtzeeDie die = YahtzeeDie.Create(root, DieColour, PipColour);
                die.transform.localPosition = new Vector3(-4f + i * 1.2f, 0.4f, 0f);
                dieViews.Add(die);
            }
            ShowDice();
        }

        void ShowDice()
        {
            for (int i = 0; i < DiceCount && i < dieViews.Count; i++) dieViews[i].SetValue(dice[i]);
        }

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
            if (IsFinished || thinking != null) return;
            if (player != seat) return;
            if (!LocalControls(FirstSeatToPlay)) return;
            if (!rolledThisTurn) return;
            if (cards[player].IsFilled(category)) return;

            Score(category, true);
        }

        void RefreshCard()
        {
            if (panel == null) return;

            bool localTurn = !IsFinished && thinking == null
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

                    // What this roll would be worth here, including nothing at all.
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
