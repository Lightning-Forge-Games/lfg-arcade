using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Backgammon;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightningForge.Arcade.Game.Backgammon
{
    /// <summary>
    /// Backgammon, played a checker at a time.
    ///
    /// The player builds a turn move by move, but every move offered is one that leads to a
    /// complete legal turn. That is what enforces the rule about using as many dice as you
    /// can without ever telling the player off: a move that would strand a die is simply
    /// never offered, so it is impossible to paint yourself into a corner.
    ///
    /// The dice are rolled by whoever is to move and travel with their turn, so both
    /// clients see the same roll without needing a server to arbitrate. Both sides run the
    /// same rules, so an illegal turn would be rejected on arrival.
    /// </summary>
    public class BackgammonGame : ArcadeGame
    {
        const float PointWidth = 1f;
        const float PointLength = 4.6f;
        const float BarX = -0.5f;
        const float CheckerRadius = 0.42f;
        const float CheckerHeight = 0.16f;

        static readonly Color BoardColour = new Color(0.16f, 0.11f, 0.08f);
        static readonly Color RailColour = new Color(0.10f, 0.07f, 0.05f);
        static readonly Color LightPoint = new Color(0.60f, 0.50f, 0.38f);
        static readonly Color DarkPoint = new Color(0.34f, 0.20f, 0.15f);
        static readonly Color WhiteChecker = new Color(0.87f, 0.84f, 0.77f);
        static readonly Color BlackChecker = new Color(0.13f, 0.12f, 0.12f);
        static readonly Color Highlight = new Color(0.30f, 0.75f, 0.50f);
        static readonly Color Selected = new Color(0.90f, 0.72f, 0.25f);

        readonly BackgammonBoard board = new BackgammonBoard();
        readonly List<BackgammonMove> played = new List<BackgammonMove>();
        readonly List<List<BackgammonMove>> legalTurns = new List<List<BackgammonMove>>();
        readonly List<GameObject> checkers = new List<GameObject>();
        readonly Dictionary<int, GameObject> pointMarkers = new Dictionary<int, GameObject>();

        System.Random dice;
        BackgammonPlayer opponent;
        Transform root;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine thinking;

        int firstDie;
        int secondDie;
        int selectedPoint = int.MinValue;
        bool rolled;

        public override ArcadeGameId Id => ArcadeGameId.Backgammon;

        public override bool IsFinished => BackgammonBoard.IsOver(board.Status);

        public override string DebugState => board.ToString();

        public override string StatusText
        {
            get
            {
                switch (board.Status)
                {
                    case BackgammonStatus.WhiteWins: return "White wins";
                    case BackgammonStatus.BlackWins: return "Black wins";
                }

                string side = board.SideToMove == BackgammonSide.White ? "White" : "Black";
                if (!rolled) return side + " to roll";

                string roll = " rolled " + firstDie + " and " + secondDie;
                if (legalTurns.Count == 1 && legalTurns[0].Count == 0)
                {
                    return side + roll + ", nothing to play";
                }

                int remaining = legalTurns.Count > 0 ? legalTurns[0].Count - played.Count : 0;
                return side + roll + ", " + remaining + " to play";
            }
        }

        bool WhiteToMove => board.SideToMove == BackgammonSide.White;

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
        }

        protected override void OnBegin()
        {
            dice = new System.Random();
            opponent = new BackgammonPlayer(new System.Random());

            board.Reset();
            played.Clear();
            legalTurns.Clear();
            rolled = false;
            selectedPoint = int.MinValue;

            BuildBoard();
            RefreshCheckers();

            if (cameraRig != null)
            {
                cameraRig.OverrideFraming(new BoardFraming
                {
                    // A backgammon board is much deeper than a chess board, so it needs a
                    // steeper, further view to get both rows of points on screen at once.
                    Focus = Vector3.zero,
                    Height = 15f,
                    Distance = 10.5f,
                    Pitch = 56f,
                    Fov = 42f,
                    HalfExtent = 8.2f,
                });
            }

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
            checkers.Clear();
            pointMarkers.Clear();
        }

        public override void Restart()
        {
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
            board.Reset();
            played.Clear();
            legalTurns.Clear();
            rolled = false;
            RefreshCheckers();
            Raise();
            BeginTurn();
        }

        // Turn flow -----------------------------------------------------------------

        void BeginTurn()
        {
            played.Clear();
            selectedPoint = int.MinValue;
            rolled = false;
            Raise();

            if (IsFinished) return;

            // The local player rolls for themselves; the opponent rolls when it thinks.
            if (Setup.Mode == GameMode.SinglePlayer && !LocalControls(WhiteToMove))
            {
                thinking = StartCoroutine(OpponentTurn());
                return;
            }

            RollFor(dice.Next(1, 7), dice.Next(1, 7));
        }

        void RollFor(int a, int b)
        {
            firstDie = a;
            secondDie = b;
            rolled = true;

            legalTurns.Clear();
            legalTurns.AddRange(board.GenerateTurns(a, b));

            Raise();
            HighlightMovable();

            // A roll with nothing playable passes straight over.
            if (legalTurns.Count == 1 && legalTurns[0].Count == 0)
            {
                StartCoroutine(PassAfterAPause());
            }
        }

        IEnumerator PassAfterAPause()
        {
            // Long enough to read what happened before the board changes hands.
            yield return new WaitForSeconds(1.1f);
            CompleteTurn();
        }

        IEnumerator OpponentTurn()
        {
            yield return new WaitForSeconds(0.4f);

            int a = dice.Next(1, 7);
            int b = dice.Next(1, 7);
            firstDie = a;
            secondDie = b;
            rolled = true;
            legalTurns.Clear();
            legalTurns.AddRange(board.GenerateTurns(a, b));
            Raise();

            List<BackgammonMove> turn = opponent.ChooseTurn(board, a, b, Setup.Difficulty);
            thinking = null;

            foreach (BackgammonMove move in turn)
            {
                yield return new WaitForSeconds(0.35f);
                board.ApplyMove(move);
                played.Add(move);
                RefreshCheckers();
                Raise();
            }

            yield return new WaitForSeconds(0.35f);
            CompleteTurn();
        }

        /// <summary>
        /// The moves that may be played next: those that extend what has been played so far
        /// into some complete legal turn. Anything that would waste a die never appears.
        /// </summary>
        List<BackgammonMove> AvailableMoves()
        {
            var available = new List<BackgammonMove>();
            var seen = new HashSet<string>();

            foreach (List<BackgammonMove> turn in legalTurns)
            {
                if (turn.Count <= played.Count) continue;
                if (!StartsWithPlayed(turn)) continue;

                BackgammonMove next = turn[played.Count];
                string key = next.From + ">" + next.To;
                if (seen.Add(key)) available.Add(next);
            }
            return available;
        }

        bool StartsWithPlayed(List<BackgammonMove> turn)
        {
            for (int i = 0; i < played.Count; i++)
            {
                if (turn[i].From != played[i].From || turn[i].To != played[i].To) return false;
            }
            return true;
        }

        bool TurnComplete()
        {
            foreach (List<BackgammonMove> turn in legalTurns)
            {
                if (StartsWithPlayed(turn) && turn.Count > played.Count) return false;
            }
            return true;
        }

        void CompleteTurn()
        {
            // The moves are already on the board; this only hands over the turn.
            if (!BackgammonBoard.IsOver(board.Status))
            {
                board.SideToMove = BackgammonBoard.Opponent(board.SideToMove);
            }

            if (played.Count > 0 || legalTurns.Count > 0) RaisePlayedTurn();

            ClearHighlights();
            if (IsFinished)
            {
                Raise();
                return;
            }

            BeginTurn();
        }

        void RaisePlayedTurn()
        {
            // Only the local player's turns go out; ones arriving from the opponent are
            // replayed through ApplyRemoteMove and must not be echoed back.
            if (Setup.Mode != GameMode.Online) return;
            if (!wasLocalTurn) return;

            var sb = new System.Text.StringBuilder();
            sb.Append(firstDie).Append(secondDie).Append(':');
            for (int i = 0; i < played.Count; i++)
            {
                if (i > 0) sb.Append(';');
                sb.Append(played[i].From).Append('-').Append(played[i].To);
            }
            RaiseMovePlayed(sb.ToString());
        }

        bool wasLocalTurn;

        public override bool ApplyRemoteMove(string encoded)
        {
            int colon = encoded.IndexOf(':');
            if (colon < 2) return false;

            if (!int.TryParse(encoded.Substring(0, 1), out int a)) return false;
            if (!int.TryParse(encoded.Substring(1, 1), out int b)) return false;

            firstDie = a;
            secondDie = b;
            rolled = true;
            legalTurns.Clear();
            legalTurns.AddRange(board.GenerateTurns(a, b));
            played.Clear();

            string body = encoded.Substring(colon + 1);
            if (body.Length > 0)
            {
                foreach (string part in body.Split(';'))
                {
                    string[] ends = part.Split('-');
                    if (ends.Length != 2) return false;
                    if (!int.TryParse(ends[0], out int from) || !int.TryParse(ends[1], out int to))
                        return false;

                    bool matched = false;
                    foreach (BackgammonMove candidate in AvailableMoves())
                    {
                        if (candidate.From != from || candidate.To != to) continue;
                        board.ApplyMove(candidate);
                        played.Add(candidate);
                        matched = true;
                        break;
                    }
                    if (!matched) return false;
                }
            }

            RefreshCheckers();
            wasLocalTurn = false;
            CompleteTurn();
            return true;
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || IsFinished || !rolled) return;
            if (thinking != null) return;
            if (!LocalControls(WhiteToMove)) return;
            if (!WasPressedThisFrame()) return;

            int point = PointUnderPointer();
            if (point == int.MinValue) return;

            HandlePick(point);
        }

        static bool WasPressedThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        int PointUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return int.MinValue;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return int.MinValue;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return int.MinValue;

            var marker = hit.collider.GetComponent<BackgammonPointPicker>();
            return marker != null ? marker.Point : int.MinValue;
        }

        void HandlePick(int point)
        {
            List<BackgammonMove> available = AvailableMoves();

            // Second click: a destination for the checker already chosen.
            if (selectedPoint != int.MinValue)
            {
                foreach (BackgammonMove move in available)
                {
                    if (move.From != selectedPoint || move.To != point) continue;
                    PlayLocal(move);
                    return;
                }
            }

            // First click: a checker with somewhere to go.
            foreach (BackgammonMove move in available)
            {
                if (move.From != point) continue;
                selectedPoint = point;
                HighlightFor(point, available);
                return;
            }

            selectedPoint = int.MinValue;
            HighlightMovable();
        }

        void PlayLocal(BackgammonMove move)
        {
            wasLocalTurn = true;
            board.ApplyMove(move);
            played.Add(move);
            selectedPoint = int.MinValue;

            RefreshCheckers();
            Raise();

            if (TurnComplete()) CompleteTurn();
            else HighlightMovable();
        }

        // Presentation --------------------------------------------------------------

        void BuildBoard()
        {
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Backgammon Board");
            go.transform.SetParent(transform, false);
            root = go.transform;

            AddBox("Table", new Vector3(0f, -0.16f, 0f), new Vector3(15.4f, 0.3f, 11.6f), BoardColour);
            AddBox("Bar", new Vector3(BarX, -0.02f, 0f), new Vector3(1.1f, 0.12f, 11.2f), RailColour);
            AddBox("Rail_Left", new Vector3(-7.4f, 0.02f, 0f), new Vector3(0.6f, 0.3f, 11.6f), RailColour);
            AddBox("Rail_Right", new Vector3(7.4f, 0.02f, 0f), new Vector3(0.6f, 0.3f, 11.6f), RailColour);

            for (int point = 0; point < BackgammonBoard.Points; point++) BuildPoint(point);
        }

        void BuildPoint(int point)
        {
            bool near = point < 12;
            float x = PointX(point);
            float baseZ = near ? -5.4f : 5.4f;
            float tipZ = near ? -5.4f + PointLength : 5.4f - PointLength;

            var mesh = new Mesh { name = "Point" + point };
            mesh.vertices = new[]
            {
                new Vector3(x - PointWidth * 0.5f, 0f, baseZ),
                new Vector3(x + PointWidth * 0.5f, 0f, baseZ),
                new Vector3(x, 0f, tipZ),
            };
            // Wound so the face points up for the near row and the far row alike.
            mesh.triangles = near ? new[] { 0, 2, 1 } : new[] { 0, 1, 2 };
            mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up };

            var go = new GameObject("Point" + point);
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(point % 2 == 0 ? DarkPoint : LightPoint, 0.2f);

            // A flat box over the triangle, because a one sided triangle is awkward to hit
            // and the whole column should be clickable, not just the wedge.
            var picker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            picker.name = "Pick" + point;
            picker.transform.SetParent(root, false);
            picker.transform.localPosition =
                new Vector3(x, 0.35f, (baseZ + tipZ) * 0.5f);
            picker.transform.localScale = new Vector3(PointWidth, 0.7f, PointLength);
            Destroy(picker.GetComponent<MeshRenderer>());
            picker.AddComponent<BackgammonPointPicker>().Point = point;

            pointMarkers[point] = go;
        }

        static float PointX(int point)
        {
            if (point < 12)
            {
                // Near row runs left to right, with a gap for the bar in the middle.
                return -6.5f + point + (point >= 6 ? 1f : 0f);
            }

            // Far row runs back the other way, so the path around the board is continuous.
            int i = point - 12;
            return 5.5f - i - (i >= 6 ? 1f : 0f);
        }

        Vector3 CheckerPosition(int point, int index)
        {
            if (point == BackgammonMove.Bar)
            {
                return new Vector3(BarX, CheckerHeight * 0.5f + index * CheckerHeight, 0f);
            }

            bool near = point < 12;
            float x = PointX(point);
            float baseZ = near ? -5.1f : 5.1f;
            float step = near ? 0.78f : -0.78f;

            // Checkers past the fifth stack on top rather than running off the point.
            int along = Mathf.Min(index, 4);
            int layer = index / 5;
            return new Vector3(x, CheckerHeight * 0.5f + layer * CheckerHeight, baseZ + along * step);
        }

        void RefreshCheckers()
        {
            foreach (GameObject checker in checkers)
            {
                if (checker != null) Destroy(checker);
            }
            checkers.Clear();

            for (int point = 0; point < BackgammonBoard.Points; point++)
            {
                BackgammonPoint p = board[point];
                for (int i = 0; i < p.Count; i++) MakeChecker(p.Side, CheckerPosition(point, i));
            }

            for (int i = 0; i < board.Bar(BackgammonSide.White); i++)
            {
                MakeChecker(BackgammonSide.White, new Vector3(BarX, CheckerHeight * 0.5f + i * CheckerHeight, -2.2f));
            }
            for (int i = 0; i < board.Bar(BackgammonSide.Black); i++)
            {
                MakeChecker(BackgammonSide.Black, new Vector3(BarX, CheckerHeight * 0.5f + i * CheckerHeight, 2.2f));
            }

            // Borne off checkers stack on the right hand rail, so progress is visible.
            for (int i = 0; i < board.Off(BackgammonSide.White); i++)
            {
                MakeChecker(BackgammonSide.White, new Vector3(6.9f, 0.2f + i * CheckerHeight, -3f));
            }
            for (int i = 0; i < board.Off(BackgammonSide.Black); i++)
            {
                MakeChecker(BackgammonSide.Black, new Vector3(6.9f, 0.2f + i * CheckerHeight, 3f));
            }
        }

        void MakeChecker(BackgammonSide side, Vector3 localPosition)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = side + "Checker";
            go.transform.SetParent(root, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = new Vector3(CheckerRadius * 2f, CheckerHeight * 0.5f, CheckerRadius * 2f);
            Destroy(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(
                side == BackgammonSide.White ? WhiteChecker : BlackChecker, 0.4f);
            checkers.Add(go);
        }

        void HighlightMovable()
        {
            ClearHighlights();
            foreach (BackgammonMove move in AvailableMoves())
            {
                if (move.From == BackgammonMove.Bar) continue;
                Tint(move.From, Highlight, 0.7f);
            }
        }

        void HighlightFor(int from, List<BackgammonMove> available)
        {
            ClearHighlights();
            Tint(from, Selected, 1.1f);
            foreach (BackgammonMove move in available)
            {
                if (move.From != from || move.To == BackgammonMove.Off) continue;
                Tint(move.To, Highlight, 0.9f);
            }
        }

        void Tint(int point, Color colour, float strength)
        {
            if (!pointMarkers.TryGetValue(point, out GameObject go) || go == null) return;
            go.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Emissive(colour, strength);
        }

        void ClearHighlights()
        {
            foreach (var pair in pointMarkers)
            {
                if (pair.Value == null) continue;
                pair.Value.GetComponent<MeshRenderer>().sharedMaterial =
                    ArcadeMaterials.Get(pair.Key % 2 == 0 ? DarkPoint : LightPoint, 0.2f);
            }
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(root, false);
            box.transform.localPosition = localPosition;
            box.transform.localScale = scale;
            Destroy(box.GetComponent<Collider>());
            box.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(colour, 0.25f);
        }
    }

    /// <summary>Marks a point's pick volume so a raycast can name the point it hit.</summary>
    public class BackgammonPointPicker : MonoBehaviour
    {
        public int Point;
    }
}
