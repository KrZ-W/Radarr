using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.MovieServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionPathPropagationFixture : CoreTest<MovieService>
    {
        private string _oldPath;
        private string _newPath;
        private MovieMetadata _metadata;
        private Movie _storedDefault;
        private Movie _storedExtended;

        [SetUp]
        public void Setup()
        {
            _oldPath = @"C:\Movies\The Matrix (1999)".AsOsAgnostic();
            _newPath = @"D:\Movies\The Matrix (1999)".AsOsAgnostic();

            _metadata = new MovieMetadata { Id = 7, TmdbId = 603, Title = "The Matrix", Year = 1999 };

            _storedDefault = NewRow(1, "", _oldPath);
            _storedExtended = NewRow(2, "Extended", _oldPath);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(1))
                  .Returns(_storedDefault);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Get(2))
                  .Returns(_storedExtended);

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(1))
                  .Returns(() => new List<Movie> { _storedExtended });

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.GetEditionSiblings(2))
                  .Returns(() => new List<Movie> { _storedDefault });

            Mocker.GetMock<IMovieRepository>()
                  .Setup(s => s.Update(It.IsAny<Movie>()))
                  .Returns<Movie>(m => m);

            Mocker.GetMock<IAutoTaggingService>()
                  .Setup(s => s.GetTagChanges(It.IsAny<Movie>()))
                  .Returns(new AutoTaggingChanges());
        }

        private Movie NewRow(int id, string edition, string path)
        {
            return new Movie
            {
                Id = id,
                MovieMetadataId = 7,
                MovieMetadata = _metadata,
                Edition = edition,
                Path = path,
                QualityProfileId = 1,
                Monitored = true
            };
        }

        private static Expression<Func<Movie, object>>[] AnyFields()
        {
            return It.IsAny<Expression<Func<Movie, object>>[]>();
        }

        [Test]
        public void updating_the_default_rows_path_should_move_its_edition_rows_along()
        {
            var edited = NewRow(1, "", _newPath);

            Subject.UpdateMovie(edited);

            Mocker.GetMock<IMovieRepository>()
                  .Verify(v => v.SetFields(It.Is<IList<Movie>>(l => l.Count == 1 && l[0].Id == 2 && l[0].Path == _newPath), AnyFields()), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<MoviesBulkEditedEvent>(e => e.Movies.Single().Id == 2)), Times.Once());
        }

        [Test]
        public void updating_the_default_row_without_a_path_change_should_leave_edition_rows_alone()
        {
            var edited = NewRow(1, "", _oldPath);
            edited.Monitored = false;

            Subject.UpdateMovie(edited);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.SetFields(It.IsAny<IList<Movie>>(), AnyFields()), Times.Never());
        }

        [Test]
        public void should_reject_a_path_change_on_an_edition_row()
        {
            var edited = NewRow(2, "Extended", _newPath);

            Assert.Throws<NzbDroneClientException>(() => Subject.UpdateMovie(edited));

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Update(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_accept_other_edits_on_an_edition_row()
        {
            var edited = NewRow(2, "Extended", _oldPath);
            edited.Monitored = false;

            Subject.UpdateMovie(edited);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.Update(edited), Times.Once());
        }

        [Test]
        public void should_not_propagate_when_the_edition_rows_are_already_on_the_new_path()
        {
            _storedExtended.Path = _newPath;

            Subject.UpdateMovie(NewRow(1, "", _newPath));

            Mocker.GetMock<IMovieRepository>().Verify(v => v.SetFields(It.IsAny<IList<Movie>>(), AnyFields()), Times.Never());
        }

        [Test]
        public void bulk_root_folder_change_of_the_default_row_should_drag_edition_rows_outside_the_batch_along()
        {
            var newRoot = @"D:\Movies".AsOsAgnostic();
            var batchDefault = NewRow(1, "", _oldPath);
            batchDefault.RootFolderPath = newRoot;

            Mocker.GetMock<IBuildMoviePaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Movie>(), It.IsAny<bool>()))
                  .Returns<Movie, bool>((m, u) => Path.Combine(m.RootFolderPath, "The Matrix (1999)"));

            Subject.UpdateMovie(new List<Movie> { batchDefault }, false);

            batchDefault.Path.Should().Be(_newPath);

            Mocker.GetMock<IMovieRepository>().Verify(v => v.UpdateMany(It.Is<List<Movie>>(l => l.Count == 1 && l[0].Id == 1)), Times.Once());
            Mocker.GetMock<IMovieRepository>()
                  .Verify(v => v.SetFields(It.Is<IList<Movie>>(l => l.Count == 1 && l[0].Id == 2 && l[0].Path == _newPath), AnyFields()), Times.Once());
        }

        [Test]
        public void bulk_root_folder_change_with_both_rows_in_the_batch_should_give_the_edition_row_the_default_rows_path()
        {
            var newRoot = @"D:\Movies".AsOsAgnostic();
            var batchDefault = NewRow(1, "", _oldPath);
            var batchExtended = NewRow(2, "Extended", _oldPath);
            batchDefault.RootFolderPath = newRoot;
            batchExtended.RootFolderPath = newRoot;

            Mocker.GetMock<IBuildMoviePaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Movie>(), It.IsAny<bool>()))
                  .Returns<Movie, bool>((m, u) => Path.Combine(m.RootFolderPath, "The Matrix (1999)"));

            Subject.UpdateMovie(new List<Movie> { batchExtended, batchDefault }, false);

            batchDefault.Path.Should().Be(_newPath);
            batchExtended.Path.Should().Be(_newPath);

            Mocker.GetMock<IBuildMoviePaths>().Verify(v => v.BuildPath(batchExtended, It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.UpdateMany(It.Is<List<Movie>>(l => l.Count == 2)), Times.Once());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.SetFields(It.IsAny<IList<Movie>>(), AnyFields()), Times.Never());
        }

        [Test]
        public void bulk_root_folder_change_of_an_edition_row_alone_should_be_rejected()
        {
            var batchExtended = NewRow(2, "Extended", _oldPath);
            batchExtended.RootFolderPath = @"D:\Movies".AsOsAgnostic();

            Mocker.GetMock<IBuildMoviePaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Movie>(), It.IsAny<bool>()))
                  .Returns<Movie, bool>((m, u) => Path.Combine(m.RootFolderPath, "The Matrix (1999)"));

            Assert.Throws<NzbDroneClientException>(() => Subject.UpdateMovie(new List<Movie> { batchExtended }, false));

            Mocker.GetMock<IMovieRepository>().Verify(v => v.UpdateMany(It.IsAny<List<Movie>>()), Times.Never());
        }

        [Test]
        public void bulk_edit_of_an_edition_row_alone_that_keeps_its_path_should_pass()
        {
            var batchExtended = NewRow(2, "Extended", _oldPath);
            batchExtended.RootFolderPath = @"C:\Movies".AsOsAgnostic();

            Mocker.GetMock<IBuildMoviePaths>()
                  .Setup(s => s.BuildPath(It.IsAny<Movie>(), It.IsAny<bool>()))
                  .Returns<Movie, bool>((m, u) => Path.Combine(m.RootFolderPath, "The Matrix (1999)"));

            Subject.UpdateMovie(new List<Movie> { batchExtended }, false);

            batchExtended.Path.Should().Be(_oldPath);
            Mocker.GetMock<IMovieRepository>().Verify(v => v.UpdateMany(It.Is<List<Movie>>(l => l.Count == 1)), Times.Once());
        }

        [Test]
        public void bulk_edit_without_a_root_folder_should_not_touch_any_path()
        {
            var batchDefault = NewRow(1, "", _oldPath);
            var batchExtended = NewRow(2, "Extended", _oldPath);
            batchDefault.Monitored = false;
            batchExtended.Monitored = false;

            Subject.UpdateMovie(new List<Movie> { batchDefault, batchExtended }, true);

            Mocker.GetMock<IBuildMoviePaths>().Verify(v => v.BuildPath(It.IsAny<Movie>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMovieRepository>().Verify(v => v.UpdateMany(It.Is<List<Movie>>(l => l.Count == 2)), Times.Once());
        }
    }
}
