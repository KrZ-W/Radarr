using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class AddMovieEditionServiceFixture : CoreTest<AddMovieEditionService>
    {
        private const int TmdbId = 603;

        private Movie _default;
        private Movie _extended;
        private Movie _inserted;

        [SetUp]
        public void Setup()
        {
            var metadata = new MovieMetadata { Id = 7, TmdbId = TmdbId, Title = "The Matrix", Year = 1999 };

            _default = new Movie
            {
                Id = 1,
                MovieMetadataId = 7,
                MovieMetadata = metadata,
                Edition = "",
                Path = @"/movies/The Matrix (1999)".AsOsAgnostic(),
                RootFolderPath = @"/movies".AsOsAgnostic(),
                QualityProfileId = 3,
                Monitored = true
            };

            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = 7,
                MovieMetadata = metadata,
                Edition = "Extended",
                EditionAliases = new List<string> { "extended", "extended cut" },
                Path = _default.Path,
                RootFolderPath = _default.RootFolderPath,
                QualityProfileId = 3
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTmdbId(TmdbId))
                  .Returns(_default);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(new List<Movie> { _default, _extended });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AddMovie(It.IsAny<Movie>()))
                  .Returns<Movie>(m =>
                  {
                      _inserted = m;
                      m.Id = 9;
                      return m;
                  });
        }

        private static Movie Requested(string name, params string[] aliases)
        {
            return new Movie
            {
                Edition = name,
                EditionAliases = aliases.ToList(),
                EditionAcceptUntagged = true,
                QualityProfileId = 5,
                Monitored = false,
                MinimumAvailability = MovieStatusType.InCinemas,
                Tags = new HashSet<int> { 11 },
                AddOptions = new AddMovieOptions { SearchForMovie = true },
                Path = @"/somewhere/else".AsOsAgnostic(),
                RootFolderPath = @"/somewhere".AsOsAgnostic()
            };
        }

        [Test]
        public void should_throw_when_the_movie_has_no_default_row()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTmdbId(TmdbId))
                  .Returns((Movie)null);

            var ex = Assert.Throws<ValidationException>(() => Subject.AddEdition(Requested("Director's Cut"), TmdbId));

            ex.Errors.Single().PropertyName.Should().Be("TmdbId");

            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void should_throw_when_the_name_is_blank(string name)
        {
            var ex = Assert.Throws<ValidationException>(() => Subject.AddEdition(Requested(name), TmdbId));

            ex.Errors.Single().PropertyName.Should().Be("Edition");
            ex.Errors.Single().ErrorMessage.Should().Be(EditionValidation.NameRequiredMessage);
        }

        [Test]
        public void should_throw_when_the_name_is_longer_than_32_characters()
        {
            var ex = Assert.Throws<ValidationException>(() => Subject.AddEdition(Requested(new string('x', 33)), TmdbId));

            ex.Errors.Single().PropertyName.Should().Be("Edition");
            ex.Errors.Single().ErrorMessage.Should().Contain("32");
        }

        [TestCase("Extended")]
        [TestCase("EXTENDED")]
        [TestCase("extended.cut")]
        public void should_throw_when_an_edition_row_already_carries_the_name_or_alias(string name)
        {
            var ex = Assert.Throws<ValidationException>(() => Subject.AddEdition(Requested(name), TmdbId));

            ex.Errors.Single().PropertyName.Should().Be("Edition");
            ex.Errors.Single().ErrorMessage.Should().Be(string.Format(EditionValidation.AlreadyAddedMessage, name));

            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_copy_metadata_path_and_root_folder_from_the_default_row()
        {
            var result = Subject.AddEdition(Requested("Director's Cut"), TmdbId);

            result.Should().BeSameAs(_inserted);
            result.Id.Should().Be(9);
            result.MovieMetadataId.Should().Be(_default.MovieMetadataId);
            result.MovieMetadata.Value.Should().BeSameAs(_default.MovieMetadata.Value);
            result.TmdbId.Should().Be(TmdbId);
            result.Path.Should().Be(_default.Path);
            result.RootFolderPath.Should().Be(_default.RootFolderPath);
            result.IsEdition.Should().BeTrue();
            result.Edition.Should().Be("Director's Cut");
        }

        [Test]
        public void should_keep_the_callers_profile_monitoring_tags_availability_and_add_options()
        {
            var result = Subject.AddEdition(Requested("Director's Cut"), TmdbId);

            result.QualityProfileId.Should().Be(5);
            result.Monitored.Should().BeFalse();
            result.MinimumAvailability.Should().Be(MovieStatusType.InCinemas);
            result.Tags.Should().BeEquivalentTo(new HashSet<int> { 11 });
            result.AddOptions.SearchForMovie.Should().BeTrue();
            result.EditionAcceptUntagged.Should().BeTrue();
        }

        [Test]
        public void should_fall_back_to_the_default_rows_profile_when_none_is_given()
        {
            var requested = Requested("Director's Cut");
            requested.QualityProfileId = 0;

            Subject.AddEdition(requested, TmdbId).QualityProfileId.Should().Be(_default.QualityProfileId);
        }

        [Test]
        public void should_normalize_aliases_and_always_include_the_name()
        {
            var result = Subject.AddEdition(Requested("Director's Cut", "Directors.Cut", " DC ", "", "director's edition"), TmdbId);

            result.EditionAliases.Should().Equal("directors cut", "dc", "directors edition");
        }

        [Test]
        public void should_trim_the_name()
        {
            Subject.AddEdition(Requested("  Final Cut "), TmdbId).Edition.Should().Be("Final Cut");
        }

        [Test]
        public void should_update_tags_before_inserting()
        {
            Subject.AddEdition(Requested("Director's Cut"), TmdbId);

            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateTags(_inserted), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.AddMovie(_inserted), Times.Once());
        }
    }
}
