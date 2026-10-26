using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.ValidationTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class MoviePathValidatorFixture : CoreTest<MoviePathValidator>
    {
        private const int TmdbId = 603;

        private string _sharedPath;
        private string _otherPath;
        private TestValidator<Movie> _validator;

        [SetUp]
        public void Setup()
        {
            _sharedPath = @"C:\Movies\The Matrix (1999)".AsOsAgnostic();
            _otherPath = @"C:\Movies\Other (2001)".AsOsAgnostic();

            _validator = new TestValidator<Movie>
            {
                v => v.RuleFor(s => s.Path).SetValidator(Subject)
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { 1, _sharedPath }, { 2, _otherPath } });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(new List<Movie> { new Movie { Id = 1, MovieMetadataId = 7, Edition = "" } });
        }

        private static Movie NewMovie(int id, int tmdbId, string path)
        {
            return new Movie { Id = id, Path = path, MovieMetadata = new MovieMetadata { TmdbId = tmdbId } };
        }

        [Test]
        public void should_allow_a_new_edition_row_on_the_default_rows_path()
        {
            _validator.Validate(NewMovie(0, TmdbId, _sharedPath)).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_allow_an_existing_row_to_keep_a_path_shared_with_its_editions()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(new List<Movie> { new Movie { Id = 1, Edition = "" }, new Movie { Id = 3, Edition = "Extended" } });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { 1, _sharedPath }, { 2, _otherPath }, { 3, _sharedPath } });

            _validator.Validate(NewMovie(1, TmdbId, _sharedPath)).IsValid.Should().BeTrue();
            _validator.Validate(NewMovie(3, TmdbId, _sharedPath)).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_reject_the_path_of_a_foreign_movie()
        {
            _validator.Validate(NewMovie(0, 999, _sharedPath)).IsValid.Should().BeFalse();
            _validator.Validate(NewMovie(1, TmdbId, _otherPath)).IsValid.Should().BeFalse();
        }

        [Test]
        public void should_keep_upstream_behaviour_for_instances_without_a_tmdb_id()
        {
            var validator = new TestValidator<PathOnly>
            {
                v => v.RuleFor(s => s.Path).SetValidator(Subject)
            };

            validator.Validate(new PathOnly { Id = 0, Path = _sharedPath }).IsValid.Should().BeFalse();
            validator.Validate(new PathOnly { Id = 1, Path = _sharedPath }).IsValid.Should().BeTrue();
        }

        public class PathOnly
        {
            public int Id { get; set; }
            public string Path { get; set; }
        }
    }
}
