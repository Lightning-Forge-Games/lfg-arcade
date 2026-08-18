using System;
using System.Collections;
using System.Collections.Generic;
using LightningForge.Chess.Core;
using UnityEngine;

namespace LightningForge.Chess.Game
{
    /// <summary>
    /// Binds the rules core to the visuals: keeps piece views in sync with the board and
    /// turns pointer picks into legal moves.
    ///
    /// Deliberately input-agnostic. Call <see cref="HandlePointer"/> from whatever input
    /// layer you like so this stays testable and independent of the Input System package.
    /// </summary>
    [RequireComponent(typeof(ChessBoardView))]
    public class ChessGameController : MonoBehaviour
    {
        [SerializeField] ChessBoardView boardView;
        [SerializeField] PieceViewFactory pieceFactory;

        [Tooltip("Starting position. Leave blank for the standard opening setup.")]
        [SerializeField] string startingFen = string.Empty;

        [Tooltip("Piece chosen automatically when a pawn promotes.")]
        [SerializeField] PieceType autoPromotion = PieceType.Queen;

        [Header("Animation")]
        [SerializeField] float moveDuration = 0.28f;
        [SerializeField] float knightHopHeight = 0.55f;
        [SerializeField] float glideHeight = 0.08f;
        [SerializeField] float captureFadeDuration = 0.22f;

        readonly GameObject[] pieceViews = new GameObject[Square.Count];
        readonly List<Move> legalMoves = new List<Move>();
        readonly List<Move> movesFromSelection = new List<Move>();

        Board board;
        int selectedSquare = Square.None;
        Coroutine running;
        int pendingPromotionFrom = Square.None;
        int pendingPromotionTo = Square.None;

        public Board Board => board;
        public GameStatus Status { get; private set; } = GameStatus.Ongoing;
        public bool IsAnimating => running != null;

        /// <summary>True while waiting for the player to choose a promotion piece.</summary>
        public bool AwaitingPromotion => pendingPromotionFrom != Square.None;

        public event Action<Move> MoveMade;
        public event Action<GameStatus> StatusChanged;

        /// <summary>
        /// Raised when a pawn reaches the last rank, with the origin and destination squares.
        /// Answer it with <see cref="CompletePromotion"/>. With no subscriber the controller
        /// falls back to <see cref="autoPromotion"/> so the game stays playable on its own.
        /// </summary>
        public event Action<int, int> PromotionRequested;

        /// <summary>The view currently standing on a square, or null. Exposed for tests.</summary>
        public GameObject GetPieceView(int square) =>
            Square.IsValid(square) ? pieceViews[square] : null;

        void Reset()
        {
            boardView = GetComponent<ChessBoardView>();
        }

        void Awake()
        {
            EnsureInitialised();
        }

        /// <summary>
        /// Rebuilds state if it is missing. <see cref="board"/> is a plain C# object, so a
        /// domain reload while in play mode (any script recompile) wipes it without Awake
        /// running again. Entry points call this so the game recovers instead of throwing.
        /// </summary>
        void EnsureInitialised()
        {
            if (boardView == null) boardView = GetComponent<ChessBoardView>();
            if (board == null) NewGame();
        }

        public void NewGame()
        {
            if (running != null)
            {
                StopCoroutine(running);
                running = null;
            }

            board = string.IsNullOrWhiteSpace(startingFen) ? new Board() : new Board(startingFen);
            selectedSquare = Square.None;
            pendingPromotionFrom = Square.None;
            pendingPromotionTo = Square.None;

            RebuildPieceViews();
            RefreshLegalMoves();
            UpdateStatus();

            if (boardView != null)
            {
                boardView.SetSelected(Square.None);
                boardView.ClearHighlights();
            }
        }

        /// <summary>
        /// Routes a pointer pick. First click selects one of your own pieces, second click
        /// either plays a legal move, switches selection, or clears it.
        /// </summary>
        public void HandlePointer(Vector2 screenPosition, Camera camera)
        {
            EnsureInitialised();

            if (camera == null) camera = Camera.main;
            if (camera == null || board == null) return;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            int square = boardView.WorldToSquare(hit.point);
            if (square == Square.None) return;

            HandleSquarePicked(square);
        }

        public void HandleSquarePicked(int square)
        {
            EnsureInitialised();

            // Ignore picks mid-animation: the board has already advanced, so acting now
            // would let a second move start before the first finished moving on screen.
            if (IsAnimating || AwaitingPromotion || GameStatusEvaluator.IsGameOver(Status)) return;

            if (selectedSquare != Square.None)
            {
                foreach (Move move in movesFromSelection)
                {
                    if (move.To != square) continue;

                    if (move.IsPromotion)
                    {
                        // Hand the choice to the UI when something is listening, so
                        // underpromotion is reachable rather than silently queening.
                        if (PromotionRequested != null)
                        {
                            pendingPromotionFrom = move.From;
                            pendingPromotionTo = move.To;
                            PromotionRequested(move.From, move.To);
                            return;
                        }
                        if (move.Promotion != autoPromotion) continue;
                    }

                    PlayMove(move);
                    return;
                }
            }

            Piece piece = board[square];
            if (piece.IsSome && piece.Color == board.SideToMove) Select(square);
            else ClearSelection();
        }

        /// <summary>
        /// Plays the promotion the player picked. Returns false if nothing was pending or
        /// the piece is not a legal promotion here.
        /// </summary>
        public bool CompletePromotion(PieceType promotion)
        {
            if (!AwaitingPromotion) return false;

            foreach (Move move in legalMoves)
            {
                if (move.From != pendingPromotionFrom) continue;
                if (move.To != pendingPromotionTo) continue;
                if (!move.IsPromotion || move.Promotion != promotion) continue;

                pendingPromotionFrom = Square.None;
                pendingPromotionTo = Square.None;
                PlayMove(move);
                return true;
            }

            return false;
        }

        public void CancelPromotion()
        {
            pendingPromotionFrom = Square.None;
            pendingPromotionTo = Square.None;
            ClearSelection();
        }

        public bool TryPlayUci(string uci)
        {
            EnsureInitialised();

            foreach (Move move in legalMoves)
            {
                if (move.ToUci() == uci)
                {
                    PlayMove(move);
                    return true;
                }
            }
            return false;
        }

        void PlayMove(Move move)
        {
            PieceColor mover = board.SideToMove;
            PieceType movingType = board[move.From].Type;

            // Work out every visual consequence before the board mutates.
            int captureSquare = move.IsEnPassant
                ? move.To + (mover == PieceColor.White ? -8 : 8)
                : move.To;

            GameObject capturedView = board[captureSquare].IsSome ? pieceViews[captureSquare] : null;
            GameObject movingView = pieceViews[move.From];

            int rookFrom = Square.None;
            int rookTo = Square.None;
            if (move.IsCastle)
            {
                int backRank = mover == PieceColor.White ? 0 : 56;
                bool kingSide = (move.Flags & MoveFlags.KingSideCastle) != 0;
                rookFrom = kingSide ? backRank + 7 : backRank;
                rookTo = kingSide ? backRank + 5 : backRank + 3;
            }

            board.MakeMove(move);

            // Re-key the view table to match.
            if (capturedView != null) pieceViews[captureSquare] = null;
            pieceViews[move.From] = null;
            pieceViews[move.To] = movingView;

            GameObject rookView = null;
            if (rookFrom != Square.None)
            {
                rookView = pieceViews[rookFrom];
                pieceViews[rookFrom] = null;
                pieceViews[rookTo] = rookView;
            }

            ClearSelection();
            RefreshLegalMoves();
            UpdateStatus();

            running = StartCoroutine(AnimateMove(
                move, movingView, movingType, capturedView, rookView, rookTo, mover));

            MoveMade?.Invoke(move);
        }

        IEnumerator AnimateMove(
            Move move, GameObject movingView, PieceType movingType,
            GameObject capturedView, GameObject rookView, int rookTo, PieceColor mover)
        {
            float duration = movingType == PieceType.Knight ? moveDuration * 1.35f : moveDuration;
            float arc = movingType == PieceType.Knight ? knightHopHeight : glideHeight;

            Vector3 fromPos = movingView != null ? movingView.transform.position : Vector3.zero;
            Vector3 toPos = boardView.SquareSurface(move.To);

            Vector3 rookFromPos = rookView != null ? rookView.transform.position : Vector3.zero;
            Vector3 rookToPos = rookTo != Square.None ? boardView.SquareSurface(rookTo) : Vector3.zero;

            Coroutine captureRoutine = null;
            bool captureStarted = false;
            float elapsed = 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = t * t * (3f - 2f * t);   // smoothstep

                if (movingView != null)
                {
                    Vector3 p = Vector3.Lerp(fromPos, toPos, eased);
                    p.y += Mathf.Sin(eased * Mathf.PI) * arc;
                    movingView.transform.position = p;
                }

                if (rookView != null)
                {
                    rookView.transform.position = Vector3.Lerp(rookFromPos, rookToPos, eased);
                }

                // Start clearing the captured piece once the attacker is most of the way in.
                if (!captureStarted && capturedView != null && t > 0.55f)
                {
                    captureStarted = true;
                    captureRoutine = StartCoroutine(AnimateCapture(capturedView));
                }

                yield return null;
            }

            if (movingView != null) movingView.transform.position = toPos;
            if (rookView != null) rookView.transform.position = rookToPos;

            // The capture outlives the slide, so wait for it. Otherwise IsAnimating drops
            // to false while a taken piece is still visibly shrinking on the board.
            if (capturedView != null && !captureStarted) yield return AnimateCapture(capturedView);
            else if (captureRoutine != null) yield return captureRoutine;

            // Promotion swaps the model only once the pawn has arrived.
            if (move.IsPromotion)
            {
                if (movingView != null) Destroy(movingView);
                Piece promoted = board[move.To];
                GameObject view = pieceFactory != null
                    ? pieceFactory.Create(promoted.Type, promoted.Color, transform)
                    : null;
                if (view != null)
                {
                    view.transform.position = toPos;
                    pieceViews[move.To] = view;
                    yield return AnimateSpawn(view);
                }
            }

            running = null;
        }

        IEnumerator AnimateCapture(GameObject view)
        {
            Vector3 startScale = view.transform.localScale;
            Vector3 startPos = view.transform.position;
            float elapsed = 0f;

            while (elapsed < captureFadeDuration && view != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / captureFadeDuration);
                view.transform.localScale = Vector3.Lerp(startScale, startScale * 0.05f, t);
                view.transform.position = startPos + Vector3.down * (t * 0.18f);
                yield return null;
            }

            if (view != null) Destroy(view);
        }

        IEnumerator AnimateSpawn(GameObject view)
        {
            Vector3 target = view.transform.localScale;
            float elapsed = 0f;
            const float duration = 0.18f;

            while (elapsed < duration && view != null)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / duration);
                view.transform.localScale = Vector3.Lerp(target * 0.3f, target, t);
                yield return null;
            }

            if (view != null) view.transform.localScale = target;
        }

        void Select(int square)
        {
            selectedSquare = square;
            movesFromSelection.Clear();

            var targets = new List<int>();
            foreach (Move move in legalMoves)
            {
                if (move.From != square) continue;
                movesFromSelection.Add(move);
                targets.Add(move.To);
            }

            boardView.SetSelected(square);
            boardView.SetHighlights(targets);
        }

        void ClearSelection()
        {
            selectedSquare = Square.None;
            movesFromSelection.Clear();
            boardView.SetSelected(Square.None);
            boardView.ClearHighlights();
        }

        void RefreshLegalMoves()
        {
            legalMoves.Clear();
            MoveGenerator.GenerateLegalMoves(board, legalMoves);
        }

        void UpdateStatus()
        {
            GameStatus previous = Status;
            Status = GameStatusEvaluator.Evaluate(board);
            if (Status != previous) StatusChanged?.Invoke(Status);
        }

        /// <summary>Spawns a fresh set of views from the board. Used on new game only.</summary>
        void RebuildPieceViews()
        {
            for (int square = 0; square < Square.Count; square++)
            {
                if (pieceViews[square] == null) continue;
                DestroyView(pieceViews[square]);
                pieceViews[square] = null;
            }

            if (pieceFactory == null) return;

            for (int square = 0; square < Square.Count; square++)
            {
                Piece piece = board[square];
                if (piece.IsNone) continue;

                GameObject view = pieceFactory.Create(piece.Type, piece.Color, transform);
                view.transform.position = boardView.SquareSurface(square);
                pieceViews[square] = view;
            }
        }

        void DestroyView(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }
    }
}
