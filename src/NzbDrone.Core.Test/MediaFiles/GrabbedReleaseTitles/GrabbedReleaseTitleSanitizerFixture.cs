using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class GrabbedReleaseTitleSanitizerFixture : CoreTest
    {
        [TestCase("Movie.2004.1080p.WEBRip.x264-GRP", "Movie.2004.1080p.WEBRip.x264-GRP")]
        [TestCase("  Movie.2004.1080p-GRP  ", "Movie.2004.1080p-GRP")]
        [TestCase("Movie.2004-GRP\nTaille: 4 GB Seeders: 27", "Movie.2004-GRP")]
        [TestCase("Movie.2004-GRP\r\n\t\r\n\tTaille: 4 GB", "Movie.2004-GRP")]
        [TestCase("\n\t\nMovie.2004-GRP\nnoise", "Movie.2004-GRP")]
        [TestCase("Movie   2004\t\t1080p", "Movie 2004 1080p")]
        public void should_keep_the_first_non_empty_line_with_collapsed_whitespace(string title, string expected)
        {
            GrabbedReleaseTitleSanitizer.Sanitize(title).Should().Be(expected);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\r\n\t")]
        public void should_return_null_when_nothing_is_left(string title)
        {
            GrabbedReleaseTitleSanitizer.Sanitize(title).Should().BeNull();
        }
    }
}
