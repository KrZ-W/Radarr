using System;  // krzw(multi-edition)
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;  // krzw(multi-edition)
using NzbDrone.Core.Download.TrackedDownloads;  // krzw(multi-edition)
using NzbDrone.Core.Languages;  // krzw(multi-edition)
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;  // krzw(multi-edition)
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;  // krzw(multi-edition)
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;  // krzw(multi-edition)
using NzbDrone.Core.Parser.Model;  // krzw(multi-edition)
using NzbDrone.Core.Qualities;  // krzw(multi-edition)
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Manual
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private Movie _default;  // krzw(multi-edition)
        private Movie _extended;  // krzw(multi-edition)

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 1)
                                   .With(m => m.Path = @"C:\Test\Movies\Movie".AsOsAgnostic())
                                   .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                           .With(f => f.Id = 1)
                                           .With(f => f.RelativePath = @"movie.mkv")
                                           .With(f => f.Size = 123456789)
                                           .Build();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_movie.Id))
                  .Returns(_movie);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieFile> { _movieFile });

            // krzw(multi-edition): no sibling edition rows unless a test says so
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetSiblingEditionFiles(It.IsAny<Movie>()))
                  .Returns(new List<MovieFile>());

            // krzw(grabbed-release-title): the listing scores existing files through the scoring ladder
            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormatForScoring(It.IsAny<MovieFile>(), It.IsAny<Movie>()))
                  .Returns(new List<CustomFormat>());

            // No unmapped files on disk.
            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns(new List<string>());
        }

        [Test]
        public void should_not_throw_when_existing_file_is_missing_from_disk()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(false);

            List<ManualImportItem> items = null;

            Assert.DoesNotThrow(() => items = Subject.GetMediaFiles(_movie.Id));

            items.Should().HaveCount(1);
            items.First().Size.Should().Be(_movieFile.Size);

            // The missing file must never be stat'd for size (that is what throws FileNotFoundException).
            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetFileSize(It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_use_on_disk_size_when_existing_file_is_present()
        {
            var expectedPath = Path.Combine(_movie.Path, _movieFile.RelativePath);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetFileSize(expectedPath))
                  .Returns(999);

            var items = Subject.GetMediaFiles(_movie.Id);

            items.Should().HaveCount(1);
            items.First().Size.Should().Be(999);
        }

        // krzw(multi-edition) begin: edition routing and the user-pick flag
        private void GivenEditionRows()
        {
            var metadata = new MovieMetadata
            {
                Id = 7,
                TmdbId = 603,
                Title = "Movie",
                CleanTitle = "movie",
                Year = 2010
            };

            _default = new Movie { Id = 10, MovieMetadataId = 7, MovieMetadata = metadata, Edition = "", Path = @"C:\Test\Movies\Movie (2010)".AsOsAgnostic() };
            _extended = new Movie { Id = 11, MovieMetadataId = 7, MovieMetadata = metadata, Edition = "Extended", Path = _default.Path };

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_default.Id))
                  .Returns(_default);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_extended.Id))
                  .Returns(_extended);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(_default.Id))
                  .Returns(new List<Movie> { _extended });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(_extended.Id))
                  .Returns(new List<Movie> { _default });

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.FilterPaths(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<bool>()))
                  .Returns((string root, IEnumerable<string> paths, bool extras) => paths.ToList());

            // Decisions mirror what ImportDecisionMaker would build: the router (when given) picks the row per file.
            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>(), It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<Func<string, Movie>>()))
                  .Returns((List<string> files, Movie movie, DownloadClientItem item, ParsedMovieInfo folderInfo, bool sceneSource, bool filterExisting, bool editionOverride, Func<string, Movie> router) =>
                      files.Select(f => new ImportDecision(new LocalMovie { Path = f, Movie = router?.Invoke(f) ?? movie, EditionOverride = editionOverride })).ToList());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetImportDecisions(It.IsAny<List<string>>(), It.IsAny<Movie>(), It.IsAny<DownloadClientItem>(), It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>()))
                  .Returns((List<string> files, Movie movie, DownloadClientItem item, ParsedMovieInfo folderInfo, bool sceneSource) =>
                      files.Select(f => new ImportDecision(new LocalMovie { Path = f, Movie = movie })).ToList());

            Mocker.GetMock<IMakeImportDecision>()
                  .Setup(s => s.GetDecision(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns((LocalMovie localMovie, DownloadClientItem item) => new ImportDecision(localMovie));

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Returns((LocalMovie localMovie, DownloadClientItem item) => localMovie);
        }

        private string GivenDownloadFolder(string folderName, params string[] fileNames)
        {
            var folder = Path.Combine(@"C:\Test\Downloads".AsOsAgnostic(), folderName);
            var files = fileNames.Select(f => Path.Combine(folder, f)).ToArray();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(folder))
                  .Returns(true);

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetVideoFiles(folder, It.IsAny<bool>()))
                  .Returns(files);

            return folder;
        }

        private TrackedDownload GivenTrackedDownload(string downloadId, string title, string outputPath, Movie grabbedRow)
        {
            var downloadItem = new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = title,
                OutputPath = new OsPath(outputPath)
            };

            var trackedDownload = new TrackedDownload
            {
                DownloadItem = downloadItem,
                ImportItem = downloadItem,
                RemoteMovie = new RemoteMovie { Movie = grabbedRow }
            };

            Mocker.GetMock<ITrackedDownloadService>()
                  .Setup(s => s.Find(downloadId))
                  .Returns(trackedDownload);

            return trackedDownload;
        }

        [Test]
        public void reprocess_item_should_flag_the_row_as_the_users_pick_when_the_user_picked_the_movie()
        {
            GivenEditionRows();

            var path = @"C:\Test\Downloads\Movie (2010)\Movie (2010).mkv".AsOsAgnostic();

            var item = Subject.ReprocessItem(path, null, _extended.Id, null, new QualityModel(Quality.Bluray1080p), new List<Language> { Language.English }, 0, true);

            item.Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetDecision(It.Is<LocalMovie>(l => l.EditionOverride && l.Movie == _extended), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        // A quality/language/release-group/flag edit reprocesses the row with the movie the listing preselected; that is
        // not a pick, so the edition gate must judge the row again instead of being skipped.
        [Test]
        public void reprocess_item_without_a_user_pick_should_keep_the_edition_gate_on()
        {
            GivenEditionRows();

            var path = @"C:\Test\Downloads\Movie (2010)\Movie (2010).mkv".AsOsAgnostic();

            var item = Subject.ReprocessItem(path, null, _extended.Id, null, new QualityModel(Quality.Bluray1080p), new List<Language> { Language.English }, 0, false);

            item.Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetDecision(It.Is<LocalMovie>(l => !l.EditionOverride && l.Movie == _extended), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        // krzw(edition-runtime-catalog) begin
        [Test]
        public void reprocess_item_without_a_user_pick_should_keep_a_runtime_rehome()
        {
            GivenEditionRows();

            Mocker.GetMock<IEditionRuntimeImportEvaluator>()
                  .Setup(s => s.IsRehomeTarget(It.Is<LocalMovie>(l => l.Movie == _extended)))
                  .Returns(true);

            var path = @"C:\Test\Downloads\Movie (2010)\Movie (2010).mkv".AsOsAgnostic();

            Subject.ReprocessItem(path, null, _extended.Id, null, new QualityModel(Quality.Bluray1080p), new List<Language> { Language.English }, 0, false);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetDecision(It.Is<LocalMovie>(l => !l.EditionOverride && l.EditionRuntimeRehomed && l.Movie == _extended), It.IsAny<DownloadClientItem>()), Times.Once());
        }

        // krzw(edition-runtime-catalog) end

        [Test]
        public void folder_without_a_user_pick_should_route_each_file_by_its_own_edition()
        {
            GivenEditionRows();

            var folder = GivenDownloadFolder("Movie (2010)", "Movie (2010).mkv", "Movie (2010) {edition-Extended}.mkv");

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Movie (2010)"))
                  .Returns(_default);

            var items = Subject.GetMediaFiles(folder, null, null, true);

            items.Should().HaveCount(2);
            items.Single(i => i.Path.EndsWith("Movie (2010).mkv")).Movie.Should().BeSameAs(_default);
            items.Single(i => i.Path.Contains("{edition-Extended}")).Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _default, null, It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), true, false, It.IsNotNull<Func<string, Movie>>()), Times.Once());
        }

        [Test]
        public void folder_on_a_grabbed_edition_row_should_send_a_file_naming_no_edition_row_to_the_default_row()
        {
            GivenEditionRows();

            var folder = GivenDownloadFolder("Movie.2010.1080p.BluRay-GRP", "Movie.2010.1080p.BluRay-GRP.mkv", "Movie.2010.Unrated.1080p.BluRay-GRP.mkv");
            GivenTrackedDownload("abc", "Movie.2010.1080p.BluRay-GRP", folder, _extended);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Movie.2010.1080p.BluRay-GRP"))
                  .Returns(_default);

            var items = Subject.GetMediaFiles(null, "abc", null, true);

            items.Should().HaveCount(2);
            items.Single(i => i.Path.EndsWith("Movie.2010.1080p.BluRay-GRP.mkv")).Movie.Should().BeSameAs(_extended);
            items.Single(i => i.Path.Contains("Unrated")).Movie.Should().BeSameAs(_default);
        }

        [Test]
        public void folder_without_edition_rows_should_not_route_per_file()
        {
            GivenEditionRows();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(_default.Id))
                  .Returns(new List<Movie>());

            var folder = GivenDownloadFolder("Movie (2010)", "Movie (2010) {edition-Extended}.mkv");

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Movie (2010)"))
                  .Returns(_default);

            var items = Subject.GetMediaFiles(folder, null, null, true);

            items.Single().Movie.Should().BeSameAs(_default);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _default, null, It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), true, false, null), Times.Once());
        }

        [Test]
        public void folder_with_a_user_picked_row_should_keep_that_row_for_every_file_and_flag_it()
        {
            GivenEditionRows();

            var folder = GivenDownloadFolder("Movie (2010)", "Movie (2010).mkv", "Movie (2010) {edition-Extended}.mkv");

            var items = Subject.GetMediaFiles(folder, null, _extended.Id, true);

            items.Should().HaveCount(2);
            items.Should().OnlyContain(i => i.Movie == _extended);

            Mocker.GetMock<IParsingService>().Verify(v => v.GetMovie(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _extended, null, It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), true, true, null), Times.Once());
        }

        [Test]
        public void untagged_download_folder_should_stay_on_the_row_it_was_grabbed_for()
        {
            GivenEditionRows();

            var folder = GivenDownloadFolder("Movie.2010.1080p.BluRay-GRP", "Movie.2010.1080p.BluRay-GRP.mkv");
            GivenTrackedDownload("abc", "Movie.2010.1080p.BluRay-GRP", folder, _extended);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Movie.2010.1080p.BluRay-GRP"))
                  .Returns(_default);

            var items = Subject.GetMediaFiles(null, "abc", null, true);

            items.Single().Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMakeImportDecision>()
                  .Verify(v => v.GetImportDecisions(It.IsAny<List<string>>(), _extended, It.IsNotNull<DownloadClientItem>(), It.IsAny<ParsedMovieInfo>(), It.IsAny<bool>(), true, false, It.IsAny<Func<string, Movie>>()), Times.Once());
        }

        [Test]
        public void tagged_download_folder_should_keep_the_selected_row_over_the_grabbed_row()
        {
            GivenEditionRows();

            var folder = GivenDownloadFolder("Movie.2010.Extended.1080p.BluRay-GRP", "Movie.2010.Extended.1080p.BluRay-GRP.mkv");
            GivenTrackedDownload("abc", "Movie.2010.Extended.1080p.BluRay-GRP", folder, _default);

            Mocker.GetMock<IParsingService>()
                  .Setup(s => s.GetMovie("Movie.2010.Extended.1080p.BluRay-GRP"))
                  .Returns(_extended);

            var items = Subject.GetMediaFiles(null, "abc", null, true);

            items.Single().Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void single_file_title_fallback_should_use_the_file_edition()
        {
            GivenEditionRows();

            var file = @"C:\Test\Downloads\Movie.2010.Extended.1080p.BluRay-GRP.mkv".AsOsAgnostic();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(file))
                  .Returns(true);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitle("Movie", 2010, "Extended"))
                  .Returns(_extended);

            var items = Subject.GetMediaFiles(file, null, null, true);

            items.Single().Movie.Should().BeSameAs(_extended);

            Mocker.GetMock<IMovieService>().Verify(v => v.FindByTitle(It.IsAny<string>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void movie_listing_should_not_offer_a_sibling_editions_file_as_unmapped()
        {
            GivenEditionRows();

            var ownFile = new MovieFile { Id = 5, MovieId = _default.Id, RelativePath = "Movie (2010).mkv", Size = 1, Quality = new QualityModel(Quality.Bluray1080p), Languages = new List<Language>() };
            var siblingFile = new MovieFile { Id = 6, MovieId = _extended.Id, RelativePath = "Movie (2010) {edition-Extended}.mkv", Size = 2 };
            var ownPath = Path.Combine(_default.Path, ownFile.RelativePath);
            var siblingPath = Path.Combine(_default.Path, siblingFile.RelativePath);
            var unmappedPath = Path.Combine(_default.Path, "Movie (2010) Bonus.mkv");

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(_default.Id))
                  .Returns(new List<MovieFile> { ownFile });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetSiblingEditionFiles(_default))
                  .Returns(new List<MovieFile> { siblingFile });

            Mocker.GetMock<IDiskScanService>()
                  .Setup(s => s.GetVideoFiles(_default.Path, It.IsAny<bool>()))
                  .Returns(new[] { ownPath, siblingPath, unmappedPath });

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);

            var items = Subject.GetMediaFiles(_default.Id);

            items.Select(i => i.Path).Should().BeEquivalentTo(new[] { ownPath, unmappedPath });
            items.Should().OnlyContain(i => i.Movie == _default);
        }

        // krzw(multi-edition) end
    }
}
