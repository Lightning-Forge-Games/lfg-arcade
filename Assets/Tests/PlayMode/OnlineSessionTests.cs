using System;
using System.Collections;
using System.Reflection;
using LightningForge.Chess.Core;
using LightningForge.Chess.Game;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LightningForge.Chess.Tests.PlayMode
{
    /// <summary>
    /// Exercises the online lobby against the real Photon backend.
    ///
    /// The networking scripts live in Assembly-CSharp, which an asmdef cannot reference,
    /// so they are reached by reflection. That is the cost of the Fusion weaver needing
    /// them there; see the notes on ChessNetLink.
    ///
    /// The connecting test is in the Network category because it needs an internet
    /// connection and spends Photon CCU. Exclude the category to skip it.
    /// </summary>
    public class OnlineSessionTests
    {
        const float ConnectTimeout = 25f;
        const float SpawnTimeout = 10f;

        static readonly Type SessionType =
            Type.GetType("LightningForge.Chess.Net.ChessSession, Assembly-CSharp");
        static readonly Type LinkType =
            Type.GetType("LightningForge.Chess.Net.ChessNetLink, Assembly-CSharp");

        MonoBehaviour session;

        static object Get(object target, string property) =>
            target.GetType().GetProperty(property).GetValue(target);

        static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method).Invoke(target, args);

        /// <summary>Polls until the condition holds, or fails the test on timeout.</summary>
        static IEnumerator WaitUntil(Func<bool> condition, float seconds, string what)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline) Assert.Fail("timed out waiting for " + what);
                yield return null;
            }
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (session != null && (bool)Get(session, "IsConnected"))
            {
                Call(session, "Leave");
                yield return WaitUntil(() => !(bool)Get(session, "IsConnected"), 10f, "shutdown");
            }
            session = null;
        }

        [Test]
        public void TheNetworkingTypesAreInAssemblyCSharp()
        {
            // The weaver only reaches RPCs in Assembly-CSharp. If these ever move to their
            // own asmdef, RpcPlayMove throws FieldAccessException on the first call, at
            // runtime, in a real match. Fail here instead.
            Assert.IsNotNull(SessionType, "ChessSession is not in Assembly-CSharp");
            Assert.IsNotNull(LinkType, "ChessNetLink is not in Assembly-CSharp");
        }

        [Test]
        public void GeneratedCodesAreFourUnambiguousCharacters()
        {
            MethodInfo generate = SessionType.GetMethod(
                "GenerateCode", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(generate, "GenerateCode is gone or renamed");

            for (int i = 0; i < 200; i++)
            {
                string code = (string)generate.Invoke(null, null);
                Assert.AreEqual(4, code.Length, "code length");
                foreach (char c in code)
                {
                    Assert.IsTrue("ABCDEFGHJKLMNPQRSTUVWXYZ23456789".IndexOf(c) >= 0,
                        "'" + c + "' is a look-alike or out of alphabet");
                }
            }
        }

        [UnityTest, Category("Network")]
        public IEnumerator HostingAMatchConnectsAndSpawnsTheLink()
        {
            SceneManager.LoadScene("Chess", LoadSceneMode.Single);
            yield return null;
            yield return null;

            session = (MonoBehaviour)UnityEngine.Object.FindFirstObjectByType(SessionType);
            Assert.IsNotNull(session, "no ChessSession in the Chess scene");

            var controller = UnityEngine.Object.FindFirstObjectByType<ChessGameController>();
            Assert.IsNotNull(controller, "no ChessGameController in the Chess scene");
            Assert.AreEqual(ControlMode.Both, controller.Control, "should start in hot seat");

            Call(session, "CreateMatch");

            yield return WaitUntil(() => !(bool)Get(session, "IsConnecting"),
                ConnectTimeout, "the connect to settle");

            string error = (string)Get(session, "LastError");
            Assert.IsNull(error, "connect reported: " + error);
            Assert.IsTrue((bool)Get(session, "IsConnected"), "runner is not running");

            string code = (string)Get(session, "MatchCode");
            Assert.IsFalse(string.IsNullOrEmpty(code), "connected without a match code");
            Debug.Log("Test hosted match " + code);

            // The link is spawned by the master client and arrives a few frames later.
            yield return WaitUntil(() => UnityEngine.Object.FindFirstObjectByType(LinkType) != null,
                SpawnTimeout, "ChessNetLink to spawn");

            object link = UnityEngine.Object.FindFirstObjectByType(LinkType);
            Assert.IsTrue((bool)Get(link, "IsWhite"), "the host should play White");
            Assert.AreEqual(ControlMode.WhiteOnly, controller.Control,
                "the host should only be able to move White");

            var rig = UnityEngine.Object.FindFirstObjectByType<BoardCameraRig>();
            if (rig != null) Assert.AreEqual(PieceColor.White, rig.Viewpoint, "camera side");

            // Playing a move drives MoveMade into RpcPlayMove. A weaving problem shows up
            // as a FieldAccessException on the very first RPC and nowhere earlier, so make
            // a move rather than trusting that the connection alone proves anything. The
            // RPC targets all peers including this one, which drops it on arrival because
            // the sender already played it, so the board must be left holding exactly the
            // one move.
            Assert.IsTrue(controller.TryPlayUci("e2e4"), "e2e4 should be legal from the start");
            yield return null;
            yield return null;

            Assert.AreEqual(PieceColor.Black, controller.Board.SideToMove,
                "the local move should have been applied once, not echoed back");

            // Leaving must hand the board back, or the player is stuck after a disconnect.
            Call(session, "Leave");
            yield return WaitUntil(() => !(bool)Get(session, "IsConnected"), 10f, "shutdown");
            yield return null;

            Assert.AreEqual(ControlMode.Both, controller.Control, "leaving should restore hot seat");

            // Fusion destroys the runner's GameObject on shutdown. If the runner shares an
            // object with the lobby, leaving one match takes the whole lobby with it and
            // there is no way into a second one.
            Assert.IsFalse(session == null, "leaving destroyed the ChessSession");
            Assert.IsFalse(UnityEngine.Object.FindFirstObjectByType(
                Type.GetType("LightningForge.Chess.Net.ChessOnlineHud, Assembly-CSharp")) == null,
                "leaving destroyed the lobby UI");

            // And a second match must still be reachable.
            Call(session, "CreateMatch");
            yield return WaitUntil(() => !(bool)Get(session, "IsConnecting"),
                ConnectTimeout, "the second connect to settle");
            Assert.IsTrue((bool)Get(session, "IsConnected"), "could not host again after leaving");
        }
    }
}
