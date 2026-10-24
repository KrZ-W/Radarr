using System.Collections.Generic;
using System.IO;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileDeletionService
{
    // krzw(multi-edition)
    [TestFixture]
    public class DeleteMoviesFixture : CoreTest<Core.MediaFiles.MediaFileDeletionService>
    {
        private const string RootFolder = @"C:\Test\Movies";
        private const int MetadataId = 7;

        private Movie _default;
        private Movie _extended;
        private string _extendedFilePath;

        [SetUp]
        public void Setup()
        {
            var moviePath = Path.Combine(RootFolder, "Movie Title").AsOsAgnostic();

            _default = new Movie { Id = 1, MovieMetadataId = MetadataId, Edition = "", Path = moviePath };
            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = MetadataId,
                Edition = "Extended",
                Path = moviePath,
                MovieFileId = 20,
                MovieFile = new MovieFile { Id = 20, MovieId = 2, RelativePath = "Movie Title {edition-Extended}.mkv" }
            };

            _extendedFilePath = Path.Combine(moviePath, "Movie Title {edition-Extended}.mkv");

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(moviePath))
                  .Returns(RootFolder.AsOsAgnostic());

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetParentFolder(_extendedFilePath))
                  .Returns(moviePath);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(moviePath))
                  .Returns(true);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_default.Id))
                  .Returns(_default);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_extended.Id))
                  .Returns(_extended);
        }

        private void GivenRemainingRows(params Movie[] rows)
        {
            var paths = new Dictionary<int, string>();

            foreach (var row in rows)
            {
                paths[row.Id] = row.Path;
            }

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(paths);
        }

        private void GivenExtendedFileOnDisk()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(_extendedFilePath))
                  .Returns(true);
        }

        [Test]
        public void should_delete_only_the_edition_rows_file_when_the_default_row_keeps_the_folder()
        {
            GivenRemainingRows(_default);
            GivenExtendedFileOnDisk();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(_extendedFilePath, "Movie Title"), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(0);
        }

        [Test]
        public void should_skip_the_file_delete_when_the_edition_row_has_no_file()
        {
            GivenRemainingRows(_default);

            var fileless = new Movie { Id = 3, MovieMetadataId = MetadataId, Edition = "Unrated", Path = _default.Path };

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { fileless }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(0);
        }

        [Test]
        public void should_not_touch_disk_when_the_edition_rows_file_is_already_gone()
        {
            GivenRemainingRows(_default);

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_delete_the_folder_when_no_other_row_shares_it()
        {
            GivenRemainingRows();
            GivenExtendedFileOnDisk();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(_extended.Path), Times.Once());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_still_refuse_when_a_foreign_movie_has_the_same_path()
        {
            var foreign = new Movie { Id = 9, MovieMetadataId = 99, Edition = "", Path = _default.Path };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(foreign.Id))
                  .Returns(foreign);

            GivenRemainingRows(foreign);
            GivenExtendedFileOnDisk();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, true, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_not_delete_anything_when_delete_files_is_off()
        {
            GivenRemainingRows(_default);
            GivenExtendedFileOnDisk();

            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, false, false));

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFolder(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
