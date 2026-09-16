using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.GrabbedReleaseTitles
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class BackfillGrabbedReleaseTitlesServiceFixture : CoreTest<BackfillGrabbedReleaseTitlesService>
    {
        private static readonly DateTime ImportedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private MovieFile _file;
        private List<MovieHistory> _imports;
        private List<MovieHistory> _grabs;

        [SetUp]
        public void Setup()
        {
            _file = new MovieFile
            {
                Id = 11,
                MovieId = 3,
                RelativePath = "Movie (2004).mkv",
                DateAdded = ImportedAt
            };

            _imports = new List<MovieHistory>();
            _grabs = new List<MovieHistory>();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetAllMovies())
                  .Returns(new List<Movie> { new Movie { Id = 3 } });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns(() => new List<MovieFile> { _file });

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.AllByEventType(MovieHistoryEventType.DownloadFolderImported))
                  .Returns(() => _imports);

            Mocker.GetMock<IHistoryService>()
                  .Setup(s => s.AllByEventType(MovieHistoryEventType.Grabbed))
                  .Returns(() => _grabs);
        }

        private void GivenImport(string downloadId, DateTime date, int? fileId = null)
        {
            var history = new MovieHistory
            {
                MovieId = _file.MovieId,
                EventType = MovieHistoryEventType.DownloadFolderImported,
                DownloadId = downloadId,
                Date = date
            };

            if (fileId.HasValue)
            {
                history.Data.Add("fileId", fileId.Value.ToString());
            }

            _imports.Add(history);
        }

        private void GivenGrab(string downloadId, DateTime date, string sourceTitle)
        {
            _grabs.Add(new MovieHistory
            {
                MovieId = _file.MovieId,
                EventType = MovieHistoryEventType.Grabbed,
                DownloadId = downloadId,
                Date = date,
                SourceTitle = sourceTitle
            });
        }

        private void Execute()
        {
            Subject.Execute(new BackfillGrabbedReleaseTitlesCommand());
        }

        private void VerifyNotWritten()
        {
            Mocker.GetMock<IMediaFileService>()
                  .Verify(s => s.Update(It.IsAny<List<MovieFile>>()), Times.Never());
        }

        [Test]
        public void should_prefer_the_file_id_oracle_over_a_closer_import_in_time()
        {
            GivenImport("AAA", ImportedAt.AddHours(-3), _file.Id);
            GivenImport("BBB", ImportedAt, 99);
            GivenGrab("AAA", ImportedAt.AddHours(-4), "Correct.Movie.2004.1080p-GRP");
            GivenGrab("BBB", ImportedAt.AddMinutes(-10), "Wrong.Movie.2004.1080p-OTHER");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("Correct.Movie.2004.1080p-GRP");
        }

        [Test]
        public void should_skip_a_file_whose_own_import_has_no_download_id()
        {
            // A manual import carries no DownloadId and therefore no grab. Falling back to the nearest
            // import in time here is exactly what mis-attributed a neighbouring torrent's title.
            GivenImport(null, ImportedAt, _file.Id);
            GivenImport("BBB", ImportedAt.AddMinutes(1), 99);
            GivenGrab("BBB", ImportedAt, "Wrong.Movie.2004.1080p-OTHER");

            Execute();

            _file.GrabbedReleaseTitle.Should().BeNull();
            VerifyNotWritten();
        }

        [Test]
        public void should_fall_back_to_the_closest_import_within_six_hours()
        {
            GivenImport("BBB", ImportedAt.AddHours(1));
            GivenImport("CCC", ImportedAt.AddHours(5));
            GivenGrab("BBB", ImportedAt, "Closest.Movie.2004.1080p-GRP");
            GivenGrab("CCC", ImportedAt, "Farther.Movie.2004.1080p-GRP");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("Closest.Movie.2004.1080p-GRP");
        }

        [Test]
        public void should_not_match_an_import_outside_the_six_hour_window()
        {
            GivenImport("BBB", ImportedAt.AddHours(7));
            GivenGrab("BBB", ImportedAt, "Too.Far.Movie.2004.1080p-GRP");

            Execute();

            _file.GrabbedReleaseTitle.Should().BeNull();
            VerifyNotWritten();
        }

        [Test]
        public void should_match_the_download_id_case_insensitively()
        {
            GivenImport("abc123", ImportedAt, _file.Id);
            GivenGrab("ABC123", ImportedAt.AddHours(-1), "Movie.2004.1080p-GRP");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("Movie.2004.1080p-GRP");
        }

        [Test]
        public void should_use_the_latest_grab_for_the_download_id()
        {
            GivenImport("AAA", ImportedAt, _file.Id);
            GivenGrab("AAA", ImportedAt.AddHours(-5), "Older.Movie.2004.1080p-GRP");
            GivenGrab("AAA", ImportedAt.AddHours(-1), "Newer.Movie.2004.1080p-GRP");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("Newer.Movie.2004.1080p-GRP");
        }

        [Test]
        public void should_leave_the_file_untouched_when_there_is_no_grab()
        {
            GivenImport("AAA", ImportedAt, _file.Id);

            Execute();

            _file.GrabbedReleaseTitle.Should().BeNull();
            VerifyNotWritten();
        }

        [Test]
        public void should_sanitise_a_tracker_description_blob()
        {
            GivenImport("AAA", ImportedAt, _file.Id);
            GivenGrab("AAA", ImportedAt, "White.Chicks.2004.MULTi.1080p-PopHD\r\n\t\r\n\tTaille: 4 GB Seeders: 27");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("White.Chicks.2004.MULTi.1080p-PopHD");
        }

        [Test]
        public void should_be_idempotent()
        {
            _file.GrabbedReleaseTitle = "Movie.2004.1080p-GRP";

            GivenImport("AAA", ImportedAt, _file.Id);
            GivenGrab("AAA", ImportedAt, "Movie.2004.1080p-GRP");

            Execute();

            _file.GrabbedReleaseTitle.Should().Be("Movie.2004.1080p-GRP");
            VerifyNotWritten();
        }

        [Test]
        public void should_write_the_updated_files_once()
        {
            GivenImport("AAA", ImportedAt, _file.Id);
            GivenGrab("AAA", ImportedAt, "Movie.2004.1080p-GRP");

            Execute();

            Mocker.GetMock<IMediaFileService>()
                  .Verify(s => s.Update(It.Is<List<MovieFile>>(f => f.Single().Id == _file.Id)), Times.Once());
        }
    }
}
