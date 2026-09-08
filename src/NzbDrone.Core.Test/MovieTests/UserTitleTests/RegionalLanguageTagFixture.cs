using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.UserTitles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.UserTitleTests
{
    [TestFixture]
    public class RegionalLanguageTagFixture : CoreTest
    {
        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("  ", null)]
        [TestCase("FR", "fr")]
        [TestCase(" fr-CA ", "fr-ca")]
        public void normalize_should_lowercase_and_trim(string input, string expected)
        {
            RegionalLanguageTag.Normalize(input).Should().Be(expected);
        }

        [TestCase("fr", null, "fr")]
        [TestCase("fr", "CA", "fr-ca")]
        [TestCase("FR", " ca ", "fr-ca")]
        [TestCase("de", "", "de")]
        [TestCase(null, "CA", null)]
        public void build_should_produce_the_canonical_shape(string language, string region, string expected)
        {
            RegionalLanguageTag.Build(language, region).Should().Be(expected);
        }

        [TestCase("fr-CA", "fr", "ca")]
        [TestCase("fr", "fr", null)]
        [TestCase("FR-", "fr", null)]
        [TestCase(null, null, null)]
        public void split_should_separate_language_and_region(string tag, string language, string region)
        {
            var (l, r) = RegionalLanguageTag.Split(tag);

            l.Should().Be(language);
            r.Should().Be(region);
        }
    }
}
