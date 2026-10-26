using System.Collections.Generic;
using System.Linq;  // krzw(multi-edition)
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionLookupFixture : CoreTest<MovieService>
    {
        private MovieMetadata _metadata;
        private Movie _default;
        private Movie _extended;

        [SetUp]
        public void Setup()
        {
            _metadata = new MovieMetadata
            {
                Id = 7,
                TmdbId = 603,
                Title = "The Matrix",
                CleanTitle = "thematrix",
                Year = 1999
            };

            _default = new Movie { Id = 1, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "Extended" };
        }

        [Test]
        public void movie_exists_should_be_true_when_only_an_edition_row_shares_the_tmdb_id()
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindAllByTmdbId(603))
                  .Returns(new List<Movie> { _extended });

            Subject.MovieExists(new Movie { MovieMetadata = new MovieMetadata { TmdbId = 603 } }).Should().BeTrue();

            Mocker.GetMock<IMovieRepository>().Verify(s => s.FindByImdbId(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void movie_exists_should_fall_through_when_no_row_shares_the_tmdb_id()
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindAllByTmdbId(603))
                  .Returns(new List<Movie>());

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindByImdbId(It.IsAny<string>()))
                  .Returns((Movie)null);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.FindByTitles(It.IsAny<List<string>>()))
                  .Returns(new List<Movie>());

            Subject.MovieExists(new Movie { MovieMetadata = new MovieMetadata { TmdbId = 603, ImdbId = "tt0133093", Title = "The Matrix", Year = 1999 } }).Should().BeFalse();
        }

        [Test]
        public void find_by_title_should_collapse_rows_of_one_title_onto_the_default_row()
        {
            var candidates = new List<Movie> { _extended, _default };

            var result = Subject.FindByTitle(new List<string> { "thematrix" }, 1999, new List<string>(), candidates);

            result.Should().BeSameAs(_default);
        }

        [Test]
        public void find_by_title_should_return_the_edition_row_when_it_is_the_only_row()
        {
            var result = Subject.FindByTitle(new List<string> { "thematrix" }, 1999, new List<string>(), new List<Movie> { _extended });

            result.Should().BeSameAs(_extended);
        }

        [Test]
        public void find_by_title_should_still_throw_for_two_different_titles()
        {
            var otherMetadata = new MovieMetadata { Id = 8, TmdbId = 604, Title = "The Matrix", CleanTitle = "thematrix", Year = 1999 };
            var other = new Movie { Id = 3, MovieMetadataId = 8, MovieMetadata = otherMetadata, Edition = "" };

            Assert.Throws<MultipleMoviesFoundException>(() => Subject.FindByTitle(new List<string> { "thematrix" }, 1999, new List<string>(), new List<Movie> { _default, other }));
        }

        [Test]
        public void find_by_title_should_still_throw_for_unsaved_rows_without_metadata_id()
        {
            var a = new Movie { Id = 0, MovieMetadataId = 0, MovieMetadata = _metadata };
            var b = new Movie { Id = 0, MovieMetadataId = 0, MovieMetadata = _metadata };

            Assert.Throws<MultipleMoviesFoundException>(() => Subject.FindByTitle(new List<string> { "thematrix" }, 1999, new List<string>(), new List<Movie> { a, b }));
        }

        [Test]
        public void lookups_should_delegate_to_the_repository()
        {
            Mocker.GetMock<IMovieRepository>().Setup(s => s.GetEditionSiblings(1)).Returns(new List<Movie> { _extended });
            Mocker.GetMock<IMovieRepository>().Setup(s => s.GetDefaultEdition(7)).Returns(_default);
            Mocker.GetMock<IMovieRepository>().Setup(s => s.EditionExists(603, "Extended")).Returns(true);
            Mocker.GetMock<IMovieRepository>().Setup(s => s.FindAllByTmdbId(603)).Returns(new List<Movie> { _default, _extended });
            Mocker.GetMock<IMovieRepository>().Setup(s => s.FindByTmdbId(It.Is<List<int>>(l => l.SequenceEqual(new[] { 603, 604 })))).Returns(new List<Movie> { _default, _extended });

            Mocker.GetMock<IMovieRepository>().Setup(s => s.GetEditionSiblings(_default)).Returns(new List<Movie> { _extended });

            Subject.GetEditionSiblings(1).Should().BeEquivalentTo(new List<Movie> { _extended });
            Subject.GetEditionSiblings(_default).Should().BeEquivalentTo(new List<Movie> { _extended });
            Subject.GetDefaultEdition(7).Should().BeSameAs(_default);
            Subject.EditionExists(603, "Extended").Should().BeTrue();
            Subject.FindAllByTmdbId(603).Should().HaveCount(2);
            Subject.FindAllByTmdbIds(new List<int> { 603, 604 }).Should().HaveCount(2);
        }

        [Test]
        public void find_all_by_tmdb_ids_should_not_query_for_an_empty_id_list()
        {
            Subject.FindAllByTmdbIds(new List<int>()).Should().BeEmpty();
            Subject.FindAllByTmdbIds(null).Should().BeEmpty();

            Mocker.GetMock<IMovieRepository>().Verify(v => v.FindByTmdbId(It.IsAny<List<int>>()), Times.Never());
        }
    }
}
