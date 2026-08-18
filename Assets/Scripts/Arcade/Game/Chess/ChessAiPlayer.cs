using System.Collections;
using LightningForge.Arcade.Core.Chess;
using UnityEngine;

namespace LightningForge.Arcade.Game.Chess
{
    /// <summary>
    /// Plays one side against the human.
    ///
    /// The search runs on the main thread because the web build is single threaded and has
    /// no workers available to Unity. To keep the tab responsive it waits a frame before
    /// searching, and the search itself is bounded by a node budget, so the worst case is
    /// a brief hitch rather than a freeze.
    /// </summary>
    public class ChessAiPlayer : MonoBehaviour
    {
        [SerializeField] ChessGameController controller;
        [SerializeField] Difficulty difficulty = Difficulty.Medium;

        [Tooltip("Side the computer plays.")]
        [SerializeField] PieceColor side = PieceColor.Black;

        [Tooltip("Pause before replying, so moves do not appear instantly.")]
        [SerializeField] float thinkingDelay = 0.45f;

        readonly SearchEngine engine = new SearchEngine();
        System.Random random;
        Coroutine thinking;

        public Difficulty Difficulty
        {
            get => difficulty;
            set => difficulty = value;
        }

        public PieceColor Side
        {
            get => side;
            set => side = value;
        }

        public bool IsThinking => thinking != null;

        void Awake()
        {
            if (controller == null) controller = FindFirstObjectByType<ChessGameController>();
            random = new System.Random();
        }

        void OnEnable()
        {
            if (controller != null)
            {
                controller.MoveMade += OnMoveMade;
                controller.StatusChanged += OnStatusChanged;
            }
            // The computer may be on move the instant it is switched on.
            Nudge();
        }

        void OnDisable()
        {
            if (controller != null)
            {
                controller.MoveMade -= OnMoveMade;
                controller.StatusChanged -= OnStatusChanged;
            }
            Stop();
        }

        void OnMoveMade(Move move) => Nudge();
        void OnStatusChanged(GameStatus status) => Nudge();

        public void Stop()
        {
            if (thinking != null)
            {
                StopCoroutine(thinking);
                thinking = null;
            }
        }

        /// <summary>Starts thinking if it is our turn and the game is still running.</summary>
        public void Nudge()
        {
            if (!isActiveAndEnabled || controller == null || controller.Board == null) return;
            if (thinking != null) return;
            if (GameStatusEvaluator.IsGameOver(controller.Status)) return;
            if (controller.Board.SideToMove != side) return;

            thinking = StartCoroutine(Think());
        }

        IEnumerator Think()
        {
            // If the game is restarted while we wait, this whole line of thought is about
            // a position that no longer exists and must be dropped.
            int gameId = controller.GameId;

            // Let the human's move finish animating before the reply lands.
            float wait = Mathf.Max(thinkingDelay, 0f);
            float elapsed = 0f;
            while (elapsed < wait || controller.IsAnimating)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }

            // Re-check: the game may have been reset, ended, or the turn changed.
            if (controller.GameId != gameId
                || controller.Board.SideToMove != side
                || GameStatusEvaluator.IsGameOver(controller.Status))
            {
                thinking = null;
                yield break;
            }

            Move move = engine.FindBestMove(controller.Board, difficulty, random);

            thinking = null;

            if (move.IsNone)
            {
                // No legal reply means the game is over; the controller already knows.
                yield break;
            }

            controller.TryPlayUci(move.ToUci());
        }
    }
}
