using System.Collections.Generic;
using System.Linq;
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
    public class MovieExistsValidatorFixture : CoreTest<MovieExistsValidator>
    {
        private const int TmdbId = 603;

        private Movie _default;
        private Movie _extended;
        private TestValidator<Movie> _validator;

        [SetUp]
        public void Setup()
        {
            _default = new Movie { Id = 1, MovieMetadataId = 7, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, Edition = "Extended", EditionAliases = new List<string> { "Extended", "Extended Cut" } };

            _validator = new TestValidator<Movie>
            {
                v => v.RuleFor(s => s.TmdbId).SetValidator(Subject)
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTmdbId(TmdbId))
                  .Returns(_default);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(new List<Movie> { _default, _extended });
        }

        private static Movie NewMovie(int tmdbId, string edition)
        {
            return new Movie { Edition = edition, MovieMetadata = new MovieMetadata { TmdbId = tmdbId } };
        }

        [Test]
        public void should_reject_a_plain_movie_that_is_already_added()
        {
            var result = _validator.Validate(NewMovie(TmdbId, ""));

            result.IsValid.Should().BeFalse();
            result.Errors.Single().ErrorMessage.Should().Be("This movie has already been added");
        }

        [Test]
        public void should_accept_a_plain_movie_that_is_not_added_yet()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTmdbId(999))
                  .Returns((Movie)null);

            _validator.Validate(NewMovie(999, "")).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_new_edition_for_an_added_movie()
        {
            _validator.Validate(NewMovie(TmdbId, "Director's Cut")).IsValid.Should().BeTrue();
        }

        [Test]
        public void should_reject_an_edition_that_already_exists_by_name_or_alias()
        {
            _validator.Validate(NewMovie(TmdbId, "EXTENDED")).IsValid.Should().BeFalse();
            _validator.Validate(NewMovie(TmdbId, "extended.cut")).IsValid.Should().BeFalse();

            var result = _validator.Validate(NewMovie(TmdbId, "Extended Cut"));

            result.Errors.Single().ErrorMessage.Should().Be("This movie edition 'Extended Cut' has already been added");
        }

        [Test]
        public void should_read_the_edition_name_from_a_resource_shaped_instance()
        {
            var validator = new TestValidator<ResourceLike>
            {
                v => v.RuleFor(s => s.TmdbId).SetValidator(Subject)
            };

            validator.Validate(new ResourceLike { TmdbId = TmdbId, Edition = new EditionLike { Name = "IMAX" } }).IsValid.Should().BeTrue();
            validator.Validate(new ResourceLike { TmdbId = TmdbId, Edition = new EditionLike { Name = "Extended" } }).IsValid.Should().BeFalse();
            validator.Validate(new ResourceLike { TmdbId = TmdbId, Edition = null }).IsValid.Should().BeFalse();
        }

        public class ResourceLike
        {
            public int Id { get; set; }
            public int TmdbId { get; set; }
            public EditionLike Edition { get; set; }
        }

        public class EditionLike
        {
            public string Name { get; set; }
            public List<string> Aliases { get; set; }
            public bool AcceptUntagged { get; set; }
        }
    }
}
