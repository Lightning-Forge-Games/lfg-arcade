using System.Globalization;
using System.Threading;
using LightningForge.Arcade.Core.Yahtzee;
using NUnit.Framework;

namespace LightningForge.Arcade.Tests
{
    /// <summary>
    /// The wire format for online Yahtzee.
    ///
    /// This is the one part of the online path that can be tested without two machines and
    /// a relay between them, and it is also the part most likely to break quietly: a
    /// message that decodes into the wrong thing shows up as a table that disagrees with
    /// itself rather than as an error.
    /// </summary>
    public class YahtzeeWireTests
    {
        static YahtzeeMessage Parse(string text)
        {
            Assert.IsTrue(YahtzeeWire.TryParse(text, out YahtzeeMessage message),
                "failed to parse '" + text + "'");
            return message;
        }

        [Test]
        public void CupLiftedSurvivesTheRoundTrip()
        {
            Assert.AreEqual(YahtzeeMessageKind.CupLifted, Parse(YahtzeeWire.CupLifted()).Kind);
        }

        [Test]
        public void AThrowCarriesEveryDiceValueAndPlace()
        {
            var sent = new[]
            {
                new YahtzeeLandedDie { X = -1.24f, Z = 0.5f, Value = 3 },
                new YahtzeeLandedDie { X = 0f, Z = -0.83f, Value = 6 },
                new YahtzeeLandedDie { X = 2.05f, Z = 1.4f, Value = 1 },
                new YahtzeeLandedDie { X = -3.5f, Z = 0f, Value = 4 },
                new YahtzeeLandedDie { X = 0.07f, Z = -1.61f, Value = 5 },
            };

            YahtzeeMessage message = Parse(YahtzeeWire.Thrown(sent));

            Assert.AreEqual(YahtzeeMessageKind.Thrown, message.Kind);
            Assert.AreEqual(sent.Length, message.Landed.Length);
            for (int i = 0; i < sent.Length; i++)
            {
                Assert.AreEqual(sent[i].Value, message.Landed[i].Value, "value " + i);
                // Positions travel as hundredths, so this is the full precision available
                // and half a centimetre on a five unit tray is not a place anyone can see.
                Assert.AreEqual(sent[i].X, message.Landed[i].X, 0.005f, "x " + i);
                Assert.AreEqual(sent[i].Z, message.Landed[i].Z, 0.005f, "z " + i);
            }
        }

        [Test]
        public void KeepFlagsKeepTheirOrder()
        {
            YahtzeeMessage message = Parse(YahtzeeWire.Kept(new[] { true, false, false, true, true }));

            Assert.AreEqual(YahtzeeMessageKind.Kept, message.Kind);
            CollectionAssert.AreEqual(new[] { true, false, false, true, true }, message.Held);
        }

        [Test]
        public void AScoreCarriesTheBoxAndTheDiceInIt()
        {
            YahtzeeMessage message =
                Parse(YahtzeeWire.Scored(YahtzeeCategory.FullHouse, new[] { 2, 2, 5, 5, 5 }));

            Assert.AreEqual(YahtzeeMessageKind.Scored, message.Kind);
            Assert.AreEqual(YahtzeeCategory.FullHouse, message.Category);
            CollectionAssert.AreEqual(new[] { 2, 2, 5, 5, 5 }, message.Dice);
        }

        [Test]
        public void EveryBoxSurvivesTheRoundTrip()
        {
            for (int i = 0; i < YahtzeeScorecard.CategoryCount; i++)
            {
                var category = (YahtzeeCategory)i;
                YahtzeeMessage message =
                    Parse(YahtzeeWire.Scored(category, new[] { 1, 2, 3, 4, 5 }));
                Assert.AreEqual(category, message.Category);
            }
        }

        [Test]
        public void TheFourKindsAreToldApart()
        {
            Assert.AreEqual(YahtzeeMessageKind.CupLifted, Parse("c").Kind);
            Assert.AreEqual(YahtzeeMessageKind.Thrown, Parse("t|0,0,1").Kind);
            Assert.AreEqual(YahtzeeMessageKind.Kept, Parse("k|00000").Kind);
            Assert.AreEqual(YahtzeeMessageKind.Scored, Parse("s|0|11111").Kind);
        }

        [TestCase("")]
        [TestCase(null)]
        [TestCase("x")]
        [TestCase("t")]
        [TestCase("t|0,0")]
        [TestCase("t|0,0,7")]
        [TestCase("t|0,0,0")]
        [TestCase("t|nope,0,1")]
        [TestCase("k")]
        [TestCase("k|")]
        [TestCase("k|0102")]
        [TestCase("s|0")]
        [TestCase("s|0|")]
        [TestCase("s|99|11111")]
        [TestCase("s|-1|11111")]
        [TestCase("s|0|11711")]
        public void RubbishIsRejectedRatherThanGuessedAt(string text)
        {
            // A rejected message is reported as divergence by the net link, which is the
            // right end for it: silently accepting half a message would leave the two
            // tables disagreeing with nothing to say why.
            Assert.IsFalse(YahtzeeWire.TryParse(text, out _));
        }

        [Test]
        public void PositionsDoNotDependOnTheSendersNumberFormatting()
        {
            // A German or French machine writes 1,5 rather than 1.5, and a decimal point
            // that means a thousands separator at the other end would put a die metres
            // from where it landed. Whole numbers of hundredths have no such reading.
            CultureInfo original = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                string encoded = YahtzeeWire.Thrown(
                    new[] { new YahtzeeLandedDie { X = -1.5f, Z = 2.25f, Value = 4 } });

                Thread.CurrentThread.CurrentCulture = CultureInfo.InvariantCulture;
                YahtzeeMessage message = Parse(encoded);

                Assert.AreEqual(-1.5f, message.Landed[0].X, 0.005f);
                Assert.AreEqual(2.25f, message.Landed[0].Z, 0.005f);
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Test]
        public void NegativeAndPositivePlacesRoundTheSameWay()
        {
            string encoded = YahtzeeWire.Thrown(new[]
            {
                new YahtzeeLandedDie { X = 0.005f, Z = -0.005f, Value = 1 },
            });

            YahtzeeMessage message = Parse(encoded);
            Assert.AreEqual(0.01f, message.Landed[0].X, 0.0001f);
            Assert.AreEqual(-0.01f, message.Landed[0].Z, 0.0001f);
        }
    }
}
