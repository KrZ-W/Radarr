using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;  // krzw(multi-edition)
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras.Subtitles
{
    // krzw(atomic-upgrade): the MovieFileDeletedEvent handler runs asynchronously; after a same-name
    // upgrade the replacement's extras may already own the old paths when it gets to run.
    [TestFixture]
    public class SubtitleFileServiceFixture : CoreTest<SubtitleFileService>
    {
        private Movie _movie;
        private MovieFile _oldMovieFile;
        private SubtitleFile _oldSubtitle;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 5,
                Path = @"C:\Test\Movies\Movie".AsOsAgnostic()
            };

            _oldMovieFile = new MovieFile
            {
                Id = 1,
                MovieId = _movie.Id,
                RelativePath = "Movie.mkv"
            };

            _oldSubtitle = new SubtitleFile
            {
                Id = 10,
                MovieId = _movie.Id,
                MovieFileId = _oldMovieFile.Id,
                RelativePath = "Movie.en.srt"
            };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(_movie);

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovieFile(_oldMovieFile.Id))
                  .Returns(new List<SubtitleFile> { _oldSubtitle });

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<SubtitleFile> { _oldSubtitle });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(It.IsAny<string>()))
                  .Returns<string>(p => Path.GetDirectoryName(p));
        }

        private void GivenReplacementOwnsTheSamePath()
        {
            var newSubtitle = new SubtitleFile
            {
                Id = 11,
                MovieId = _movie.Id,
                MovieFileId = 2,
                RelativePath = "Movie.en.srt"
            };

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<SubtitleFile> { _oldSubtitle, newSubtitle });
        }

        [Test]
        public void should_recycle_extra_of_deleted_movie_file()
        {
            Subject.HandleAsync(new MovieFileDeletedEvent(_oldMovieFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(Path.Combine(_movie.Path, "Movie.en.srt"), It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_oldMovieFile.Id), Times.Once());
        }

        [Test]
        public void should_not_recycle_a_path_now_owned_by_another_movie_file()
        {
            GivenReplacementOwnsTheSamePath();

            Subject.HandleAsync(new MovieFileDeletedEvent(_oldMovieFile, DeleteMediaFileReason.Upgrade));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_oldMovieFile.Id), Times.Once());
        }

        [Test]
        public void should_not_touch_disk_for_no_linked_episodes_cleanup()
        {
            Subject.HandleAsync(new MovieFileDeletedEvent(_oldMovieFile, DeleteMediaFileReason.NoLinkedEpisodes));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovieFile(_oldMovieFile.Id), Times.Once());
        }

        // krzw(multi-edition) begin: deleting an edition row with "delete files" while the folder stays with its
        // siblings recycles only that row's movie file and raises no MovieFileDeletedEvent (the row is gone), so the
        // MoviesDeletedEvent handler must recycle the extras linked to that file itself.
        private Movie _extendedRow;
        private Movie _defaultRow;
        private SubtitleFile _extendedSubtitle;
        private string _extendedSubtitlePath;

        private void GivenDeletedEditionRow()
        {
            var moviePath = @"C:\Test\Movies\Movie".AsOsAgnostic();

            _extendedRow = new Movie { Id = 7, MovieMetadataId = 3, Edition = "Extended", Path = moviePath, MovieFileId = 70 };
            _defaultRow = new Movie { Id = 6, MovieMetadataId = 3, Edition = "", Path = moviePath, MovieFileId = 60 };

            _extendedSubtitle = new SubtitleFile
            {
                Id = 12,
                MovieId = _extendedRow.Id,
                MovieFileId = _extendedRow.MovieFileId,
                RelativePath = "Movie {edition-Extended}.en.srt"
            };

            _extendedSubtitlePath = Path.Combine(moviePath, _extendedSubtitle.RelativePath);

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovieFile(_extendedRow.MovieFileId))
                  .Returns(new List<SubtitleFile> { _extendedSubtitle });

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovie(_defaultRow.Id))
                  .Returns(new List<SubtitleFile>());
        }

        private void GivenRemainingSiblings(params Movie[] siblings)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(It.Is<Movie>(m => m != null && m.Id == _extendedRow.Id)))
                  .Returns(new List<Movie>(siblings));
        }

        [Test]
        public void should_recycle_the_extras_of_a_deleted_edition_row_whose_folder_stays_with_its_siblings()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(_defaultRow);

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(_extendedSubtitlePath, "Movie"), Times.Once());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovies(It.Is<List<int>>(ids => ids.Count == 1 && ids[0] == _extendedRow.Id)), Times.Once());
        }

        [Test]
        public void should_leave_the_extras_to_the_folder_recycle_when_no_sibling_remains()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovies(It.IsAny<List<int>>()), Times.Once());
        }

        [Test]
        public void should_not_count_a_sibling_at_another_path_as_sharing_the_folder()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(new Movie { Id = 8, MovieMetadataId = 3, Edition = "", Path = @"C:\Test\Movies\Elsewhere".AsOsAgnostic() });

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_touch_disk_or_siblings_when_delete_files_is_off()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(_defaultRow);

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, false, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.GetEditionSiblings(It.IsAny<Movie>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovies(It.IsAny<List<int>>()), Times.Once());
        }

        [Test]
        public void should_skip_a_deleted_row_without_a_movie_file()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(_defaultRow);

            var fileless = new Movie { Id = 9, MovieMetadataId = 3, Edition = "Unrated", Path = _extendedRow.Path, MovieFileId = 0 };

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { fileless }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.GetEditionSiblings(It.IsAny<Movie>()), Times.Never());
        }

        [Test]
        public void should_not_recycle_an_extra_path_a_remaining_sibling_also_owns()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(_defaultRow);

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Setup(s => s.GetFilesByMovie(_defaultRow.Id))
                  .Returns(new List<SubtitleFile> { new SubtitleFile { Id = 13, MovieId = _defaultRow.Id, MovieFileId = _defaultRow.MovieFileId, RelativePath = _extendedSubtitle.RelativePath } });

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_not_touch_disk_when_the_extra_of_the_deleted_row_is_already_gone()
        {
            GivenDeletedEditionRow();
            GivenRemainingSiblings(_defaultRow);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_extendedSubtitlePath))
                  .Returns(false);

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extendedRow }, true, false));

            Mocker.GetMock<IRecycleBinProvider>()
                  .Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            Mocker.GetMock<IExtraFileRepository<SubtitleFile>>()
                  .Verify(v => v.DeleteForMovies(It.IsAny<List<int>>()), Times.Once());
        }

        // krzw(multi-edition) end
    }
}
