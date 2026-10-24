using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionNormalizerFixture : CoreTest
    {
        [TestCase(null, "")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        [TestCase("Extended", "extended")]
        [TestCase("Director's Cut", "directors cut")]
        [TestCase("Director’s Cut", "directors cut")]
        [TestCase("Director‘s Cut", "directors cut")]
        [TestCase("Directors.Cut", "directors cut")]
        [TestCase("Extended-Cut", "extended cut")]
        [TestCase("Open_Matte", "open matte")]
        [TestCase("[Extended]", "extended")]
        [TestCase("(Remastered)", "remastered")]
        [TestCase("{edition-Ulysses Cut}", "edition ulysses cut")]
        [TestCase("  IMAX   Edition  ", "imax edition")]
        [TestCase("Special Edition (Remastered)", "special edition remastered")]
        [TestCase("20th Anniversary Edition", "20th anniversary edition")]
        public void should_normalize(string input, string expected)
        {
            EditionNormalizer.Normalize(input).Should().Be(expected);
        }

        [Test]
        public void should_be_idempotent()
        {
            var once = EditionNormalizer.Normalize("Director's.Cut [Remastered]");

            EditionNormalizer.Normalize(once).Should().Be(once);
        }
    }
}
