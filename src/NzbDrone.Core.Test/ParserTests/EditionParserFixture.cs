using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class EditionParserFixture : CoreTest
    {
        [TestCase("Movie Title 2012 Directors Cut", "Directors Cut")]
        [TestCase("Movie Title 1999 (Despecialized).mkv", "Despecialized")]
        [TestCase("Movie Title.2012.(Special.Edition.Remastered).[Bluray-1080p].mkv", "Special Edition Remastered")]
        [TestCase("Movie Title 2012 Extended", "Extended")]
        [TestCase("Movie Title 2012 Extended Directors Cut Fan Edit", "Extended Directors Cut Fan Edit")]
        [TestCase("Movie Title 2012 Director's Cut", "Director's Cut")]
        [TestCase("Movie Title 2012 Directors Cut", "Directors Cut")]
        [TestCase("Movie Title.2012.(Extended.Theatrical.Version.IMAX).BluRay.1080p.2012.asdf", "Extended Theatrical Version IMAX")]
        [TestCase("2021 A Movie (1968) Director's Cut .mkv", "Director's Cut")]
        [TestCase("2021 A Movie 1968 (Extended Directors Cut FanEdit)", "Extended Directors Cut FanEdit")]
        [TestCase("A Fake Movie 2035 2012 Directors.mkv", "Directors")]
        [TestCase("Movie 2049 Director's Cut.mkv", "Director's Cut")]
        [TestCase("Movie Title 2012 50th Anniversary Edition.mkv", "50th Anniversary Edition")]
        [TestCase("Movie 2012 2in1.mkv", "2in1")]
        [TestCase("Movie 2012 IMAX.mkv", "IMAX")]
        [TestCase("Movie 2012 Restored.mkv", "Restored")]
        [TestCase("Movie Title.Special.Edition.Fan Edit.2012..BRRip.x264.AAC-m2g", "Special Edition Fan Edit")]
        [TestCase("Movie Title (Despecialized) 1999.mkv", "Despecialized")]
        [TestCase("Movie Title.(Special.Edition.Remastered).2012.[Bluray-1080p].mkv", "Special Edition Remastered")]
        [TestCase("Movie Title Extended 2012", "Extended")]
        [TestCase("Movie Title Extended Directors Cut Fan Edit 2012", "Extended Directors Cut Fan Edit")]
        [TestCase("Movie Title Director's Cut 2012", "Director's Cut")]
        [TestCase("Movie Title Directors Cut 2012", "Directors Cut")]
        [TestCase("Movie Title.(Extended.Theatrical.Version.IMAX).2012.BluRay.1080p.asdf", "Extended Theatrical Version IMAX")]
        [TestCase("Movie Director's Cut (1968).mkv", "Director's Cut")]
        [TestCase("2021 A Movie (Extended Directors Cut FanEdit) 1968 Bluray 1080p", "Extended Directors Cut FanEdit")]
        [TestCase("A Fake Movie 2035 Directors 2012.mkv", "Directors")]
        [TestCase("Movie Director's Cut 2049.mkv", "Director's Cut")]
        [TestCase("Movie Title 50th Anniversary Edition 2012.mkv", "50th Anniversary Edition")]
        [TestCase("Movie 2in1 2012.mkv", "2in1")]
        [TestCase("Movie IMAX 2012.mkv", "IMAX")]
        [TestCase("Fake Movie Final Cut 2016", "Final Cut")]
        [TestCase("Fake Movie 2016 Final Cut ", "Final Cut")]
        [TestCase("My Movie GERMAN Extended Cut 2016", "Extended Cut")]
        [TestCase("My.Movie.GERMAN.Extended.Cut.2016", "Extended Cut")]
        [TestCase("My.Movie.GERMAN.Extended.Cut", "Extended Cut")]
        [TestCase("My.Movie.Assembly.Cut.1992.REPACK.1080p.BluRay.DD5.1.x264-Group", "Assembly Cut")]
        [TestCase("Movie.1987.Ultimate.Hunter.Edition.DTS-HD.DTS.MULTISUBS.1080p.BluRay.x264.HQ-TUSAHD", "Ultimate Hunter Edition")]
        [TestCase("Movie.1950.Diamond.Edition.1080p.BluRay.x264-nikt0", "Diamond Edition")]
        [TestCase("Movie.Title.1990.Ultimate.Rekall.Edition.NORDiC.REMUX.1080p.BluRay.AVC.DTS-HD.MA5.1-TWA", "Ultimate Rekall Edition")]
        [TestCase("Movie.Title.1971.Signature.Edition.1080p.BluRay.FLAC.2.0.x264-TDD", "Signature Edition")]
        [TestCase("Movie.1979.The.Imperial.Edition.BluRay.720p.DTS.x264-CtrlHD", "Imperial Edition")]
        [TestCase("Movie.1997.Open.Matte.1080p.BluRay.x264.DTS-FGT", "Open Matte")]
        [TestCase("Waterworld 1995 Ulysses Cut 2160p BluRay", "Ulysses Cut")]  // krzw(multi-edition)
        [TestCase("Waterworld.1995.Ulysses.Cut.2160p.UHD.BluRay.x265-GRP", "Ulysses Cut")]  // krzw(multi-edition)
        [TestCase("Waterworld (1995) (Ulysses Cut) [Bluray-2160p].mkv", "Ulysses Cut")]  // krzw(multi-edition)
        [TestCase("Movie.Title.1960.Criterion.1080p.BluRay.x264-GRP", "Criterion")]  // krzw(multi-edition)
        [TestCase("Movie.Title.1960.Criterion.Collection.1080p.BluRay.x264-GRP", "Criterion Collection")]  // krzw(multi-edition)
        [TestCase("Movie Title (1960) (Criterion Collection) 1080p BluRay", "Criterion Collection")]  // krzw(multi-edition)
        [TestCase("Movie.Title.1960.Criterion.Collection.Remastered.1080p.BluRay.x264-GRP", "Criterion Collection Remastered")]  // krzw(multi-edition)
        [TestCase("Star.Trek.II.1982.Directors.Edition.2160p.UHD.BluRay.x265-GRP", "Directors Edition")]  // krzw(multi-edition)
        [TestCase("Star Trek II (1982) Director's Edition 2160p", "Director's Edition")]  // krzw(multi-edition)
        [TestCase("Movie.Title.2007.Final.Cut.1080p.BluRay.x264-GRP", "Final Cut")]  // krzw(multi-edition)
        public void should_parse_edition(string postTitle, string edition)
        {
            var parsed = Parser.Parser.ParseMovieTitle(postTitle);
            parsed.Edition.Should().Be(edition);
        }

        [TestCase("Movie.Holiday.Special.1978.DVD.REMUX.DD.2.0-ViETNAM")]
        [TestCase("Directors.Cut.German.2006.COMPLETE.PAL.DVDR-LoD")]
        [TestCase("Movie Impossible: Rogue Movie 2012 Bluray")]
        [TestCase("Loving.Movie.2018.TS.FRENCH.MD.x264-DROGUERiE")]
        [TestCase("Uncut.Movie.2019.720p.BluRay.x264-YOL0W")]
        [TestCase("The.Christmas.Edition.1941.720p.HDTV.x264-CRiMSON")]
        [TestCase("Ulysses.1954.1080p.BluRay.x264-GRP")]  // krzw(multi-edition)
        [TestCase("Ulysses (1954) Bluray-1080p.mkv")]  // krzw(multi-edition)
        public void should_not_parse_edition(string postTitle)
        {
            var parsed = Parser.Parser.ParseMovieTitle(postTitle);
            parsed.Edition.Should().Be("");
        }

        // krzw(multi-edition) begin
        [TestCase("Waterworld 1995 Ulysses Cut 2160p BluRay", "Waterworld", 1995, "Ulysses Cut")]
        [TestCase("Waterworld.Ulysses.Cut.1995.2160p.BluRay.x265-GRP", "Waterworld", 1995, "Ulysses Cut")]
        [TestCase("Movie.Title.Criterion.Collection.1960.1080p.BluRay.x264-GRP", "Movie Title", 1960, "Criterion Collection")]
        [TestCase("Movie.Title.Criterion.1960.1080p.BluRay.x264-GRP", "Movie Title", 1960, "Criterion")]
        [TestCase("Ulysses.1954.1080p.BluRay.x264-GRP", "Ulysses", 1954, "")]
        public void should_keep_title_with_new_edition_words(string postTitle, string title, int year, string edition)
        {
            var parsed = Parser.Parser.ParseMovieTitle(postTitle);

            parsed.PrimaryMovieTitle.Should().Be(title);
            parsed.Year.Should().Be(year);
            parsed.Edition.Should().Be(edition);
        }

        [TestCase("Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv", "Waterworld", 1995, "Ulysses Cut")]
        [TestCase("Waterworld (1995) {edition-Ulysses Cut}", "Waterworld", 1995, "Ulysses Cut")]
        [TestCase("The Lord of the Rings The Fellowship of the Ring (2001) {edition-Extended} [Bluray-1080p][DTS 5.1][x264]-GRP.mkv", "The Lord of the Rings The Fellowship of the Ring", 2001, "Extended")]
        [TestCase("Movie.Title.2010.{edition-Director's Cut}.1080p.BluRay.x264-GRP", "Movie Title", 2010, "Director's Cut")]
        [TestCase("Movie.Title.2010.{edition-Extended}", "Movie Title", 2010, "Extended")]
        [TestCase("Movie Title {edition-Extended} (2010).mkv", "Movie Title", 2010, "Extended")]
        [TestCase("{edition-Extended} Movie Title (2010).mkv", "Movie Title", 2010, "Extended")]
        [TestCase("Movie Title (2010) {Edition-IMAX Enhanced} [Bluray-2160p].mkv", "Movie Title", 2010, "IMAX Enhanced")]
        [TestCase("Movie Title (2010) {edition- Final Cut }.mkv", "Movie Title", 2010, "Final Cut")]
        [TestCase("Movie Title (2010) {edition-Version Longue} Bluray-1080p.mkv", "Movie Title", 2010, "Version Longue")]
        public void should_parse_plex_edition_tag(string postTitle, string title, int year, string edition)
        {
            var parsed = Parser.Parser.ParseMovieTitle(postTitle);

            parsed.PrimaryMovieTitle.Should().Be(title);
            parsed.Year.Should().Be(year);
            parsed.Edition.Should().Be(edition);
        }

        [Test]
        public void should_parse_plex_edition_tag_from_folder_name()
        {
            var parsed = Parser.Parser.ParseMovieTitle("Waterworld (1995) {edition-Ulysses Cut}", true);

            parsed.PrimaryMovieTitle.Should().Be("Waterworld");
            parsed.Year.Should().Be(1995);
            parsed.Edition.Should().Be("Ulysses Cut");
            parsed.ReleaseGroup.Should().BeNullOrEmpty();
        }

        [TestCase("Star Trek II The Wrath of Khan (1982) Director's Cut {edition-Theatrical} Bluray-1080p.mkv", "Theatrical")]
        [TestCase("Movie.Title.2010.Extended.{edition-Ulysses Cut}.1080p.BluRay-GRP", "Ulysses Cut")]
        [TestCase("Movie Title Directors Cut 2010 {edition-Criterion Collection}.mkv", "Criterion Collection")]
        [TestCase("Movie Title (2010) {edition-Theatrical} Extended Remastered.mkv", "Theatrical")]
        public void should_prefer_plex_edition_tag_over_edition_words(string postTitle, string edition)
        {
            Parser.Parser.ParseMovieTitle(postTitle).Edition.Should().Be(edition);
        }

        [Test]
        public void should_not_leak_plex_edition_tag_into_release_group_or_titles()
        {
            var parsed = Parser.Parser.ParseMovieTitle("Movie.Title.2010.{edition-Extended}.1080p.BluRay.x264-GRP");

            parsed.PrimaryMovieTitle.Should().Be("Movie Title");
            parsed.ReleaseGroup.Should().Be("GRP");
            parsed.ReleaseTitle.Should().Be("Movie.Title.2010.1080p.BluRay.x264-GRP");
            parsed.SimpleReleaseTitle.Should().NotContain("{edition-");
            parsed.OriginalTitle.Should().Contain("{edition-Extended}");
        }

        [TestCase("Movie Title (2010) {edition-}.mkv")]
        [TestCase("Movie Title (2010) {edition-   }.mkv")]
        public void should_ignore_empty_plex_edition_tag(string postTitle)
        {
            var parsed = Parser.Parser.ParseMovieTitle(postTitle);

            parsed.PrimaryMovieTitle.Should().Be("Movie Title");
            parsed.Year.Should().Be(2010);
            parsed.Edition.Should().Be("");
        }

        [TestCase("Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv", "Ulysses Cut")]
        [TestCase("Waterworld (1995) {EDITION-Ulysses Cut}", "Ulysses Cut")]
        [TestCase("Waterworld (1995) Bluray-2160p.mkv", "")]
        [TestCase("Waterworld (1995) {imdb-tt0114898}", "")]
        [TestCase("", "")]
        [TestCase(null, "")]
        public void should_parse_edition_tag_alone(string title, string edition)
        {
            Parser.Parser.ParseEditionTag(title).Should().Be(edition);
        }

        // krzw(multi-edition) end
    }
}
