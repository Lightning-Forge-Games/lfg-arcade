using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Connect4;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightningForge.Arcade.Game.Connect4
{
    /// <summary>
    /// Connect 4: an upright grid you drop discs into.
    ///
    /// The whole board is built from primitives at runtime rather than from authored art,
    /// so adding the game meant adding scripts and nothing else. The frame is a slab with
    /// the play area cut into it by seven gaps between eight posts, which reads as the real
    /// toy from the front without needing a mesh with holes in it.
    ///
    /// Discs fall rather than appear. It costs a few lines and it is most of why the game
    /// feels like Connect 4 instead of a grid lighting up.
    /// </summary>
    public class Connect4Game : ArcadeGame
    {
        const float Cell = 1f;
        const float DiscRadius = 0.42f;
        const float DiscThickness = 0.18f;
        const float FallSpeed = 11f;

        static readonly Color RedDisc = new Color(0.72f, 0.18f, 0.16f);
        static readonly Color YellowDisc = new Color(0.87f, 0.68f, 0.16f);
        // Deep enough to still read as navy under the scene's key light, which washes
        // mid tones out badly on large flat faces.
        static readonly Color FrameColor = new Color(0.055f, 0.085f, 0.17f);
        static readonly Color PostColor = new Color(0.085f, 0.125f, 0.24f);

        readonly Connect4Board board = new Connect4Board();
        readonly Connect4Search search = new Connect4Search();
        readonly GameObject[] discs = new GameObject[Connect4Board.Cells];
        readonly List<GameObject> columnPickers = new List<GameObject>(Connect4Board.Columns);

        Transform root;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine falling;
        Coroutine thinking;
        int hoveredColumn = -1;
        GameObject hoverGhost;

        public override ArcadeGameId Id => ArcadeGameId.Connect4;

        public override bool IsFinished => Connect4Board.IsOver(board.Status);

        public override string DebugState => board.ToString();

        public override string StatusText
        {
            get
            {
                switch (board.Status)
                {
                    case Connect4Status.RedWins: return "Red wins";
                    case Connect4Status.YellowWins: return "Yellow wins";
                    case Connect4Status.Draw: return "A draw";
                    default:
                        return board.SideToMove == Connect4Player.Red
                            ? "Red to move"
                            : "Yellow to move";
                }
            }
        }

        /// <summary>Red is the first seat, so it maps onto White everywhere else.</summary>
        bool RedToMove => board.SideToMove == Connect4Player.Red;

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
        }

        protected override void OnBegin()
        {
            board.Reset();
            BuildBoard();
            RefreshDiscs();

            if (cameraRig != null)
            {
                // Square on to an upright board, lifted to the middle of the grid rather
                // than the floor it stands on.
                cameraRig.OverrideFraming(new BoardFraming
                {
                    Focus = new Vector3(0f, Connect4Board.Rows * Cell * 0.5f, 0f),
                    Height = 1.4f,
                    Distance = 11.5f,
                    Pitch = 6f,
                    Fov = 42f,
                    HalfExtent = Connect4Board.Columns * Cell * 0.62f,
                });
            }

            MaybeStartThinking();
        }

        public override void End()
        {
            StopAllWork();
            if (cameraRig != null) cameraRig.ClearFramingOverride();
            if (root != null)
            {
                Destroy(root.gameObject);
                root = null;
            }
        }

        public override void Restart()
        {
            StopAllWork();
            board.Reset();
            RefreshDiscs();
            Raise();
            MaybeStartThinking();
        }

        void StopAllWork()
        {
            if (falling != null) { StopCoroutine(falling); falling = null; }
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (root == null || IsFinished) return;

            // Nothing may be dropped while a disc is still in the air, or the board and the
            // animation would disagree about what is where.
            bool busy = falling != null || thinking != null;
            int column = busy ? -1 : ColumnUnderPointer();

            if (column != hoveredColumn)
            {
                hoveredColumn = column;
                UpdateHoverGhost();
            }

            if (busy || !CanActLocally()) return;

            if (WasPressedThisFrame() && hoveredColumn >= 0) TryDrop(hoveredColumn, true);
        }

        bool CanActLocally() => LocalControls(RedToMove);

        static bool WasPressedThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        static bool TryPointerPosition(out Vector2 position)
        {
            Mouse mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                return true;
            }

            Touchscreen touch = Touchscreen.current;
            if (touch != null)
            {
                position = touch.primaryTouch.position.ReadValue();
                return true;
            }

            position = default;
            return false;
        }

        int ColumnUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null || !TryPointerPosition(out Vector2 pointer)) return -1;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            if (!Physics.Raycast(ray, out RaycastHit hit, 200f)) return -1;

            var picker = hit.collider.GetComponent<Connect4ColumnPicker>();
            return picker != null && board.IsPlayable(picker.Column) ? picker.Column : -1;
        }

        // Playing -------------------------------------------------------------------

        bool TryDrop(int column, bool local)
        {
            if (!board.IsPlayable(column)) return false;

            Connect4Player mover = board.SideToMove;
            int row = board.Drop(column);
            if (row < 0) return false;

            if (local) RaiseMovePlayed(column.ToString());

            falling = StartCoroutine(DropDisc(column, row, mover));
            Raise();
            return true;
        }

        public override bool ApplyRemoteMove(string encoded)
        {
            if (!int.TryParse(encoded, out int column)) return false;
            return TryDrop(column, false);
        }

        IEnumerator DropDisc(int column, int row, Connect4Player player)
        {
            GameObject disc = MakeDisc(player);
            discs[Connect4Board.IndexOf(column, row)] = disc;

            Vector3 target = CellPosition(column, row);
            // Starts just above the frame, where a real disc would be released.
            Vector3 from = CellPosition(column, Connect4Board.Rows) + Vector3.up * 0.6f;
            disc.transform.localPosition = from;

            float t = 0f;
            float distance = from.y - target.y;
            while (true)
            {
                t += Time.deltaTime;
                // Constant acceleration, so it lands with some weight behind it.
                float travelled = 0.5f * FallSpeed * t * t;
                if (travelled >= distance) break;

                Vector3 p = from;
                p.y = from.y - travelled;
                disc.transform.localPosition = p;
                yield return null;
            }

            disc.transform.localPosition = target;
            falling = null;

            if (IsFinished) ShowWin();
            Raise();

            MaybeStartThinking();
        }

        void MaybeStartThinking()
        {
            if (IsFinished || Setup.Mode != GameMode.SinglePlayer) return;
            if (CanActLocally()) return;
            if (thinking != null || falling != null) return;

            thinking = StartCoroutine(ThinkAndPlay());
        }

        IEnumerator ThinkAndPlay()
        {
            // A beat before it answers. Instant replies read as the game ignoring you.
            yield return new WaitForSeconds(0.35f);

            int column = search.ChooseColumn(board, Setup.Difficulty, new System.Random());
            thinking = null;

            if (column >= 0) TryDrop(column, false);
        }

        // Presentation --------------------------------------------------------------

        void BuildBoard()
        {
            if (root != null) Destroy(root.gameObject);

            var go = new GameObject("Connect4 Board");
            go.transform.SetParent(transform, false);
            root = go.transform;

            float width = Connect4Board.Columns * Cell;
            float height = Connect4Board.Rows * Cell;

            // Back panel, which the discs sit in front of.
            AddBox("Backboard", new Vector3(0f, height * 0.5f, 0.16f),
                new Vector3(width + 0.7f, height + 0.7f, 0.12f), FrameColor);

            // Eight posts with the seven columns between them.
            for (int i = 0; i <= Connect4Board.Columns; i++)
            {
                float x = (i - Connect4Board.Columns * 0.5f) * Cell;
                AddBox("Post" + i, new Vector3(x, height * 0.5f, 0f),
                    new Vector3(0.16f, height + 0.7f, 0.34f), PostColor);
            }

            // Top and bottom rails, so the grid reads as a single object.
            AddBox("Rail_Bottom", new Vector3(0f, -0.28f, 0f),
                new Vector3(width + 0.7f, 0.36f, 0.34f), PostColor);
            AddBox("Rail_Top", new Vector3(0f, height + 0.28f, 0f),
                new Vector3(width + 0.7f, 0.36f, 0.34f), PostColor);

            // Legs, so it stands on the ground rather than floating.
            AddBox("Leg_Left", new Vector3(-width * 0.35f, -0.95f, 0f),
                new Vector3(0.4f, 1.3f, 0.9f), FrameColor);
            AddBox("Leg_Right", new Vector3(width * 0.35f, -0.95f, 0f),
                new Vector3(0.4f, 1.3f, 0.9f), FrameColor);

            BuildColumnPickers(height);
        }

        /// <summary>
        /// One tall invisible slab per column. Picking a column rather than a cell is what
        /// makes the game feel like the real thing: you choose where to drop, not where the
        /// disc ends up.
        /// </summary>
        void BuildColumnPickers(float height)
        {
            columnPickers.Clear();
            for (int column = 0; column < Connect4Board.Columns; column++)
            {
                var picker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                picker.name = "Column" + column;
                picker.transform.SetParent(root, false);
                picker.transform.localPosition = new Vector3(ColumnX(column), height * 0.5f, -0.05f);
                picker.transform.localScale = new Vector3(Cell * 0.95f, height + 1.2f, 0.5f);

                Destroy(picker.GetComponent<MeshRenderer>());
                picker.AddComponent<Connect4ColumnPicker>().Column = column;
                columnPickers.Add(picker);
            }
        }

        void UpdateHoverGhost()
        {
            if (hoverGhost == null)
            {
                hoverGhost = MakeDisc(Connect4Player.Red);
                hoverGhost.name = "HoverGhost";
                foreach (Collider c in hoverGhost.GetComponentsInChildren<Collider>()) Destroy(c);
            }

            bool show = hoveredColumn >= 0 && !IsFinished && CanActLocally();
            hoverGhost.SetActive(show);
            if (!show) return;

            // Sits just clear of the top rail, in the colour about to be played, and dimmed
            // so it reads as a preview rather than a disc already in the frame. Any higher
            // and it drifts off the top of the screen on a short window.
            Color colour = RedToMove ? RedDisc : YellowDisc;
            hoverGhost.GetComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(colour * 0.55f, 0.35f);
            hoverGhost.transform.localPosition =
                CellPosition(hoveredColumn, Connect4Board.Rows) + Vector3.up * 0.12f;
        }

        void RefreshDiscs()
        {
            for (int i = 0; i < discs.Length; i++)
            {
                if (discs[i] != null) Destroy(discs[i]);
                discs[i] = null;
            }
            if (hoverGhost != null) hoverGhost.SetActive(false);

            for (int column = 0; column < Connect4Board.Columns; column++)
            {
                for (int row = 0; row < Connect4Board.Rows; row++)
                {
                    Connect4Player p = board[column, row];
                    if (p == Connect4Player.None) continue;

                    GameObject disc = MakeDisc(p);
                    disc.transform.localPosition = CellPosition(column, row);
                    discs[Connect4Board.IndexOf(column, row)] = disc;
                }
            }
        }

        /// <summary>Lights the four that won, so the result is visible on the board itself.</summary>
        void ShowWin()
        {
            Color colour = board.Status == Connect4Status.RedWins ? RedDisc : YellowDisc;
            foreach (int index in board.WinningCells)
            {
                GameObject disc = discs[index];
                if (disc == null) continue;
                disc.GetComponent<MeshRenderer>().sharedMaterial =
                    ArcadeMaterials.Emissive(colour, 1.5f);
            }
        }

        GameObject MakeDisc(Connect4Player player)
        {
            var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = player + "Disc";
            disc.transform.SetParent(root, false);
            // A Unity cylinder stands along Y; tipping it onto its side faces it forward.
            disc.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            disc.transform.localScale =
                new Vector3(DiscRadius * 2f, DiscThickness * 0.5f, DiscRadius * 2f);

            Destroy(disc.GetComponent<Collider>());
            disc.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(
                player == Connect4Player.Red ? RedDisc : YellowDisc, 0.45f);
            return disc;
        }

        void AddBox(string name, Vector3 localPosition, Vector3 scale, Color colour)
        {
            float bevel = Mathf.Min(0.05f, Mathf.Min(scale.x, Mathf.Min(scale.y, scale.z)) * 0.3f);
            ArcadeMeshes.Box(root, name, localPosition, scale, bevel,
                ArcadeMaterials.Get(colour, 0.3f), false);
        }

        static float ColumnX(int column) => (column - (Connect4Board.Columns - 1) * 0.5f) * Cell;

        static Vector3 CellPosition(int column, int row) =>
            new Vector3(ColumnX(column), row * Cell + Cell * 0.5f, 0f);
    }

    /// <summary>Marks a column's pick volume, so a raycast can name the column it hit.</summary>
    public class Connect4ColumnPicker : MonoBehaviour
    {
        public int Column;
    }
}
