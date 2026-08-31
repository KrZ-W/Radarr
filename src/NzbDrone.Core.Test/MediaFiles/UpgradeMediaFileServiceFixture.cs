using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private const string ParkedFileSuffix = ".krzw-upgrade-bak";

        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _localMovie = new LocalMovie();
            _localMovie.Path = @"C:\Test\Unsorted\A.Movie.2019.new.mkv".AsOsAgnostic();
            _localMovie.Movie = new Movie
            {
                Path = @"C:\Test\Movies\Movie".AsOsAgnostic()
            };

            _movieFile = Builder<MovieFile>
                  .CreateNew()
                  .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localMovie.Movie.Path).FullName))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                  .Returns<string>(c => Path.GetDirectoryName(c));

            // The replacement is transferred into place by the mover; return a file with a distinct
            // relative path so the service can compute NewFilePath.
            Mocker.GetMock<IMoveMovieFiles>()
                  .Setup(c => c.MoveMovieFile(It.IsAny<MovieFile>(), It.IsAny<LocalMovie>()))
                  .Returns(new MovieFile { RelativePath = @"A.Movie.2019.new.mkv" });

            Mocker.GetMock<IMoveMovieFiles>()
                  .Setup(c => c.CopyMovieFile(It.IsAny<MovieFile>(), It.IsAny<LocalMovie>()))
                  .Returns(new MovieFile { RelativePath = @"A.Movie.2019.new.mkv" });
        }

        private void GivenSingleMovieWithSingleMovieFile()
        {
            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Id = 1,
                    RelativePath = @"A.Movie.2019.avi",
                };
        }

        private MovieFileMoveResult UpgradeAndFinalize()
        {
            var result = Subject.UpgradeMovieFile(_movieFile, _localMovie);
            Subject.FinalizeUpgrade(result);
            return result;
        }

        [Test]
        public void should_delete_single_movie_file_once()
        {
            GivenSingleMovieWithSingleMovieFile();

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_movie_file_from_database()
        {
            GivenSingleMovieWithSingleMovieFile();

            UpgradeAndFinalize();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            UpgradeAndFinalize();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_old_movie_file_in_oldFiles()
        {
            GivenSingleMovieWithSingleMovieFile();

            UpgradeAndFinalize().OldFiles.Count.Should().Be(1);
        }

        [Test]
        public void should_throw_if_there_are_existing_movie_files_and_the_root_folder_is_missing()
        {
            GivenSingleMovieWithSingleMovieFile();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localMovie.Movie.Path).FullName))
                  .Returns(false);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeMovieFile(_movieFile, _localMovie));

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localMovie.Movie.MovieFile, DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_park_existing_file_before_moving_replacement()
        {
            GivenSingleMovieWithSingleMovieFile();

            var originalPath = Path.Combine(_localMovie.Movie.Path, _localMovie.Movie.MovieFile.RelativePath);

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath, originalPath + ParkedFileSuffix, false), Times.Once());
        }

        [Test]
        public void should_not_delete_or_recycle_before_finalize()
        {
            GivenSingleMovieWithSingleMovieFile();

            Subject.UpgradeMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_restore_parked_original_and_not_delete_when_replacement_transfer_fails()
        {
            GivenSingleMovieWithSingleMovieFile();

            var originalPath = Path.Combine(_localMovie.Movie.Path, _localMovie.Movie.MovieFile.RelativePath);

            Mocker.GetMock<IMoveMovieFiles>()
                  .Setup(c => c.MoveMovieFile(It.IsAny<MovieFile>(), It.IsAny<LocalMovie>()))
                  .Throws(new IOException("Simulated destination write failure"));

            Assert.Throws<IOException>(() => Subject.UpgradeMovieFile(_movieFile, _localMovie));

            // Parked, then restored to its original location.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath, originalPath + ParkedFileSuffix, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());

            // The original must never be deleted or recycled when the replacement fails.
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_remove_replacement_and_restore_original_on_rollback()
        {
            GivenSingleMovieWithSingleMovieFile();

            var originalPath = Path.Combine(_localMovie.Movie.Path, _localMovie.Movie.MovieFile.RelativePath);
            var newPath = Path.Combine(_localMovie.Movie.Path, @"A.Movie.2019.new.mkv");

            var result = Subject.UpgradeMovieFile(_movieFile, _localMovie);
            Subject.RollbackUpgrade(result);

            // The source still exists (copy/hardlink import), so the freshly-placed replacement is
            // removed and the original restored.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(newPath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<MovieFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_return_moved_replacement_to_source_on_rollback()
        {
            GivenSingleMovieWithSingleMovieFile();

            var originalPath = Path.Combine(_localMovie.Movie.Path, _localMovie.Movie.MovieFile.RelativePath);
            var newPath = Path.Combine(_localMovie.Movie.Path, @"A.Movie.2019.new.mkv");
            var sourcePath = _localMovie.Path;

            // The source is gone (the file was moved into the library).
            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(sourcePath))
                  .Returns(false);

            // Present when parked, absent (no stray) when the original is restored.
            Mocker.GetMock<IDiskProvider>()
                  .SetupSequence(c => c.FileExists(originalPath))
                  .Returns(true)
                  .Returns(false);

            var result = Subject.UpgradeMovieFile(_movieFile, _localMovie);
            Subject.RollbackUpgrade(result);

            // The moved replacement goes back to the download location so it stays importable, and the
            // parked original is restored; nothing is permanently deleted.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(newPath, sourcePath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(newPath), Times.Never());
        }
    }
}
