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
    }
}
