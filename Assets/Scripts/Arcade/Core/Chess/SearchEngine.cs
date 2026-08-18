using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Chess
{
    /// <summary>
    /// Negamax search with alpha-beta pruning.
    ///
    /// Alpha-beta only pays off if good moves are tried first, so captures are ordered by
    /// most valuable victim and least valuable attacker before quiet moves. Without that
    /// ordering the same depth costs many times more nodes.
    ///
    /// The search is bounded by a node budget as well as a depth. The web build is single
    /// threaded, so an unbounded search would freeze the tab; when the budget runs out the
    /// caller falls back to the best move from the last completed depth.
    /// </summary>
    public sealed class SearchEngine
    {
        /// <summary>Thrown internally to unwind when the node budget is exhausted.</summary>
        class BudgetReached : Exception { }

        readonly List<Move> rootMoves = new List<Move>();
        int nodeBudget;
        int nodesSearched;

        public int NodesSearched => nodesSearched;

        /// <summary>Depth, node budget and sloppiness for each difficulty.</summary>
        public static void SettingsFor(Difficulty difficulty, out int depth, out int budget, out int slopCentipawns, out int blunderPercent)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    // Shallow, and willing to pick a clearly worse move, so it is beatable.
                    depth = 2; budget = 40000; slopCentipawns = 150; blunderPercent = 20;
                    break;
                case Difficulty.Medium:
                    depth = 3; budget = 150000; slopCentipawns = 40; blunderPercent = 5;
                    break;
                default:
                    depth = 4; budget = 500000; slopCentipawns = 0; blunderPercent = 0;
                    break;
            }
        }

        /// <summary>
        /// Best move for the side to move. Returns <see cref="Move.None"/> when the game
        /// is already over. The board is left exactly as it was found.
        /// </summary>
        public Move FindBestMove(Board board, Difficulty difficulty, Random random)
        {
            int depth, budget, slop, blunderPercent;
            SettingsFor(difficulty, out depth, out budget, out slop, out blunderPercent);
            return FindBestMove(board, depth, budget, slop, blunderPercent, random);
        }

        public Move FindBestMove(Board board, int maxDepth, int budget, int slopCentipawns, int blunderPercent, Random random)
        {
            nodeBudget = budget;
            nodesSearched = 0;

            rootMoves.Clear();
            MoveGenerator.GenerateLegalMoves(board, rootMoves);
            if (rootMoves.Count == 0) return Move.None;

            if (random != null && blunderPercent > 0 && random.Next(100) < blunderPercent)
            {
                return rootMoves[random.Next(rootMoves.Count)];
            }

            Order(board, rootMoves);

            var scores = new int[rootMoves.Count];
            var best = new List<Move>();
            Move bestSoFar = rootMoves[0];

            // Iterative deepening: each depth is cheap relative to the next, and if the
            // budget runs out we still have a complete answer from the previous depth.
            for (int depth = 1; depth <= maxDepth; depth++)
            {
                try
                {
                    int alpha = -Evaluation.MateScore * 2;
                    int beta = Evaluation.MateScore * 2;
                    int bestScore = int.MinValue;

                    for (int i = 0; i < rootMoves.Count; i++)
                    {
                        // try/finally matters here: running out of budget throws, and
                        // without this the board would be left with the move still applied.
                        Undo undo = board.MakeMove(rootMoves[i]);
                        int score;
                        try
                        {
                            score = -Negamax(board, depth - 1, -beta, -alpha, 1);
                        }
                        finally
                        {
                            board.UnmakeMove(rootMoves[i], undo);
                        }

                        scores[i] = score;
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestSoFar = rootMoves[i];
                        }
                        if (score > alpha) alpha = score;
                    }
                }
                catch (BudgetReached)
                {
                    // Keep the result of the last fully completed depth.
                    break;
                }
            }

            if (slopCentipawns <= 0 || random == null) return bestSoFar;

            // Pick at random among moves close enough to the best, so easier levels do not
            // play the identical game every time.
            int bestKnown = int.MinValue;
            for (int i = 0; i < scores.Length; i++) if (scores[i] > bestKnown) bestKnown = scores[i];

            best.Clear();
            for (int i = 0; i < scores.Length; i++)
                if (bestKnown - scores[i] <= slopCentipawns) best.Add(rootMoves[i]);

            return best.Count > 0 ? best[random.Next(best.Count)] : bestSoFar;
        }

        int Negamax(Board board, int depth, int alpha, int beta, int ply)
        {
            if (++nodesSearched > nodeBudget) throw new BudgetReached();

            var moves = new List<Move>();
            MoveGenerator.GenerateLegalMoves(board, moves);

            if (moves.Count == 0)
            {
                // Prefer mates that arrive sooner, and avoid ones that arrive later.
                if (board.IsInCheck(board.SideToMove)) return -Evaluation.MateScore + ply;
                return 0;
            }

            if (depth <= 0) return Perspective(board, Evaluation.Evaluate(board));

            if (board.HalfmoveClock >= 100) return 0;

            Order(board, moves);

            int best = int.MinValue;
            foreach (Move move in moves)
            {
                Undo undo = board.MakeMove(move);
                int score;
                try
                {
                    score = -Negamax(board, depth - 1, -beta, -alpha, ply + 1);
                }
                finally
                {
                    board.UnmakeMove(move, undo);
                }

                if (score > best) best = score;
                if (score > alpha) alpha = score;
                if (alpha >= beta) break;   // this branch is already refuted
            }

            return best;
        }

        static int Perspective(Board board, int whiteScore) =>
            board.SideToMove == PieceColor.White ? whiteScore : -whiteScore;

        /// <summary>
        /// Captures first, most valuable victim taken by least valuable attacker, then
        /// promotions. Cheap to compute and worth far more than it costs.
        /// </summary>
        static void Order(Board board, List<Move> moves)
        {
            moves.Sort((a, b) => ScoreMove(board, b).CompareTo(ScoreMove(board, a)));
        }

        static int ScoreMove(Board board, Move move)
        {
            int score = 0;

            if (move.IsCapture)
            {
                Piece victim = board[move.To];
                Piece attacker = board[move.From];
                int victimValue = victim.IsNone ? Evaluation.PieceValues[(int)PieceType.Pawn]  // en passant
                    : Evaluation.PieceValues[(int)victim.Type];
                score += 10000 + victimValue * 10 - Evaluation.PieceValues[(int)attacker.Type];
            }

            if (move.IsPromotion) score += 9000 + Evaluation.PieceValues[(int)move.Promotion];

            return score;
        }
    }
}
