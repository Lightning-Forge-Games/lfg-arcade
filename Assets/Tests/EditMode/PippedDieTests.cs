using LightningForge.Arcade.Game;
using NUnit.Framework;
using UnityEngine;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// The two halves of a die have to agree: the rotation that shows a number, and the
    /// reading of which number a rotation shows. When they drift apart a die silently
    /// changes value the moment it is squared up, which is exactly what happened when
    /// three and four were transposed.
    /// </summary>
    public class PippedDieTests
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void ARotationShowingANumberReadsBackAsThatNumber(int value)
        {
            Assert.AreEqual(value, PippedDie.ValueShowing(PippedDie.RotationShowing(value)));
        }

        [Test]
        public void EveryFaceIsReachableAndDistinct()
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            for (int value = 1; value <= 6; value++)
            {
                Assert.IsTrue(seen.Add(PippedDie.ValueShowing(PippedDie.RotationShowing(value))),
                    "two numbers share a rotation");
            }
            Assert.AreEqual(6, seen.Count);
        }

        [Test]
        public void OppositeFacesSumToSeven()
        {
            // A real die is made this way, and the pip layout assumes it.
            for (int i = 0; i < PippedDie.FaceNormals.Length; i += 2)
            {
                Assert.AreEqual(7, PippedDie.FaceValues[i] + PippedDie.FaceValues[i + 1],
                    PippedDie.FaceNormals[i] + " and its opposite should sum to seven");
            }
        }

        [Test]
        public void ATiltedDieStillReadsTheNearestFace()
        {
            // Dice never settle perfectly square, so the reading has to tolerate a lean.
            Quaternion tilted = Quaternion.Euler(9f, 23f, -7f) * PippedDie.RotationShowing(5);
            Assert.AreEqual(5, PippedDie.ValueShowing(tilted));
        }
    }
}
