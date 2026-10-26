using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests.FileNameBuilderTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionNameFixture : CoreTest<FileNameBuilder>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            // Built explicitly: NBuilder fills Edition with "Edition1" and would turn the default row into an edition row.
            _movie = new Movie
            {
                Id = 1,
                MovieMetadataId = 7,
                Title = "Movie Title",
                Year = 2010,
                Edition = ""
            };

            _movieFile = new MovieFile { Quality = new QualityModel(Quality.Bluray2160p), ReleaseGroup = "RadarrTest" };

            _namingConfig = NamingConfig.Default;
            _namingConfig.RenameMovies = true;

            Mocker.GetMock<INamingConfigService>()
                  .Setup(c => c.GetConfig()).Returns(_namingConfig);

            Mocker.GetMock<IQualityDefinitionService>()
                .Setup(v => v.Get(Moq.It.IsAny<Quality>()))
                .Returns<Quality>(v => Quality.DefaultQualityDefinitions.First(c => c.Quality == v));

            Mocker.GetMock<ICustomFormatService>()
                .Setup(v => v.All())
                .Returns(new List<CustomFormat>());
        }

        private void GivenEditionRow(string edition)
        {
            _movie.Id = 2;
            _movie.Edition = edition;
            _movie.EditionAliases = new List<string> { edition.ToLowerInvariant() };
        }

        [Test]
        public void should_add_edition_name_for_edition_row()
        {
            GivenEditionRow("Extended");
            _namingConfig.StandardMovieFormat = "{Movie Title} {edition-{Edition Name}}";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title {edition-Extended}");
        }

        [TestCase("{Movie Title} {Edition Name}")]
        [TestCase("{Movie Title} {{Edition Name}}")]
        [TestCase("{Movie Title} {[Edition Name]}")]
        [TestCase("{Movie Title} {edition-{Edition Name}}")]
        [TestCase("{Movie Title} {{edition-{Edition Name}}}")]
        [TestCase("{Movie Title} {edition-{Edition.Name}}")]
        public void should_collapse_edition_name_for_default_row(string movieFormat)
        {
            _namingConfig.StandardMovieFormat = movieFormat;

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title");
        }

        [Test]
        public void should_keep_literal_brackets_outside_the_token_like_edition_tags()
        {
            _namingConfig.StandardMovieFormat = "{Movie Title} [{Edition Name}]";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title []");
        }

        [Test]
        public void should_leave_no_double_space_when_edition_name_collapses_mid_format()
        {
            _namingConfig.StandardMovieFormat = "{Movie Title} {edition-{Edition Name}} [{Quality Title}]";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title [Bluray-2160p]");
        }

        [Test]
        public void should_handle_edition_curly_brackets()
        {
            GivenEditionRow("Extended");
            _namingConfig.StandardMovieFormat = "{Movie Title} {{Edition Name}}";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title {Extended}");
        }

        [Test]
        public void should_handle_edition_tag_curly_brackets()
        {
            GivenEditionRow("Extended");
            _namingConfig.StandardMovieFormat = "{Movie Title} {{edition-{Edition Name}}}";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title {{edition-Extended}}");
        }

        [TestCase("Director's Cut", "{Movie Title} [{Edition Name}]", "Movie Title [Director's Cut]")]
        [TestCase("IMAX", "{Movie Title} [{Edition Name}]", "Movie Title [IMAX]")]
        [TestCase("Ulysses Cut", "{Movie Title} [{Edition Name}]", "Movie Title [Ulysses Cut]")]
        [TestCase("10th anniversary edition", "{Movie Title} [{Edition Name}]", "Movie Title [10th anniversary edition]")]
        [TestCase("Extended", "{Movie Title} [{edition name}]", "Movie Title [extended]")]
        [TestCase("Extended", "{Movie Title} [{EDITION NAME}]", "Movie Title [EXTENDED]")]
        [TestCase("Ulysses Cut", "{Movie.Title} [{Edition.Name}]", "Movie.Title [Ulysses.Cut]")]
        public void should_render_canonical_name_verbatim_and_follow_token_casing(string edition, string movieFormat, string expected)
        {
            GivenEditionRow(edition);
            _namingConfig.StandardMovieFormat = movieFormat;

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be(expected);
        }

        [Test]
        public void should_use_row_edition_not_file_edition()
        {
            GivenEditionRow("Extended");
            _movieFile.Edition = "Uncut";
            _namingConfig.StandardMovieFormat = "{Movie Title} {edition-{Edition Name}} [{Edition Tags}]";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title {edition-Extended} [Uncut]");
        }

        [Test]
        public void should_collapse_edition_name_on_default_row_even_when_file_has_parsed_edition()
        {
            _movieFile.Edition = "Uncut";
            _namingConfig.StandardMovieFormat = "{Movie Title} {edition-{Edition Name}} [{Edition Tags}]";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title [Uncut]");
        }

        [Test]
        public void should_truncate_edition_name_with_custom_format()
        {
            GivenEditionRow("Director's Cut");
            _namingConfig.StandardMovieFormat = "{Movie Title} [{Edition Name:10}]";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be("Movie Title [Directo...]");
        }

        [TestCase("Ulysses Cut", "Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p")]
        [TestCase("", "Waterworld (1995) Bluray-2160p")]
        public void should_build_recommended_format_for_waterworld(string edition, string expected)
        {
            _movie.Title = "Waterworld";
            _movie.Year = 1995;
            _movie.Edition = edition;
            _namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full}";

            Subject.BuildFileName(_movie, _movieFile)
                   .Should().Be(expected);
        }

        [Test]
        public void should_produce_a_parseable_name_with_the_recommended_format()
        {
            _movie.Title = "Waterworld";
            _movie.Year = 1995;
            GivenEditionRow("Ulysses Cut");
            _namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full}";

            var fileName = Subject.BuildFileName(_movie, _movieFile);
            var parsed = Parser.Parser.ParseMovieTitle(fileName);

            parsed.Should().NotBeNull();
            parsed.PrimaryMovieTitle.Should().Be("Waterworld");
            parsed.Year.Should().Be(1995);
            parsed.Edition.Should().Be("Ulysses Cut");
        }

        [Test]
        public void should_not_render_edition_name_in_movie_folder()
        {
            GivenEditionRow("Extended");
            _namingConfig.MovieFolderFormat = "{Movie Title} ({Release Year}) {edition-{Edition Name}}";

            Subject.GetMovieFolder(_movie)
                   .Should().Be("Movie Title (2010)");
        }

        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full}", true)]
        [TestCase("{Movie Title} ({Release Year}) [{Edition Name}]", true)]
        [TestCase("{Movie Title} ({Release Year}) {Edition Name:10}", true)]
        [TestCase("{Movie Title} ({Release Year}) {edition name}", true)]
        [TestCase("{Movie Title} ({Release Year}) {Edition.Name}", true)]
        [TestCase("{Movie Title} ({Release Year}) {Edition_Name}", true)]
        [TestCase("{Movie Title} ({Release Year}) {EditionName}", true)]
        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Tags}}", false)]
        [TestCase("{Movie Title} ({Release Year}) {Quality Full}", false)]
        public void edition_name_regex_should_detect_the_token(string format, bool expected)
        {
            FileNameBuilder.EditionNameRegex.IsMatch(format).Should().Be(expected);
        }
    }
}
