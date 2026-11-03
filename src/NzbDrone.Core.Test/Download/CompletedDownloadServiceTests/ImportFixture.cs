using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class ImportFixture : CoreTest<CompletedDownloadService>
    {
        private TrackedDownload _trackedDownload;

        [SetUp]
        public void Setup()
        {
            var completed = Builder<DownloadClientItem>.CreateNew()
                                                    .With(h => h.Status = DownloadItemStatus.Completed)
                                                    .With(h => h.OutputPath = new OsPath(@"C:\DropFolder\MyDownload".AsOsAgnostic()))
                                                    .With(h => h.Title = "Drone.1998")
                                                    .Build();

            var remoteMovie = BuildRemoteMovie();

            _trackedDownload = Builder<TrackedDownload>.CreateNew()
                    .With(c => c.State = TrackedDownloadState.Downloading)
                    .With(c => c.DownloadItem = completed)
                    .With(c => c.RemoteMovie = remoteMovie)
                    .Build();

            Mocker.GetMock<IDownloadClient>()
              .SetupGet(c => c.Definition)
              .Returns(new DownloadClientDefinition { Id = 1, Name = "testClient" });

            Mocker.GetMock<IProvideDownloadClient>()
                  .Setup(c => c.Get(It.IsAny<int>()))
                  .Returns(Mocker.GetMock<IDownloadClient>().Object);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.MostRecentForDownloadId(_trackedDownload.DownloadItem.DownloadId))
                  .Returns(new MovieHistory());

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Drone.1998"))
                  .Returns(remoteMovie.Movie);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.FindByDownloadId(It.IsAny<string>()))
                  .Returns(new List<MovieHistory>());

            Mocker.GetMock<IProvideImportItemService>()
                  .Setup(s => s.ProvideImportItem(It.IsAny<DownloadClientItem>(), It.IsAny<DownloadClientItem>()))
                  .Returns<DownloadClientItem, DownloadClientItem>((i, p) => i);
        }

        private RemoteMovie BuildRemoteMovie()
        {
            return new RemoteMovie
            {
                Movie = new Movie()
            };
        }

        private void GivenABadlyNamedDownload()
        {
            _trackedDownload.DownloadItem.DownloadId = "1234";
            _trackedDownload.DownloadItem.Title = "Droned Pilot"; // Set a badly named download
            Mocker.GetMock<IHistoryService>()
               .Setup(s => s.MostRecentForDownloadId(It.Is<string>(i => i == "1234")))
               .Returns(new MovieHistory() { SourceTitle = "Droned 1998" });

            Mocker.GetMock<IParsingService>()
               .Setup(s => s.GetMovie(It.IsAny<string>()))
               .Returns((Movie)null);

            Mocker.GetMock<IParsingService>()
                .Setup(s => s.GetMovie("Droned 1998"))
                .Returns(BuildRemoteMovie().Movie);
        }

        private void GivenSeriesMatch()
        {
            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie(It.IsAny<string>()))
                  .Returns(_trackedDownload.RemoteMovie.Movie);
        }

        [Test]
        public void should_revert_import_pending_to_downloading_when_download_status_is_no_longer_completed()
        {
            _trackedDownload.State = TrackedDownloadState.ImportPending;
            _trackedDownload.Warn("Stale import warning");
            _trackedDownload.DownloadItem.Status = DownloadItemStatus.Downloading;

            Subject.Import(_trackedDownload);

            _trackedDownload.State.Should().Be(TrackedDownloadState.Downloading);
            _trackedDownload.Status.Should().Be(TrackedDownloadStatus.Ok);
            _trackedDownload.StatusMessages.Should().BeEmpty();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Verify(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()), Times.Never());
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_rejected()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure"),

                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1999.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure")
                });

            Subject.Import(_trackedDownload);

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent<DownloadCompletedEvent>(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_no_movies_were_parsed()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                .Returns(new List<ImportResult>
                {
                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure"),

                    new ImportResult(
                        new ImportDecision(
                            new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" },
                            new ImportRejection(ImportRejectionReason.Unknown, "Rejected!")),
                        "Test Failure")
                });

            _trackedDownload.RemoteMovie.Movie = new Movie();

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_not_mark_as_imported_if_all_files_were_skipped()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
                           {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" }), "Test Failure"),
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv" }), "Test Failure")
                           });

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_mark_as_imported_if_all_movies_were_imported_but_extra_files_were_not()
        {
            GivenSeriesMatch();

            _trackedDownload.RemoteMovie.Movie = new Movie();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
               {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv", Movie = _trackedDownload.RemoteMovie.Movie })),
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv" }), "Test Failure")
               });

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        [Test]
        public void should_mark_as_imported_if_the_download_can_be_tracked_using_the_source_movieid()
        {
            GivenABadlyNamedDownload();

            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(new List<ImportResult>
               {
                               new ImportResult(new ImportDecision(new LocalMovie { Path = @"C:\TestPath\Droned.S01E01.mkv", Movie = _trackedDownload.RemoteMovie.Movie }))
               });

            Mocker.GetMock<IMovieService>()
                  .Setup(v => v.GetMovie(It.IsAny<int>()))
                  .Returns(BuildRemoteMovie().Movie);

            Subject.Import(_trackedDownload);

            AssertImported();
        }

        // krzw(symlink-import-guard): a seed payload replaced by a link to the very file the movie already has
        // is nothing left to import; the download is treated as imported instead of blocked forever.
        private ImportResult GivenResult(Movie movie, ImportRejectionReason? reason)
        {
            var localMovie = new LocalMovie { Path = @"C:\TestPath\Droned.1998.mkv", Movie = movie };

            if (reason == null)
            {
                return new ImportResult(new ImportDecision(localMovie));
            }

            return new ImportResult(new ImportDecision(localMovie, new ImportRejection(reason.Value, "Source is a symbolic link → target")), "Source is a symbolic link → target");
        }

        private void GivenImportResults(params ImportResult[] results)
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                  .Setup(v => v.ProcessPath(It.IsAny<string>(), It.IsAny<ImportMode>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>()))
                  .Returns(results.ToList());
        }

        [Test]
        public void should_mark_as_imported_when_the_file_is_a_link_to_the_movies_current_file()
        {
            _trackedDownload.RemoteMovie.Movie = new Movie { Id = 42, MovieFileId = 7 };

            GivenImportResults(GivenResult(_trackedDownload.RemoteMovie.Movie, ImportRejectionReason.SourceIsSymlinkToCurrentFile));

            Subject.Import(_trackedDownload);

            AssertImported();

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.Is<DownloadCompletedEvent>(e => e.MovieId == 42)), Times.Once());

            _trackedDownload.StatusMessages.Should().BeEmpty();
        }

        [Test]
        public void should_stay_blocked_when_a_link_points_at_another_movies_library_file()
        {
            _trackedDownload.RemoteMovie.Movie = new Movie { Id = 42 };

            GivenImportResults(GivenResult(_trackedDownload.RemoteMovie.Movie, ImportRejectionReason.SourceIsSymlinkToLibraryFile));

            Subject.Import(_trackedDownload);

            AssertNotImported();
            _trackedDownload.StatusMessages.Should().NotBeEmpty();
        }

        [Test]
        public void should_stay_blocked_for_a_plain_symlink()
        {
            _trackedDownload.RemoteMovie.Movie = new Movie { Id = 42 };

            GivenImportResults(GivenResult(_trackedDownload.RemoteMovie.Movie, ImportRejectionReason.SourceIsSymlink));

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        [Test]
        public void should_stay_blocked_when_the_own_file_link_belongs_to_a_different_movie_row()
        {
            // e.g. an edition sibling: the link is that row's current file, not this download's movie
            _trackedDownload.RemoteMovie.Movie = new Movie { Id = 42 };

            GivenImportResults(GivenResult(new Movie { Id = 43, MovieFileId = 9 }, ImportRejectionReason.SourceIsSymlinkToCurrentFile));

            Subject.Import(_trackedDownload);

            AssertNotImported();
        }

        private void AssertNotImported()
        {
            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Never());

            _trackedDownload.State.Should().Be(TrackedDownloadState.ImportBlocked);
        }

        private void AssertImported()
        {
            Mocker.GetMock<IDownloadedMovieImportService>()
                .Verify(v => v.ProcessPath(_trackedDownload.DownloadItem.OutputPath.FullPath, ImportMode.Auto, _trackedDownload.RemoteMovie.Movie, _trackedDownload.DownloadItem), Times.Once());

            Mocker.GetMock<IEventAggregator>()
                  .Verify(v => v.PublishEvent(It.IsAny<DownloadCompletedEvent>()), Times.Once());

            _trackedDownload.State.Should().Be(TrackedDownloadState.Imported);
        }
    }
}
