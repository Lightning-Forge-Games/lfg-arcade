using System;
using System.Collections.Generic;

namespace LightningForge.Arcade.Core.Backgammon
{
    /// <summary>
    /// The backgammon opponent.
    ///
    /// Backgammon does not reward deep search the way chess does: the dice mean the tree
    /// branches by twenty one rolls at every ply, so looking two moves ahead costs a great
    /// deal and tells you little. Real engines learn an evaluation instead. This scores the
    /// positions one turn produces and picks the best, which is enough to punish loose play
    /// and leave a game that feels like backgammon.
    ///
    /// Difficulty changes how much of the evaluation is used rather than how deep it looks:
    /// Easy races blindly, Medium adds safety, Hard also builds points and holds anchors.
    /// </summary>
    public sealed class BackgammonPlayer
    {
        readonly Random random;

        public BackgammonPlayer(Random random)
        {
            this.random = random ?? new Random(0);
        }

        public List<BackgammonMove> ChooseTurn(BackgammonBoard board, int first, int second,
            Difficulty difficulty)
        {
            List<List<BackgammonMove>> turns = board.GenerateTurns(first, second);
            if (turns.Count == 0) return new List<BackgammonMove>();
            if (turns.Count == 1) return turns[0];

            var best = new List<List<BackgammonMove>>();
            double bestScore = double.NegativeInfinity;

            foreach (List<BackgammonMove> turn in turns)
            {
                BackgammonBoard next = board.Clone();
                foreach (BackgammonMove move in turn) next.ApplyMove(move);

                double score = Evaluate(next, board.SideToMove, difficulty);
                if (score > bestScore + 0.0001)
                {
                    bestScore = score;
                    best.Clear();
                    best.Add(turn);
                }
                else if (score > bestScore - 0.0001)
                {
                    best.Add(turn);
                }
            }

            // Easy is meant to be beatable, so it takes a random turn from the top group
            // rather than always the same one, and often that group is large.
            return best[random.Next(best.Count)];
        }

        /// <summary>
        /// Scores a position from one side's point of view. Higher is better.
        ///
        /// The race term is always there: backgammon is at bottom a race, and a player who
        /// only ever reduces their pip count is already playing a recognisable game. The
        /// rest is what separates the settings.
        /// </summary>
        static double Evaluate(BackgammonBoard board, BackgammonSide side, Difficulty difficulty)
        {
            BackgammonSide other = BackgammonBoard.Opponent(side);

            // Being ahead in the race is good, so a lower pip count than the opponent scores.
            double score = board.Pip(other) - board.Pip(side);

            score += (board.Off(side) - board.Off(other)) * 12.0;

            if (difficulty == Difficulty.Easy) return score;

            // Checkers sent back have to come all the way round again.
            score += (board.Bar(other) - board.Bar(side)) * 18.0;

            // A lone checker can be hit, which is the single most expensive thing that
            // happens in a game. Blots deep in the opponent's half are the riskiest.
            for (int point = 0; point < BackgammonBoard.Points; point++)
            {
                BackgammonPoint p = board[point];
                if (p.Count != 1) continue;

                int exposure = p.Side == BackgammonSide.White ? 24 - point : point + 1;
                double penalty = 2.0 + exposure * 0.35;
                score += p.Side == side ? -penalty : penalty * 0.6;
            }

            if (difficulty != Difficulty.Hard) return score;

            for (int point = 0; point < BackgammonBoard.Points; point++)
            {
                BackgammonPoint p = board[point];
                if (p.Count < 2) continue;

                // Made points block the opponent, and they are worth most in the home board
                // and around the bar where they cut off escape routes.
                double value = 3.0;
                if (BackgammonBoard.IsHome(p.Side, point)) value += 4.0;

                int fromBar = p.Side == BackgammonSide.White ? Math.Abs(point - 4) : Math.Abs(point - 19);
                value += Math.Max(0, 5 - fromBar) * 0.8;

                // Stacking more than three on a point wastes them.
                if (p.Count > 3) value -= (p.Count - 3) * 1.2;

                score += p.Side == side ? value : -value;
            }

            return score;
        }
    }
}
