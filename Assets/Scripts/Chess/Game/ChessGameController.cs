using System;
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

        readonly GameObject[] pieceViews = new GameObject[Square.Count];
        readonly List<Move> legalMoves = new List<Move>();
        readonly List<Move> movesFromSelection = new List<Move>();

        Board board;
        int selectedSquare = Square.None;

        public Board Board => board;
        public GameStatus Status { get; private set; } = GameStatus.Ongoing;

        public event Action<Move> MoveMade;
        public event Action<GameStatus> StatusChanged;

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
            board = string.IsNullOrWhiteSpace(startingFen) ? new Board() : new Board(startingFen);
            selectedSquare = Square.None;

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
            if (GameStatusEvaluator.IsGameOver(Status)) return;

            Ray ray = camera.ScreenPointToRay(screenPosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, 1000f)) return;

            int square = boardView.WorldToSquare(hit.point);
            if (square == Square.None) return;

            HandleSquarePicked(square);
        }

        public void HandleSquarePicked(int square)
        {
            EnsureInitialised();

            if (selectedSquare != Square.None)
            {
                foreach (Move move in movesFromSelection)
                {
                    if (move.To != square) continue;
                    if (move.IsPromotion && move.Promotion != autoPromotion) continue;

                    PlayMove(move);
                    return;
                }
            }

            Piece piece = board[square];
            if (piece.IsSome && piece.Color == board.SideToMove) Select(square);
            else ClearSelection();
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
            board.MakeMove(move);
            ClearSelection();
            RebuildPieceViews();
            RefreshLegalMoves();
            UpdateStatus();
            MoveMade?.Invoke(move);
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

        /// <summary>
        /// Rebuilds every piece view from the board. Simple and always correct; animated
        /// moves can later diff against this instead of respawning.
        /// </summary>
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
