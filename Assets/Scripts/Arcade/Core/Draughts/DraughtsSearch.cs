using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Draughts
{
    /// <summary>
    /// The draughts opponent: negamax with alpha-beta over a cloned board.
    ///
    /// Unlike chess and Connect 4 this searches on copies rather than making and unmaking
    /// moves. A draughts move can take several pieces and crown at the end of it, so undo
    /// would have to restore a list of victims and a promotion; cloning a sixty-four entry
    /// array is cheap enough that the extra state is not worth the bugs.
    ///
    /// Captures being compulsory keeps the branching factor low, which is why a modest
    /// depth already plays a decent game.
    /// </summary>
    public sealed class DraughtsSearch
    {
        const int WinScore = 1000000;

        class BudgetReached : Exception { }

        int nodeBudget;
        int nodesSearched;

        public int NodesSearched => nodesSearched;

        public static void SettingsFor(Difficulty difficulty, out int depth, out int budget)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    depth = 2;
                    budget = 20000;
                    break;
                case Difficulty.Hard:
                    depth = 8;
                    budget = 600000;
                    break;
                default:
                    depth = 5;
                    budget = 120000;
                    break;
            }
        }

        public bool TryChooseMove(DraughtsBoard board, Difficulty difficulty, Random random,
            out DraughtsMove chosen)
        {
            SettingsFor(difficulty, out int depth, out int budget);
            return TryChooseMove(board, depth, budget, random, out chosen);
        }

        public bool TryChooseMove(DraughtsBoard board, int depth, int budget, Random random,
            out DraughtsMove chosen)
        {
            nodeBudget = budget;
            nodesSearched = 0;
            chosen = default;

            List<DraughtsMove> moves = board.GenerateMoves();
            if (moves.Count == 0) return false;

            chosen = moves[0];
            var best = new List<DraughtsMove>();
            int bestScore = int.MinValue;

            try
            {
                foreach (DraughtsMove move in moves)
                {
                    DraughtsBoard next = board.Clone();
                    next.Play(move);
                    int score = -Negamax(next, depth - 1, -WinScore * 2, WinScore * 2);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best.Clear();
                        best.Add(move);
                    }
                    else if (score == bestScore)
                    {
                        best.Add(move);
                    }
                }
            }
            catch (BudgetReached)
            {
                // Keep whatever the completed part of the search liked best.
            }

            if (best.Count == 0) return true;

            // Draughts has many equal looking moves, especially early. Picking at random
            // among them stops every game against the same setting being identical.
            chosen = random != null ? best[random.Next(best.Count)] : best[0];
            return true;
        }

        int Negamax(DraughtsBoard board, int depth, int alpha, int beta)
        {
            if (++nodesSearched > nodeBudget) throw new BudgetReached();

            if (DraughtsBoard.IsOver(board.Status))
            {
                if (board.Status == DraughtsStatus.Draw) return 0;

                bool whiteToMove = board.SideToMove == DraughtsSide.White;
                bool whiteWon = board.Status == DraughtsStatus.WhiteWins;
                // Scored from the side to move, which is the side that has just been left
                // with nothing to do.
                return whiteToMove == whiteWon ? WinScore : -WinScore;
            }

            List<DraughtsMove> moves = board.GenerateMoves();
            if (moves.Count == 0) return -WinScore;

            if (depth <= 0) return Evaluate(board, board.SideToMove);

            int best = int.MinValue;
            foreach (DraughtsMove move in moves)
            {
                DraughtsBoard next = board.Clone();
                next.Play(move);
                int score = -Negamax(next, depth - 1, -beta, -alpha);

                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }
            return best;
        }

        /// <summary>
        /// Material first, then position.
        ///
        /// A king is worth roughly three men. Men are worth more the closer they are to
        /// crowning, which is what stops the search shuffling on the back rank, and the
        /// centre files are worth a little more than the edges because a piece on the rail
        /// has half as many moves.
        /// </summary>
        static int Evaluate(DraughtsBoard board, DraughtsSide forSide)
        {
            int score = 0;

            for (int square = 0; square < Square.Count; square++)
            {
                DraughtsPiece piece = board[square];
                if (piece.IsNone) continue;

                int value = piece.IsKing ? 340 : 100;

                if (!piece.IsKing)
                {
                    int rank = Square.RankOf(square);
                    int advance = piece.Side == DraughtsSide.White ? rank : 7 - rank;
                    value += advance * 9;
                }

                int file = Square.FileOf(square);
                value += 3 - Math.Abs(file * 2 - 7) / 2;

                score += piece.Side == forSide ? value : -value;
            }

            return score;
        }
    }
}
