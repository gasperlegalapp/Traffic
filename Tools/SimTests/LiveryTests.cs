using System.Linq;
using NUnit.Framework;
using Traffic.Art;

namespace Traffic.Tests
{
    /// <summary>The words painted on the sides of trucks.</summary>
    public class LiveryTests
    {
        [Test]
        public void EveryCompanyNameFitsOnItsTruck()
        {
            Assert.Greater(Livery.Of(false).Count, 3);
            Assert.Greater(Livery.Of(true).Count, 1);
            foreach (Livery livery in Livery.All)
            {
                Assert.LessOrEqual(DotLetters.Width(livery.Name, 2), Livery.Width - 40, livery.Name);
                Assert.LessOrEqual(DotLetters.Width(livery.Slogan, 1), Livery.Width - 40, livery.Slogan);
                uint[] pixels = livery.Paint();
                Assert.AreEqual(Livery.Width * Livery.Height, pixels.Length);
                uint ink = 0xFF000000u | (uint)livery.Ink, back = 0xFF000000u | (uint)livery.Back;
                int written = pixels.Count(p => p == ink), blank = pixels.Count(p => p == back);
                Assert.Greater(written, 1500, "the words are there, and large: " + livery.Name);
                Assert.Greater(blank, written, "on a plain panel");
                // Nothing runs off the edges: the outermost columns and the top rows are clear of ink.
                for (int y = 0; y < Livery.Height - 14; y++)
                {
                    Assert.AreNotEqual(ink, pixels[y * Livery.Width + 3]);
                    Assert.AreNotEqual(ink, pixels[y * Livery.Width + Livery.Width - 4]);
                }
            }
        }

        [Test]
        public void EveryLetterUsedCanBeDrawn()
        {
            // A letter with no shape would leave a gap in a name; each shape is five dots by seven.
            foreach (Livery livery in Livery.All)
            {
                foreach (char c in livery.Name + livery.Slogan)
                {
                    if (c == ' ') continue;
                    var one = new uint[DotLetters.Wide * DotLetters.High];
                    DotLetters.Paint(one, DotLetters.Wide, DotLetters.High, 0, 0, c.ToString(), 1, 1);
                    Assert.Greater(one.Count(p => p == 1), 1, "no shape for '" + c + "'");
                }
            }
            Assert.AreEqual(17, DotLetters.Width("ABC", 1));
        }

    }
}
