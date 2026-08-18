using System.Collections;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Draughts;
using UnityEngine;
using UnityEngine.InputSystem;

namespace LightningForge.Arcade.Game.Draughts
{
    /// <summary>
    /// Draughts on the same eight by eight board chess uses.
    ///
    /// Picking a destination plays the whole move, chain of jumps and all, rather than
    /// asking the player to click each hop. Captures are compulsory and a chain must be
    /// finished, so the intermediate squares are never a choice; making the player click
    /// through them would be ceremony rather than agency. The piece still travels the path
    /// so the jumps are visible.
    /// </summary>
    public class DraughtsGame : ArcadeGame
    {
        static readonly Color LightSquare = new Color(0.62f, 0.55f, 0.44f);
        static readonly Color DarkSquare = new Color(0.20f, 0.16f, 0.13f);
        static readonly Color FrameColour = new Color(0.14f, 0.11f, 0.09f);
        static readonly Color WhitePiece = new Color(0.86f, 0.83f, 0.76f);
        static readonly Color BlackPiece = new Color(0.13f, 0.12f, 0.12f);
        static readonly Color CrownColour = new Color(0.85f, 0.68f, 0.28f);

        readonly DraughtsBoard board = new DraughtsBoard();
        readonly DraughtsSearch search = new DraughtsSearch();
        readonly Dictionary<int, GameObject> pieces = new Dictionary<int, GameObject>();
        readonly List<DraughtsMove> selectedMoves = new List<DraughtsMove>();

        SquareBoardView boardView;
        Camera targetCamera;
        BoardCameraRig cameraRig;
        Coroutine animating;
        Coroutine thinking;
        int selectedSquare = Square.None;

        public override ArcadeGameId Id => ArcadeGameId.Draughts;

        public override bool IsFinished => DraughtsBoard.IsOver(board.Status);

        public override string DebugState => board.ToString();

        public override string StatusText
        {
            get
            {
                switch (board.Status)
                {
                    case DraughtsStatus.WhiteWins: return "White wins";
                    case DraughtsStatus.BlackWins: return "Black wins";
                    case DraughtsStatus.Draw: return "A draw";
                    default:
                        string side = board.SideToMove == DraughtsSide.White ? "White" : "Black";
                        // Worth saying, because a player who has not met the rule will
                        // wonder why their other pieces refuse to move.
                        return MustCapture() ? side + " to move, a capture is forced" : side + " to move";
                }
            }
        }

        bool WhiteToMove => board.SideToMove == DraughtsSide.White;

        bool MustCapture()
        {
            List<DraughtsMove> moves = board.GenerateMoves();
            return moves.Count > 0 && moves[0].IsCapture;
        }

        void Awake()
        {
            targetCamera = Camera.main;
            cameraRig = FindFirstObjectByType<BoardCameraRig>();
        }

        protected override void OnBegin()
        {
            board.Reset();
            BuildBoard();
            RefreshPieces();
            ClearSelection();

            if (cameraRig != null)
            {
                // The flat board framing, seen from whichever side is being played.
                cameraRig.ClearFramingOverride();
                cameraRig.SetViewpoint(Setup.Control == ControlMode.BlackOnly
                    ? Core.Chess.PieceColor.Black
                    : Core.Chess.PieceColor.White);
            }

            MaybeStartThinking();
        }

        public override void End()
        {
            StopWork();
            if (boardView != null)
            {
                Destroy(boardView.gameObject);
                boardView = null;
            }
            pieces.Clear();
        }

        public override void Restart()
        {
            StopWork();
            board.Reset();
            RefreshPieces();
            ClearSelection();
            Raise();
            MaybeStartThinking();
        }

        public override void AssignOnlineSide(bool firstSeat)
        {
            base.AssignOnlineSide(firstSeat);
            if (cameraRig != null)
            {
                cameraRig.SetViewpoint(firstSeat ? Core.Chess.PieceColor.White : Core.Chess.PieceColor.Black);
            }
        }

        void StopWork()
        {
            if (animating != null) { StopCoroutine(animating); animating = null; }
            if (thinking != null) { StopCoroutine(thinking); thinking = null; }
        }

        // Input ---------------------------------------------------------------------

        void Update()
        {
            if (boardView == null || IsFinished) return;
            if (animating != null || thinking != null) return;
            if (!LocalControls(WhiteToMove)) return;
            if (!WasPressedThisFrame()) return;

            int square = SquareUnderPointer();
            if (square == Square.None) return;

            HandlePick(square);
        }

        static bool WasPressedThisFrame()
        {
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame) return true;

            Touchscreen touch = Touchscreen.current;
            return touch != null && touch.primaryTouch.press.wasPressedThisFrame;
        }

        int SquareUnderPointer()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null) return Square.None;

            Vector2 pointer;
            Mouse mouse = Mouse.current;
            Touchscreen touch = Touchscreen.current;
            if (mouse != null) pointer = mouse.position.ReadValue();
            else if (touch != null) pointer = touch.primaryTouch.position.ReadValue();
            else return Square.None;

            Ray ray = targetCamera.ScreenPointToRay(pointer);
            return Physics.Raycast(ray, out RaycastHit hit, 200f)
                ? boardView.WorldToSquare(hit.point)
                : Square.None;
        }

        void HandlePick(int square)
        {
            // A second click on a highlighted destination commits the move.
            if (selectedSquare != Square.None)
            {
                foreach (DraughtsMove move in selectedMoves)
                {
                    if (move.To != square) continue;
                    PlayMove(move, true);
                    return;
                }
            }

            DraughtsPiece piece = board[square];
            if (piece.Side != board.SideToMove)
            {
                ClearSelection();
                return;
            }

            selectedSquare = square;
            selectedMoves.Clear();
            foreach (DraughtsMove move in board.GenerateMoves())
            {
                if (move.From == square) selectedMoves.Add(move);
            }

            boardView.SetSelected(selectedMoves.Count > 0 ? square : Square.None);

            var destinations = new List<int>();
            foreach (DraughtsMove move in selectedMoves) destinations.Add(move.To);
            boardView.SetHighlights(destinations);
        }

        void ClearSelection()
        {
            selectedSquare = Square.None;
            selectedMoves.Clear();
            if (boardView != null)
            {
                boardView.SetSelected(Square.None);
                boardView.ClearHighlights();
            }
        }

        // Playing -------------------------------------------------------------------

        void PlayMove(DraughtsMove move, bool local)
        {
            ClearSelection();
            if (local) RaiseMovePlayed(move.ToNotation());

            board.Play(move);
            animating = StartCoroutine(AnimateMove(move));
            Raise();
        }

        public override bool ApplyRemoteMove(string encoded)
        {
            if (!board.TryFindMove(encoded, out DraughtsMove move)) return false;
            PlayMove(move, false);
            return true;
        }

        IEnumerator AnimateMove(DraughtsMove move)
        {
            if (!pieces.TryGetValue(move.From, out GameObject piece) || piece == null)
            {
                // Nothing to animate, so just resync and carry on.
                animating = null;
                RefreshPieces();
                MaybeStartThinking();
                yield break;
            }

            pieces.Remove(move.From);

            int captureIndex = 0;
            int current = move.From;
            foreach (int landing in move.Path)
            {
                Vector3 from = boardView.SquareSurface(current);
                Vector3 to = boardView.SquareSurface(landing);

                float duration = move.IsCapture ? 0.22f : 0.16f;
                float elapsed = 0f;
                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float t = Mathf.Clamp01(elapsed / duration);
                    Vector3 p = Vector3.Lerp(from, to, t);
                    // A hop over the piece being taken, so the jump reads as a jump.
                    if (move.IsCapture) p.y += Mathf.Sin(t * Mathf.PI) * 0.45f;
                    piece.transform.position = p;
                    yield return null;
                }

                piece.transform.position = to;

                // Remove the man jumped on this leg as it is passed, not all at the end.
                if (move.Captured != null && captureIndex < move.Captured.Length)
                {
                    int victim = move.Captured[captureIndex++];
                    if (pieces.TryGetValue(victim, out GameObject taken) && taken != null) Destroy(taken);
                    pieces.Remove(victim);
                }

                current = landing;
            }

            pieces[move.To] = piece;
            if (move.Crowns) AddCrown(piece);

            animating = null;
            Raise();
            MaybeStartThinking();
        }

        void MaybeStartThinking()
        {
            if (IsFinished || Setup.Mode != GameMode.SinglePlayer) return;
            if (LocalControls(WhiteToMove)) return;
            if (thinking != null || animating != null) return;

            thinking = StartCoroutine(ThinkAndPlay());
        }

        IEnumerator ThinkAndPlay()
        {
            yield return new WaitForSeconds(0.3f);

            bool found = search.TryChooseMove(board, Setup.Difficulty, new System.Random(),
                out DraughtsMove move);
            thinking = null;

            if (found) PlayMove(move, false);
        }

        // Presentation --------------------------------------------------------------

        void BuildBoard()
        {
            if (boardView != null) Destroy(boardView.gameObject);

            var go = new GameObject("Draughts Board");
            go.transform.SetParent(transform, false);
            boardView = go.AddComponent<SquareBoardView>();
            boardView.Configure(
                ArcadeMaterials.Get(LightSquare, 0.2f),
                ArcadeMaterials.Get(DarkSquare, 0.2f),
                ArcadeMaterials.Emissive(new Color(0.25f, 0.65f, 0.45f), 0.9f),
                ArcadeMaterials.Emissive(new Color(0.85f, 0.70f, 0.25f), 1.1f),
                ArcadeMaterials.Get(FrameColour, 0.25f));
        }

        void RefreshPieces()
        {
            foreach (GameObject piece in pieces.Values)
            {
                if (piece != null) Destroy(piece);
            }
            pieces.Clear();

            for (int square = 0; square < Square.Count; square++)
            {
                DraughtsPiece piece = board[square];
                if (piece.IsNone) continue;
                pieces[square] = MakePiece(piece, square);
            }
        }

        GameObject MakePiece(DraughtsPiece piece, int square)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = (piece.Side == DraughtsSide.White ? "White" : "Black") + (piece.IsKing ? "King" : "Man");
            go.transform.SetParent(boardView.transform.parent, false);
            go.transform.position = boardView.SquareSurface(square);
            go.transform.localScale = new Vector3(0.72f, 0.11f, 0.72f);
            Destroy(go.GetComponent<Collider>());

            go.GetComponent<MeshRenderer>().sharedMaterial = ArcadeMaterials.Get(
                piece.Side == DraughtsSide.White ? WhitePiece : BlackPiece, 0.35f);

            if (piece.IsKing) AddCrown(go);
            return go;
        }

        /// <summary>
        /// A king gets a smaller disc on top, which is how a real set marks one: a second
        /// piece stacked on the first.
        /// </summary>
        void AddCrown(GameObject piece)
        {
            if (piece.transform.Find("Crown") != null) return;

            var crown = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            crown.name = "Crown";
            crown.transform.SetParent(piece.transform, false);
            // The parent is already a flattened cylinder, so the local scale has to undo
            // that squash or the crown comes out as a wafer.
            crown.transform.localScale = new Vector3(0.62f, 1.5f, 0.62f);
            crown.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            Destroy(crown.GetComponent<Collider>());
            crown.GetComponent<MeshRenderer>().sharedMaterial =
                ArcadeMaterials.Get(CrownColour, 0.6f, 0.4f);
        }
    }
}
