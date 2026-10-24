using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class DeleteMovieEditionFixture : CoreTest<MovieService>
    {
        private MovieMetadata _metadata;
        private Movie _default;
        private Movie _extended;

        [SetUp]
        public void Setup()
        {
            _metadata = new MovieMetadata { Id = 7, TmdbId = 603, Title = "The Matrix", Year = 1999 };

            _default = new Movie { Id = 1, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "Extended" };

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(1))
                  .Returns(_default);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(2))
                  .Returns(_extended);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(It.IsAny<IEnumerable<int>>()))
                  .Returns<IEnumerable<int>>(ids => ids.Select(id => id == 1 ? _default : _extended).ToList());

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(1))
                  .Returns(new List<Movie> { _extended });

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(2))
                  .Returns(new List<Movie> { _default });
        }

        [Test]
        public void should_refuse_to_delete_a_default_row_that_still_has_edition_rows()
        {
            var exception = Assert.Throws<NzbDroneClientException>(() => Subject.DeleteMovie(1, false));

            exception.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            exception.Message.Should().Contain("Extended");

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Delete(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.IsAny<MoviesDeletedEvent>()), Times.Never());
        }

        [Test]
        public void should_delete_an_edition_row_without_consulting_siblings()
        {
            Subject.DeleteMovie(2, true, true);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Delete(2), Times.Once());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEventAggregator>().Verify(v => v.PublishEvent(It.Is<MoviesDeletedEvent>(e => e.Movies.Single() == _extended && e.DeleteFiles && e.AddImportListExclusion)), Times.Once());
        }

        [Test]
        public void should_delete_a_default_row_once_its_edition_rows_are_gone()
        {
            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(1))
                  .Returns(new List<Movie>());

            Subject.DeleteMovie(1, false);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Delete(1), Times.Once());
        }

        [Test]
        public void bulk_delete_should_accept_a_default_row_whose_edition_rows_are_in_the_same_batch()
        {
            Subject.DeleteMovies(new List<int> { 1, 2 }, false);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.DeleteMany(It.Is<List<int>>(ids => ids.Count == 2)), Times.Once());
        }

        [Test]
        public void bulk_delete_should_refuse_a_default_row_whose_edition_rows_stay_behind()
        {
            Assert.Throws<NzbDroneClientException>(() => Subject.DeleteMovies(new List<int> { 1 }, false));

            Mocker.GetMock<IMovieRepository>().Verify(v => v.DeleteMany(It.IsAny<List<int>>()), Times.Never());
        }
    }
}
