using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using LightningForge.Arcade.Core;
using LightningForge.Arcade.Core.Yahtzee;
using LightningForge.Arcade.Game;
using LightningForge.Arcade.Game.Yahtzee;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace LightningForge.Arcade.Tests.PlayMode
{
    /// <summary>
    /// Two Yahtzee tables wired to each other, which is what an online match is once Photon
    /// has done its part.
    ///
    /// The relay itself is proven against the real backend elsewhere; what is worth testing
    /// here is the thing Photon cannot tell you, which is whether the other player's table
    /// actually does anything while a turn is being taken. The whole turn used to arrive as
    /// one message at the moment a box was filled, and every assertion below would have
    /// passed except the ones about the table moving before that.
    /// </summary>
    public class YahtzeeOnlineTests
    {
        const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;

        YahtzeeGame first;
        YahtzeeGame second;
        bool relaying;

        static T Field<T>(YahtzeeGame game, string name) =>
            (T)typeof(YahtzeeGame).GetField(name, Hidden).GetValue(game);

        static void Invoke(YahtzeeGame game, string name, params object[] args) =>
            typeof(YahtzeeGame).GetMethod(name, Hidden).Invoke(game, args);

        static int[] Dice(YahtzeeGame game) => Field<int[]>(game, "dice");
        static List<YahtzeeDie> Views(YahtzeeGame game) => Field<List<YahtzeeDie>>(game, "dieViews");
        static Transform Cup(YahtzeeGame game) => Field<Transform>(game, "cup");
        static YahtzeeScorecard[] Cards(YahtzeeGame game) => Field<YahtzeeScorecard[]>(game, "cards");
        static int Seat(YahtzeeGame game) => Field<int>(game, "seat");
        static bool Busy(YahtzeeGame game) =>
            Field<Coroutine>(game, "rolling") != null || Field<Coroutine>(game, "pump") != null;

        static YahtzeeGame Build(string name, ControlMode control)
        {
            var go = new GameObject(name);
            YahtzeeGame game = go.AddComponent<YahtzeeGame>();
            game.Begin(new GameSetup { Mode = GameMode.Online, Control = control });
            return game;
        }

        [SetUp]
        public void SetUp()
        {
            // The two clients each hold the first or second seat, exactly as the link
            // assigns them once it spawns.
            first = Build("Yahtzee A", ControlMode.WhiteOnly);
            second = Build("Yahtzee B", ControlMode.BlackOnly);

            first.MovePlayed += encoded => Relay(second, encoded);
            second.MovePlayed += encoded => Relay(first, encoded);
        }

        /// <summary>
        /// Hands a message to the other table, refusing to let an applied message bounce
        /// straight back. The real link does the same, for the same reason.
        /// </summary>
        void Relay(YahtzeeGame target, string encoded)
        {
            if (relaying) return;
            relaying = true;
            try
            {
                Assert.IsTrue(target.ApplyRemoteMove(encoded),
                    "the other table rejected '" + encoded + "'");
            }
            finally
            {
                relaying = false;
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (first != null) UnityEngine.Object.DestroyImmediate(first.gameObject);
            if (second != null) UnityEngine.Object.DestroyImmediate(second.gameObject);
        }

        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        IEnumerator BothIdle()
        {
            yield return WaitUntil(() => !Busy(first) && !Busy(second), 25f, "both tables to settle");
            yield return null;
        }

        [UnityTest]
        public IEnumerator TheOpponentsTableMovesWhileTheyAreStillRolling()
        {
            Vector3 restingCup = Cup(second).position;
            Invoke(first, "Roll");

            // The point of the whole exercise. The first player has not scored anything and
            // will not for several seconds, and the second player's cup is already up.
            yield return WaitUntil(() => (Cup(second).position - restingCup).sqrMagnitude > 0.01f,
                3f, "the opponent's cup to lift");

            Assert.AreEqual(0, Cards(second)[0].Total,
                "nothing should have been scored yet: this is mid turn");

            yield return BothIdle();
        }

        [UnityTest]
        public IEnumerator AThrowLandsOnTheSameNumbersAtBothEnds()
        {
            Invoke(first, "Roll");
            yield return BothIdle();

            CollectionAssert.AreEqual(Dice(first), Dice(second),
                "the two tables disagree about what was rolled");

            // Not just the recorded numbers: the dice themselves have to be showing them,
            // because the faces are what the player reads.
            List<YahtzeeDie> views = Views(second);
            for (int i = 0; i < views.Count; i++)
            {
                Assert.AreEqual(Dice(first)[i], views[i].Value,
                    "die " + i + " on the opponent's table is showing the wrong face");
            }
        }

        [UnityTest]
        public IEnumerator ThrownDiceScatterRatherThanLiningUp()
        {
            Invoke(first, "Roll");
            yield return BothIdle();

            // A remote throw used to end with the dice in a tidy row, which read as five
            // dice being placed rather than thrown. They should land where they landed.
            List<YahtzeeDie> here = Views(first);
            List<YahtzeeDie> there = Views(second);
            for (int i = 0; i < here.Count && i < there.Count; i++)
            {
                Vector3 a = here[i].transform.position;
                Vector3 b = there[i].transform.position;
                Assert.AreEqual(a.x, b.x, 0.02f, "die " + i + " landed somewhere else in x");
                Assert.AreEqual(a.z, b.z, 0.02f, "die " + i + " landed somewhere else in z");
            }
        }

        [UnityTest]
        public IEnumerator KeepingADiceShowsUpOnTheOtherTable()
        {
            Invoke(first, "Roll");
            yield return BothIdle();

            Views(first)[0].SetHeld(true);
            Views(first)[3].SetHeld(true);
            Invoke(first, "LiftHeldDice");
            Invoke(first, "RelayKeeps");
            yield return BothIdle();

            var expected = new[] { true, false, false, true, false };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.AreEqual(expected[i], Views(second)[i].Held, "die " + i);
                Assert.AreEqual(expected[i], Views(second)[i].OnRail,
                    "die " + i + " is not where a kept die belongs");
            }
        }

        [UnityTest]
        public IEnumerator ARerollKeepsTheHeldNumbersAtBothEnds()
        {
            Invoke(first, "Roll");
            yield return BothIdle();

            int keptFace = Views(first)[1].Value;
            Views(first)[1].SetHeld(true);
            Invoke(first, "LiftHeldDice");
            Invoke(first, "RelayKeeps");
            yield return BothIdle();

            Invoke(first, "Roll");
            yield return BothIdle();

            Assert.AreEqual(keptFace, Views(first)[1].Value, "the kept die was rerolled");
            CollectionAssert.AreEqual(Dice(first), Dice(second),
                "the two tables disagree after a reroll");
            Assert.AreEqual(keptFace, Views(second)[1].Value,
                "the opponent's kept die is showing something else");
        }

        [UnityTest]
        public IEnumerator ScoringFillsTheBoxAndPassesTheTurnOnBothTables()
        {
            Invoke(first, "Roll");
            yield return BothIdle();

            int expected = YahtzeeScorecard.ScoreFor(YahtzeeCategory.Chance, Dice(first));
            Invoke(first, "Score", YahtzeeCategory.Chance, true);
            yield return BothIdle();

            Assert.AreEqual(expected, Cards(first)[0][YahtzeeCategory.Chance],
                "the box was not filled here");
            Assert.AreEqual(expected, Cards(second)[0][YahtzeeCategory.Chance],
                "the box was not filled on the opponent's card");
            Assert.AreEqual(1, Seat(first), "the turn did not pass here");
            Assert.AreEqual(1, Seat(second), "the turn did not pass on the opponent's table");
        }

        [UnityTest]
        public IEnumerator MessagesArrivingTogetherAreStillPlayedInOrder()
        {
            // A slow frame or a burst of traffic can deliver several messages at once.
            // Applying them the moment they land would let a throw stow the very dice the
            // keep before it had just put on the rail, so they queue instead.
            Invoke(first, "Roll");
            yield return BothIdle();

            var held = new[] { false, true, true, false, false };
            var landed = new YahtzeeLandedDie[5];
            for (int i = 0; i < landed.Length; i++)
            {
                landed[i] = new YahtzeeLandedDie
                {
                    X = -2.4f + i * 0.5f,
                    Z = 0.4f,
                    // A kept die is not rerolled, so it arrives showing what it already was.
                    Value = held[i] ? Views(second)[i].Value : 6,
                };
            }

            second.ApplyRemoteMove(YahtzeeWire.Kept(held));
            second.ApplyRemoteMove(YahtzeeWire.Thrown(landed));
            yield return BothIdle();

            for (int i = 0; i < held.Length; i++)
            {
                Assert.AreEqual(held[i], Views(second)[i].Held, "die " + i);
                Assert.AreEqual(held[i], Views(second)[i].OnRail,
                    "die " + i + " ended up in the wrong place");
                Assert.AreEqual(landed[i].Value, Views(second)[i].Value,
                    "die " + i + " is showing the wrong face");
            }
        }

        [UnityTest]
        public IEnumerator ADisconnectMidThrowLeavesAPlayableTable()
        {
            Invoke(first, "Roll");

            // Caught deliberately early, while the dice are still hidden inside the cup and
            // the opponent's copy of the throw is only just starting.
            yield return WaitUntil(() => Field<Coroutine>(second, "pump") != null,
                3f, "the opponent's table to start the throw");
            second.ReleaseOnlineSide();
            yield return null;

            Assert.IsNull(Field<Coroutine>(second, "pump"), "the opponent's turn is still playing");
            foreach (YahtzeeDie die in Views(second))
            {
                Assert.IsTrue(die.GetComponentInChildren<MeshRenderer>().enabled,
                    "a die was left invisible inside the cup");
            }

            yield return WaitUntil(() => !Busy(first), 25f, "the first table to settle");
        }

        [UnityTest]
        public IEnumerator ATableWithNoOpponentSaysNothing()
        {
            // The computer opponent and the hot seat share every one of these code paths,
            // and a game that relayed in those modes would be talking to nobody at best.
            var sent = new List<string>();
            var solo = Build("Yahtzee Solo", ControlMode.Both);
            solo.Begin(new GameSetup
            {
                Mode = GameMode.HotSeat,
                Control = ControlMode.Both,
                Difficulty = Difficulty.Easy,
            });
            solo.MovePlayed += sent.Add;

            Invoke(solo, "Roll");
            yield return WaitUntil(() => !Busy(solo), 25f, "the solo table to settle");
            Invoke(solo, "Score", YahtzeeCategory.Chance, true);
            yield return null;

            CollectionAssert.IsEmpty(sent, "a hot seat game relayed: " + string.Join(", ", sent));
            UnityEngine.Object.DestroyImmediate(solo.gameObject);
        }
    }
}
