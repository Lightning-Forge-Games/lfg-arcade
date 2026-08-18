using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Connect4
{
    /// <summary>
    /// The Connect 4 opponent: negamax with alpha-beta over the seven columns.
    ///
    /// Connect 4 is small enough that plain alpha-beta plays a genuinely strong game
    /// provided two things are true. Moves are tried from the centre outwards, because
    /// central columns take part in far more possible lines and searching them first is
    /// what makes the cutoffs happen. And wins are scored by how soon they arrive, so the
    /// search prefers mate in one to mate in five and does not wander while winning.
    ///
    /// Like the chess engine this is bounded by a node budget as well as a depth, because
    /// the web build is single threaded and an unbounded search would freeze the tab.
    /// </summary>
    public sealed class Connect4Search
    {
        /// <summary>Big enough to dwarf any positional score, small enough not to overflow.</summary>
        const int WinScore = 1000000;

        /// <summary>Centre first. This ordering is most of the strength.</summary>
        static readonly int[] ColumnOrder = { 3, 2, 4, 1, 5, 0, 6 };

        class BudgetReached : Exception { }

        int nodeBudget;
        int nodesSearched;

        public int NodesSearched => nodesSearched;

        /// <summary>How deep each setting looks, and how many nodes it may spend.</summary>
        public static void SettingsFor(Difficulty difficulty, out int depth, out int budget)
        {
            switch (difficulty)
            {
                case Difficulty.Easy:
                    // Sees an immediate win or block and little else, which is about the
                    // level of someone who has just learned the game.
                    depth = 2;
                    budget = 20000;
                    break;
                case Difficulty.Hard:
                    depth = 9;
                    budget = 900000;
                    break;
                default:
                    depth = 5;
                    budget = 150000;
                    break;
            }
        }

        public int ChooseColumn(Connect4Board board, Difficulty difficulty, Random random)
        {
            SettingsFor(difficulty, out int depth, out int budget);
            return ChooseColumn(board, depth, budget, random);
        }

        public int ChooseColumn(Connect4Board board, int depth, int budget, Random random)
        {
            nodeBudget = budget;
            nodesSearched = 0;

            var candidates = new List<int>();
            foreach (int column in board.PlayableColumns()) candidates.Add(column);
            if (candidates.Count == 0) return -1;

            int best = candidates[0];
            int bestScore = int.MinValue;

            // Iterative deepening, so running out of budget still leaves a good answer from
            // the last depth that finished rather than an unsearched guess.
            for (int currentDepth = 1; currentDepth <= depth; currentDepth++)
            {
                int localBest = -1;
                int localBestScore = int.MinValue;
                bool completed = true;

                try
                {
                    foreach (int column in Ordered(board))
                    {
                        board.Drop(column);
                        int score = -Negamax(board, currentDepth - 1, -WinScore * 2, WinScore * 2);
                        board.Undo(column);

                        if (score > localBestScore)
                        {
                            localBestScore = score;
                            localBest = column;
                        }
                    }
                }
                catch (BudgetReached)
                {
                    completed = false;
                }

                if (completed && localBest >= 0)
                {
                    best = localBest;
                    bestScore = localBestScore;

                    // A forced win is not going to be improved on by looking deeper.
                    if (bestScore >= WinScore - 100) break;
                }
                else
                {
                    break;
                }
            }

            // Easy would otherwise play the same game every time from the same position.
            if (random != null && bestScore < WinScore - 100)
            {
                var equal = new List<int>();
                foreach (int column in Ordered(board))
                {
                    board.Drop(column);
                    int score = -Negamax(board, 0, -WinScore * 2, WinScore * 2);
                    board.Undo(column);
                    if (score == bestScore) equal.Add(column);
                }
                if (equal.Count > 1 && equal.Contains(best)) best = equal[random.Next(equal.Count)];
            }

            return best;
        }

        IEnumerable<int> Ordered(Connect4Board board)
        {
            foreach (int column in ColumnOrder)
            {
                if (board.IsPlayable(column)) yield return column;
            }
        }

        int Negamax(Connect4Board board, int depth, int alpha, int beta)
        {
            if (++nodesSearched > nodeBudget) throw new BudgetReached();

            Connect4Status status = board.Status;
            if (Connect4Board.IsOver(status))
            {
                if (status == Connect4Status.Draw) return 0;

                // The side to move is the one that just lost, since the winning drop has
                // already flipped the turn. Sooner losses are worse, which is what makes
                // the search take the quickest win and the most stubborn defence.
                return -(WinScore - board.MoveCount);
            }

            if (depth == 0) return Evaluate(board, board.SideToMove);

            int best = int.MinValue;
            foreach (int column in Ordered(board))
            {
                board.Drop(column);
                int score = -Negamax(board, depth - 1, -beta, -alpha);
                board.Undo(column);

                if (score > best) best = score;
                if (best > alpha) alpha = best;
                if (alpha >= beta) break;
            }

            return best == int.MinValue ? 0 : best;
        }

        /// <summary>
        /// Scores a quiet position by counting what every four-cell window is worth.
        ///
        /// A window holding three of ours and a gap is nearly a win; one holding a mix of
        /// both colours can never be completed and is worth nothing. Central columns get a
        /// small bonus because they appear in more windows than the edges do.
        /// </summary>
        static int Evaluate(Connect4Board board, Connect4Player forPlayer)
        {
            int score = 0;
            Connect4Player opponent = Connect4Board.Opponent(forPlayer);

            for (int column = 0; column < Connect4Board.Columns; column++)
            {
                int weight = 3 - Math.Abs(3 - column);
                for (int row = 0; row < Connect4Board.Rows; row++)
                {
                    Connect4Player p = board[column, row];
                    if (p == forPlayer) score += weight;
                    else if (p == opponent) score -= weight;
                }
            }

            score += WindowScore(board, forPlayer, 1, 0);
            score += WindowScore(board, forPlayer, 0, 1);
            score += WindowScore(board, forPlayer, 1, 1);
            score += WindowScore(board, forPlayer, 1, -1);
            return score;
        }

        static int WindowScore(Connect4Board board, Connect4Player forPlayer, int dx, int dy)
        {
            int total = 0;
            Connect4Player opponent = Connect4Board.Opponent(forPlayer);

            for (int column = 0; column < Connect4Board.Columns; column++)
            {
                for (int row = 0; row < Connect4Board.Rows; row++)
                {
                    int endColumn = column + dx * 3;
                    int endRow = row + dy * 3;
                    if (endColumn < 0 || endColumn >= Connect4Board.Columns) continue;
                    if (endRow < 0 || endRow >= Connect4Board.Rows) continue;

                    int mine = 0;
                    int theirs = 0;
                    for (int i = 0; i < 4; i++)
                    {
                        Connect4Player p = board[column + dx * i, row + dy * i];
                        if (p == forPlayer) mine++;
                        else if (p == opponent) theirs++;
                    }

                    // Contested windows can never be completed, so they are worth nothing
                    // to either side and counting them only adds noise.
                    if (mine > 0 && theirs > 0) continue;

                    if (mine == 3) total += 60;
                    else if (mine == 2) total += 12;
                    else if (theirs == 3) total -= 55;
                    else if (theirs == 2) total -= 12;
                }
            }
            return total;
        }
    }
}
