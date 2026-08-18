using System;
using System.Collections.Generic;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Backgammon;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// The rules that are easy to get wrong and expensive to get wrong: entering from the
    /// bar before anything else, using as many dice as possible, using the higher die when
    /// only one will go, and the conditions on bearing off.
    ///
    /// Positions are written as twenty four comma separated points, a dash for empty, then
    /// the bars and the checkers already borne off.
    /// </summary>
    public class BackgammonTests
    {
        const string Empty =
            "-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:0/0 off:0/0";

        static string Turn(List<BackgammonMove> moves)
        {
            var parts = new List<string>();
            foreach (BackgammonMove move in moves) parts.Add(move.ToString());
            return string.Join(" ", parts);
        }

        [Test]
        public void TheOpeningPositionHasFifteenCheckersEach()
        {
            var board = new BackgammonBoard();

            int white = 0;
            int black = 0;
            for (int i = 0; i < BackgammonBoard.Points; i++)
            {
                if (board[i].Side == BackgammonSide.White) white += board[i].Count;
                if (board[i].Side == BackgammonSide.Black) black += board[i].Count;
            }

            Assert.AreEqual(BackgammonBoard.CheckersPerSide, white);
            Assert.AreEqual(BackgammonBoard.CheckersPerSide, black);
            Assert.AreEqual(167, board.Pip(BackgammonSide.White), "standard opening pip count");
            Assert.AreEqual(167, board.Pip(BackgammonSide.Black), "both sides start level");
        }

        [Test]
        public void DoublesArePlayedFourTimes()
        {
            CollectionAssert.AreEqual(new[] { 3, 3, 3, 3 }, BackgammonBoard.DiceFor(3, 3));
            CollectionAssert.AreEqual(new[] { 5, 2 }, BackgammonBoard.DiceFor(5, 2));
        }

        [Test]
        public void APointWithTwoOpposingCheckersIsClosed()
        {
            // White on point 0 with a 5 would land on point 5, which Black owns with five.
            var board = BackgammonBoard.Parse(
                "w1,-,-,-,-,b5,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:0/0 off:0/0",
                BackgammonSide.White);

            foreach (BackgammonMove move in board.MovesForDie(5))
            {
                Assert.AreNotEqual(5, move.To, "point 5 is blocked by two or more checkers");
            }
        }

        [Test]
        public void ALoneCheckerIsHitAndGoesToTheBar()
        {
            var board = BackgammonBoard.Parse(
                "w1,-,-,b1,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:0/0 off:0/0",
                BackgammonSide.White);

            List<BackgammonMove> moves = board.MovesForDie(3);
            Assert.AreEqual(1, moves.Count);
            Assert.IsTrue(moves[0].Hits, "landing on a blot should be a hit");

            board.ApplyMove(moves[0]);
            Assert.AreEqual(1, board.Bar(BackgammonSide.Black), "the blot should be on the bar");
            Assert.AreEqual(BackgammonSide.White, board[3].Side);
        }

        [Test]
        public void CheckersOnTheBarMustComeInFirst()
        {
            // White has a checker on the bar and another on the board. Only the entry is legal.
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,-,w2,-,-,-,-,-,-,-,-,-,-,-,- bar:1/0 off:0/0",
                BackgammonSide.White);

            List<BackgammonMove> moves = board.MovesForDie(3);
            Assert.AreEqual(1, moves.Count, "nothing but the entry may move");
            Assert.AreEqual(BackgammonMove.Bar, moves[0].From);
            Assert.AreEqual(2, moves[0].To, "a 3 enters on point 3 for White");
        }

        [Test]
        public void AnEntryOntoAClosedPointIsRefused()
        {
            var board = BackgammonBoard.Parse(
                "-,-,b2,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:1/0 off:0/0",
                BackgammonSide.White);

            Assert.AreEqual(0, board.MovesForDie(3).Count, "point 2 is closed, so the 3 is dead");
        }

        [Test]
        public void APlayerMustUseBothDiceWhenPossible()
        {
            var board = new BackgammonBoard();
            List<List<BackgammonMove>> turns = board.GenerateTurns(3, 1);

            foreach (List<BackgammonMove> turn in turns)
            {
                Assert.AreEqual(2, turn.Count, "both dice are playable from the opening");
            }
        }

        [Test]
        public void TheHigherDieMustBeUsedWhenOnlyOneWillGo()
        {
            // One white checker on point 10, nowhere near home so nothing can bear off, and
            // Black holding point 17. Either die can be played on its own, to 12 or to 15,
            // but both together would need to reach 17, which is closed. Only one die can
            // be used, so the rules require it to be the higher one.
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,w1,-,-,-,-,-,-,b2,-,-,-,-,-,- bar:0/0 off:0/0",
                BackgammonSide.White);

            Assert.IsFalse(board.AllHome(BackgammonSide.White), "nothing may bear off here");
            Assert.AreEqual(1, board.MovesForDie(2).Count, "the 2 is playable on its own");
            Assert.AreEqual(1, board.MovesForDie(5).Count, "so is the 5");

            List<List<BackgammonMove>> turns = board.GenerateTurns(5, 2);
            Assert.AreEqual(1, turns.Count, "one way to play, got: "
                + string.Join(" | ", turns.ConvertAll(Turn)));
            Assert.AreEqual(1, turns[0].Count, "both dice cannot be used");
            Assert.AreEqual(5, turns[0][0].Die, "the higher die must be the one played");
        }

        [Test]
        public void ARollThatCannotBePlayedAtAllYieldsAnEmptyTurn()
        {
            // White is on the bar and every entry point is closed.
            var board = BackgammonBoard.Parse(
                "b2,b2,b2,b2,b2,b2,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:1/0 off:0/0",
                BackgammonSide.White);

            List<List<BackgammonMove>> turns = board.GenerateTurns(4, 2);
            Assert.AreEqual(1, turns.Count);
            Assert.AreEqual(0, turns[0].Count, "a blocked player forfeits the roll");
        }

        [Test]
        public void BearingOffNeedsEveryCheckerHome()
        {
            // One checker still outside the home board, so nothing may come off.
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,w1,-,-,-,-,-,-,-,-,-,-,-,-,w1 bar:0/0 off:0/0",
                BackgammonSide.White);

            Assert.IsFalse(board.AllHome(BackgammonSide.White));
            foreach (BackgammonMove move in board.MovesForDie(1))
            {
                Assert.AreNotEqual(BackgammonMove.Off, move.To, "cannot bear off yet");
            }
        }

        [Test]
        public void AnExactRollBearsOff()
        {
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,w2 bar:0/0 off:0/13",
                BackgammonSide.White);

            Assert.IsTrue(board.AllHome(BackgammonSide.White));
            List<BackgammonMove> moves = board.MovesForDie(1);
            Assert.AreEqual(1, moves.Count);
            Assert.AreEqual(BackgammonMove.Off, moves[0].To, "point 23 is one pip from off");
        }

        [Test]
        public void AHighDieOnlyBearsOffFromTheRearmostPoint()
        {
            // White has checkers on 20 and 23. A 6 would reach past point 18, but only the
            // rearmost checker may use it, otherwise the roll wastes the one behind.
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,w1,-,-,w1 bar:0/0 off:0/13",
                BackgammonSide.White);

            List<BackgammonMove> moves = board.MovesForDie(6);
            Assert.AreEqual(1, moves.Count, "only one checker may use the high die");
            Assert.AreEqual(20, moves[0].From, "it must be the one furthest from home");
        }

        [Test]
        public void BearingOffTheLastCheckerWins()
        {
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,w1 bar:0/0 off:14/0",
                BackgammonSide.White);

            board.ApplyMove(board.MovesForDie(1)[0]);
            Assert.AreEqual(BackgammonStatus.WhiteWins, board.Status);
            Assert.IsTrue(BackgammonBoard.IsOver(board.Status));
        }

        [Test]
        public void BlackRunsTheOtherWay()
        {
            var board = BackgammonBoard.Parse(
                "-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,b1 bar:0/0 off:0/0",
                BackgammonSide.Black);

            List<BackgammonMove> moves = board.MovesForDie(4);
            Assert.AreEqual(1, moves.Count);
            Assert.AreEqual(19, moves[0].To, "Black counts downwards");
        }

        [Test]
        public void TurnsAreNotOfferedTwiceForTheSameResult()
        {
            // Playing 3 then 1 with one checker reaches the same place as 1 then 3.
            var board = BackgammonBoard.Parse(
                "w1,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,-,- bar:0/0 off:0/0",
                BackgammonSide.White);

            List<List<BackgammonMove>> turns = board.GenerateTurns(3, 1);
            var endings = new HashSet<string>();
            foreach (List<BackgammonMove> turn in turns) endings.Add(Turn(turn));

            Assert.AreEqual(turns.Count, endings.Count, "duplicate sequences should be filtered");
        }

        // The opponent ------------------------------------------------------------

        [Test]
        public void TheOpponentOnlyEverReturnsALegalTurn()
        {
            var random = new Random(5);
            var player = new BackgammonPlayer(random);
            var board = new BackgammonBoard();

            int turns = 0;
            while (!BackgammonBoard.IsOver(board.Status) && turns++ < 200)
            {
                int a = random.Next(1, 7);
                int b = random.Next(1, 7);

                List<List<BackgammonMove>> legal = board.GenerateTurns(a, b);
                List<BackgammonMove> chosen = player.ChooseTurn(board, a, b, Difficulty.Medium);

                bool matches = false;
                foreach (List<BackgammonMove> option in legal)
                {
                    if (Turn(option) == Turn(chosen)) { matches = true; break; }
                }
                Assert.IsTrue(matches, "chose a turn that was not legal: " + Turn(chosen));

                board.ApplyTurn(chosen);
            }
        }

        [Test]
        public void AGameBetweenOpponentsFinishes()
        {
            // Backgammon always terminates, so a game that does not is a bug in bearing off
            // or in whose turn it is rather than bad luck.
            var random = new Random(9);
            var player = new BackgammonPlayer(random);
            var board = new BackgammonBoard();

            int turns = 0;
            while (!BackgammonBoard.IsOver(board.Status))
            {
                Assert.Less(turns++, 600, "the game should have finished by now");
                board.ApplyTurn(player.ChooseTurn(board, random.Next(1, 7), random.Next(1, 7),
                    Difficulty.Hard));
            }

            Assert.AreEqual(BackgammonBoard.CheckersPerSide,
                Math.Max(board.Off(BackgammonSide.White), board.Off(BackgammonSide.Black)));
        }

        [Test]
        public void TheHarderOpponentWinsMoreOften()
        {
            // Not a claim about any single game; the dice decide too much for that. Over a
            // run, Hard playing the full evaluation should beat Easy racing blindly.
            int hardWins = 0;
            const int games = 30;

            for (int seed = 0; seed < games; seed++)
            {
                var random = new Random(seed);
                var player = new BackgammonPlayer(random);
                var board = new BackgammonBoard();

                int turns = 0;
                while (!BackgammonBoard.IsOver(board.Status) && turns++ < 600)
                {
                    Difficulty difficulty = board.SideToMove == BackgammonSide.White
                        ? Difficulty.Hard
                        : Difficulty.Easy;
                    board.ApplyTurn(player.ChooseTurn(board, random.Next(1, 7), random.Next(1, 7),
                        difficulty));
                }

                if (board.Status == BackgammonStatus.WhiteWins) hardWins++;
            }

            Assert.Greater(hardWins, games / 2,
                "Hard won only " + hardWins + " of " + games + " against Easy");
        }
    }
}
