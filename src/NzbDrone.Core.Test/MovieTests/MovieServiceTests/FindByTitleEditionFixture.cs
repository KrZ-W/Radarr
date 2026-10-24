using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class FindByTitleEditionFixture : CoreTest<MovieService>
    {
        private MovieMetadata _metadata;
        private Movie _default;
        private Movie _extended;
        private List<Movie> _candidates;
        private List<string> _titles;
        private List<string> _otherTitles;

        [SetUp]
        public void Setup()
        {
            _metadata = new MovieMetadata
            {
                Id = 7,
                TmdbId = 603,
                ImdbId = "tt0133093",
                Title = "The Matrix",
                CleanTitle = "thematrix",
                Year = 1999
            };

            _default = new Movie { Id = 1, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "", Monitored = true };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "Extended", EditionAliases = new List<string> { "Extended Cut" }, Monitored = false };
            _candidates = new List<Movie> { _default, _extended };
            _titles = new List<string> { "The Matrix" };
            _otherTitles = new List<string>();

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindByTitles(It.IsAny<List<string>>()))
                  .Returns(_candidates);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindByImdbId("tt0133093"))
                  .Returns(_default);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(_default.Id))
                  .Returns(new List<Movie> { _extended });
        }

        [Test]
        public void legacy_overload_should_return_the_default_row()
        {
            Subject.FindByTitle(_titles, 1999, _otherTitles, _candidates).Should().BeSameAs(_default);
            Subject.FindByTitle(_titles, null, _otherTitles, _candidates).Should().BeSameAs(_default);
        }

        [TestCase("Extended")]
        [TestCase("extended")]
        [TestCase("Extended Cut")]
        [TestCase("Extended Remastered")]
        public void edition_overload_should_return_the_edition_row_the_edition_names(string edition)
        {
            Subject.FindByTitle(_titles, 1999, _otherTitles, _candidates, edition).Should().BeSameAs(_extended);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Director's Cut")]
        public void edition_overload_should_return_the_default_row_for_blank_or_unknown_editions(string edition)
        {
            Subject.FindByTitle(_titles, 1999, _otherTitles, _candidates, edition).Should().BeSameAs(_default);
        }

        [Test]
        public void edition_overload_should_return_an_unmonitored_edition_row()
        {
            _extended.Monitored = false;

            Subject.FindByTitle(_titles, 1999, _otherTitles, _candidates, "Extended").Should().BeSameAs(_extended);
        }

        [Test]
        public void edition_overload_should_respect_the_year_filter()
        {
            Subject.FindByTitle(_titles, 2003, _otherTitles, _candidates, "Extended").Should().BeNull();
        }

        [Test]
        public void edition_overload_should_still_throw_for_two_different_movies()
        {
            var otherMovie = new Movie
            {
                Id = 3,
                MovieMetadataId = 8,
                MovieMetadata = new MovieMetadata { Id = 8, TmdbId = 999, Title = "The Matrix", CleanTitle = "thematrix", Year = 2021 },
                Edition = ""
            };

            var candidates = new List<Movie> { _default, otherMovie };

            Assert.Throws<MultipleMoviesFoundException>(() => Subject.FindByTitle(_titles, null, _otherTitles, candidates, "Extended"));
        }

        [Test]
        public void string_overloads_should_route_by_edition_and_default_without_one()
        {
            Subject.FindByTitle("The Matrix", 1999, "Extended").Should().BeSameAs(_extended);
            Subject.FindByTitle("The Matrix", 1999, "").Should().BeSameAs(_default);
            Subject.FindByTitle("The Matrix", 1999).Should().BeSameAs(_default);
            Subject.FindByTitle("The Matrix").Should().BeSameAs(_default);
        }

        [Test]
        public void find_by_imdb_id_with_edition_should_return_the_edition_row()
        {
            Subject.FindByImdbId("tt0133093", "Extended").Should().BeSameAs(_extended);
        }

        [TestCase(null)]
        [TestCase("")]
        public void find_by_imdb_id_with_blank_edition_should_return_the_default_row_without_loading_siblings(string edition)
        {
            Subject.FindByImdbId("tt0133093", edition).Should().BeSameAs(_default);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void find_by_imdb_id_with_unknown_edition_should_return_the_default_row()
        {
            Subject.FindByImdbId("tt0133093", "Unrated").Should().BeSameAs(_default);
        }

        [Test]
        public void find_by_imdb_id_with_edition_should_return_null_for_an_unknown_id()
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindByImdbId("tt0000001"))
                  .Returns((Movie)null);

            Subject.FindByImdbId("tt0000001", "Extended").Should().BeNull();
        }
    }
}
