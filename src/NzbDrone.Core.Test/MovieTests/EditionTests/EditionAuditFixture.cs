// krzw(edition-runtime-catalog): audit v2 verdicts against the edition catalog, and the audit apply action
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    [TestFixture]
    public class EditionAuditFixture : CoreTest<MovieEditionFileService>
    {
        private const int MetadataId = 10;
        private const int TmdbId = 120;

        private Movie _default;
        private MovieFile _file;
        private List<Movie> _rows;
        private List<EditionCatalogEntry> _catalog;

        [SetUp]
        public void Setup()
        {
            _file = new MovieFile { Id = 100, MovieId = 1, RelativePath = "Movie (2001) Bluray-1080p.mkv", Edition = "" };
            _default = new Movie
            {
                Id = 1,
                MovieMetadataId = MetadataId,
                MovieMetadata = new MovieMetadata { Id = MetadataId, TmdbId = TmdbId, Title = "Movie", Runtime = 0 },
                Edition = "",
                MovieFileId = 100,
                MovieFile = _file,
                QualityProfileId = 7,
                Monitored = true
            };

            _rows = new List<Movie> { _default };
            _catalog = new List<EditionCatalogEntry>();

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRuntimeToleranceMinutes).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionCatalogImdbEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionCatalogWikidataEnabled).Returns(true);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(() => _rows);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(It.IsAny<int>())).Returns<int>(id => _rows.Single(r => r.Id == id));
            Mocker.GetMock<IMovieService>().Setup(s => s.FindAllByTmdbId(TmdbId)).Returns(() => _rows.ToList());
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(TmdbId)).Returns(() => _default);
            Mocker.GetMock<IEditionCatalogRepository>().Setup(s => s.GetByMetadataIds(It.IsAny<IEnumerable<int>>())).Returns(() => _catalog);
            Mocker.GetMock<IEditionCatalogService>().Setup(s => s.Merged(MetadataId)).Returns(() => EditionCatalogService.Merge(_catalog, 3));
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetMovie(100)).Returns(_file);
            Mocker.GetMock<ISubtitleFileService>().Setup(s => s.GetFilesByMovieFile(It.IsAny<int>())).Returns(new List<SubtitleFile>());
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovieFile(It.IsAny<int>())).Returns(new List<MetadataFile>());
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.GetFilesByMovieFile(It.IsAny<int>())).Returns(new List<OtherExtraFile>());
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameMovies = false });
        }

        private void GivenFile(double? minutes, string edition = "")
        {
            _file.Edition = edition;
            _file.MediaInfo = minutes.HasValue ? new MediaInfoModel { RunTime = TimeSpan.FromMinutes(minutes.Value) } : null;
        }

        private void GivenEntry(string name, int minutes, EditionCatalogSource source, int confidence)
        {
            _catalog.Add(new EditionCatalogEntry { Id = _catalog.Count + 1, MovieMetadataId = MetadataId, Name = name, RuntimeMinutes = minutes, Source = source, Confidence = confidence });
        }

        private void GivenFellowship()
        {
            GivenEntry("Default", 178, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Theatrical", 178, EditionCatalogSource.Imdb, 80);
            GivenEntry("Extended", 208, EditionCatalogSource.Imdb, 70);
            GivenEntry("Extended", 228, EditionCatalogSource.Wikidata, 70);
            GivenEntry("DVD Widescreen", 171, EditionCatalogSource.Imdb, 20);
        }

        private void GivenWaterworld()
        {
            GivenEntry("Default", 135, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Theatrical", 135, EditionCatalogSource.Imdb, 80);
            GivenEntry("Director's Cut", 176, EditionCatalogSource.Imdb, 70);
        }

        private Movie GivenEditionRow(int id, string edition, int? expected, int movieFileId = 0)
        {
            var row = new Movie
            {
                Id = id,
                MovieMetadataId = MetadataId,
                MovieMetadata = _default.MovieMetadata,
                Edition = edition,
                EditionAliases = EditionValidation.BuildAliases(edition, null),
                ExpectedRuntimeMinutes = expected,
                MovieFileId = movieFileId
            };

            _rows.Add(row);

            return row;
        }

        private EditionAuditItem AuditAll()
        {
            return Subject.Audit(true).Single();
        }

        [Test]
        public void untagged_file_at_the_theatrical_runtime_is_consistent_and_hidden_by_default()
        {
            GivenFellowship();
            GivenFile(178.4);

            Subject.Audit().Should().BeEmpty();

            var item = AuditAll();
            item.Verdict.Should().Be(EditionAuditVerdict.Consistent);
            item.Kind.Should().Be(EditionAuditKind.Untagged);
            item.FileRuntimeMinutes.Should().Be(178.4);
            item.RuntimeSuggestedEdition.Should().Be("Theatrical");
            item.CatalogSource.Should().Be(EditionCatalogSource.Imdb);
        }

        [Test]
        public void untagged_fellowship_at_228_is_runtime_only_extended()
        {
            GivenFellowship();
            GivenFile(228);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.RuntimeOnly);
            item.RuntimeMatchKind.Should().Be(RuntimeMatchKind.Unique);
            item.RuntimeSuggestedEdition.Should().Be("Extended");
            item.RuntimeSuggestedMinutes.Should().Be(228);
            item.CatalogSource.Should().Be(EditionCatalogSource.Wikidata);
            item.IsPal.Should().BeFalse();
        }

        [Test]
        public void tagged_extended_at_208_is_consistent_and_still_listed()
        {
            GivenFellowship();
            GivenFile(208.5, "Extended");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Consistent);
            item.Kind.Should().Be(EditionAuditKind.Cut);
            item.SuggestedEdition.Should().Be("Extended");
            item.RuntimeSuggestedMinutes.Should().Be(208);
        }

        [Test]
        public void tagged_extended_at_the_theatrical_runtime_is_a_conflict_when_the_extended_cut_is_known()
        {
            GivenFellowship();
            GivenFile(178, "Extended");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Conflict);
            item.RuntimeSuggestedEdition.Should().Be("Theatrical");
        }

        [Test]
        public void tagged_directors_cut_at_the_extended_runtime_is_a_conflict_pointing_at_the_extended_row()
        {
            GivenFellowship();
            GivenEditionRow(2, "Extended", 208);
            GivenEditionRow(3, "Director's Cut", null);
            GivenFile(208, "Directors Cut");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Conflict);
            item.RuntimeSuggestedEdition.Should().Be("Extended");
            item.EditionRowId.Should().Be(2);
        }

        [Test]
        public void tagged_extended_at_25_fps_is_pal()
        {
            GivenFellowship();
            GivenFile(208 * 24.0 / 25.0, "Extended");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Pal);
            item.IsPal.Should().BeTrue();
            item.RuntimeSuggestedEdition.Should().Be("Extended");
        }

        [Test]
        public void untagged_theatrical_at_25_fps_is_pal()
        {
            GivenFellowship();
            GivenFile(178 * 24.0 / 25.0);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Pal);
            item.IsPal.Should().BeTrue();
            item.RuntimeSuggestedEdition.Should().Be("Theatrical");
        }

        [Test]
        public void untagged_waterworld_at_176_is_runtime_only_directors_cut()
        {
            GivenWaterworld();
            GivenFile(176.2);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.RuntimeOnly);
            item.RuntimeSuggestedEdition.Should().Be("Director's Cut");
            item.RuntimeSuggestedMinutes.Should().Be(176);
        }

        [Test]
        public void untagged_unknown_longer_cut_is_runtime_only_without_a_suggestion()
        {
            GivenWaterworld();
            GivenFile(190);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.RuntimeOnly);
            item.RuntimeMatchKind.Should().Be(RuntimeMatchKind.UnknownLonger);
            item.RuntimeSuggestedEdition.Should().BeNull();
        }

        [Test]
        public void tagged_theatrical_at_the_directors_cut_runtime_is_a_conflict()
        {
            GivenWaterworld();
            GivenFile(176, "Theatrical");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Conflict);
            item.RuntimeSuggestedEdition.Should().Be("Director's Cut");
        }

        [Test]
        public void fast_five_tagged_extended_at_the_tmdb_runtime_is_tag_only()
        {
            GivenEntry("Default", 131, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Theatrical", 130, EditionCatalogSource.Imdb, 80);
            GivenFile(131, "Extended");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.TagOnly);
            item.RuntimeSuggestedEdition.Should().Be("Theatrical");
        }

        [Test]
        public void tagged_cut_matching_only_the_tmdb_baseline_is_tag_only_even_when_the_cut_is_known()
        {
            // Blade Runner case: TMDb may be the longer cut, never a conflict on TMDb alone
            GivenEntry("Default", 131, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Extended", 140, EditionCatalogSource.Imdb, 70);
            GivenFile(131, "Extended");

            Subject.Audit().Single().Verdict.Should().Be(EditionAuditVerdict.TagOnly);
        }

        [Test]
        public void seven_at_18_minutes_needs_its_identification_checked()
        {
            GivenEntry("Default", 127, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Theatrical", 127, EditionCatalogSource.Imdb, 80);
            GivenFile(18);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.CheckIdentification);
            item.RuntimeMatchKind.Should().Be(RuntimeMatchKind.None);
        }

        [Test]
        public void identification_check_falls_back_to_the_stored_tmdb_runtime_without_a_catalog()
        {
            _default.MovieMetadata.Value.Runtime = 127;
            GivenFile(18);

            Subject.Audit().Single().Verdict.Should().Be(EditionAuditVerdict.CheckIdentification);
        }

        [Test]
        public void a_runtime_that_misses_but_is_close_to_the_references_is_unknown()
        {
            GivenWaterworld();
            GivenFile(150);

            Subject.Audit().Should().BeEmpty();
            AuditAll().Verdict.Should().Be(EditionAuditVerdict.Unknown);
        }

        [Test]
        public void tagged_file_without_media_info_is_unknown_but_listed()
        {
            GivenFellowship();
            GivenFile(null, "Extended");

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.Unknown);
            item.FileRuntimeMinutes.Should().BeNull();
        }

        [Test]
        public void transfer_labels_never_drive_a_verdict()
        {
            GivenEntry("Theatrical", 135, EditionCatalogSource.Imdb, 80);
            GivenEntry("TV", 110, EditionCatalogSource.Imdb, 20);
            GivenFile(110);

            AuditAll().Verdict.Should().Be(EditionAuditVerdict.Unknown);
        }

        [Test]
        public void a_sibling_rows_expected_runtime_counts_as_a_known_cut()
        {
            GivenEntry("Default", 163, EditionCatalogSource.Tmdb, 30);
            GivenEntry("Theatrical", 163, EditionCatalogSource.Imdb, 80);
            GivenEditionRow(2, "Director's Cut", 196);
            GivenFile(196);

            var item = Subject.Audit().Single();

            item.Verdict.Should().Be(EditionAuditVerdict.RuntimeOnly);
            item.RuntimeSuggestedEdition.Should().Be("Director's Cut");
            item.CatalogSource.Should().Be(EditionCatalogSource.Library);
            item.EditionRowId.Should().Be(2);
            item.EditionRowHasFile.Should().BeFalse();
        }

        [Test]
        public void audit_ignores_edition_rows_and_default_rows_without_files()
        {
            GivenFellowship();
            GivenFile(228);
            GivenEditionRow(2, "Extended", 208, 300).MovieFile = new MovieFile { Id = 300, MovieId = 2, RelativePath = "x.mkv" };
            _default.MovieFileId = 0;

            Subject.Audit(true).Should().BeEmpty();
        }

        private void GivenAddEditionCreates(int id)
        {
            Mocker.GetMock<IAddMovieEditionService>()
                .Setup(s => s.AddEdition(It.IsAny<Movie>(), TmdbId))
                .Returns<Movie, int>((m, t) => GivenEditionRow(id, m.Edition, m.ExpectedRuntimeMinutes));
        }

        [Test]
        public void apply_creates_the_row_records_a_library_entry_and_moves_the_file()
        {
            GivenFellowship();
            GivenFile(228);
            GivenAddEditionCreates(5);

            var result = Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended", Aliases = new List<string> { "Blu Ray Extended Edition" } } }).Single();

            result.Ok.Should().BeTrue();
            result.Error.Should().BeNull();
            result.EditionMovieId.Should().Be(5);
            result.MovieFileId.Should().Be(100);

            _file.MovieId.Should().Be(5);
            _default.MovieFileId.Should().Be(0);
            _rows.Single(r => r.Id == 5).MovieFileId.Should().Be(100);

            Mocker.GetMock<IAddMovieEditionService>()
                .Verify(s => s.AddEdition(It.Is<Movie>(m => m.Edition == "Extended" &&
                                                            m.ExpectedRuntimeMinutes == 228 &&
                                                            m.QualityProfileId == 7 &&
                                                            m.Monitored &&
                                                            m.EditionAliases.Contains("Blu Ray Extended Edition") &&
                                                            m.AddOptions.SearchForMovie == false),
                                          TmdbId),
                        Times.Once());

            Mocker.GetMock<IEditionCatalogService>()
                .Verify(s => s.Upsert(It.Is<EditionCatalogEntry>(e => e.MovieMetadataId == MetadataId &&
                                                                     e.Name == "Extended" &&
                                                                     e.RuntimeMinutes == 228 &&
                                                                     e.Source == EditionCatalogSource.Library &&
                                                                     e.Confidence == 90)),
                        Times.Once());
        }

        [Test]
        public void apply_renames_the_moved_file_before_returning_instead_of_queueing_a_rename()
        {
            GivenFellowship();
            GivenFile(228);
            GivenAddEditionCreates(5);
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameMovies = true });

            int? ownerAtRename = null;
            Mocker.GetMock<IRenameMovieFileService>()
                  .Setup(s => s.RenameMovieFilesNow(5, It.Is<List<int>>(l => l.Single() == 100)))
                  .Callback(() => ownerAtRename = _file.MovieId);

            Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" } }).Single().Ok.Should().BeTrue();

            ownerAtRename.Should().Be(5);
            Mocker.GetMock<IRenameMovieFileService>().Verify(s => s.RenameMovieFilesNow(5, It.IsAny<List<int>>()), Times.Once());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Push(It.IsAny<RenameFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void apply_renames_nothing_when_renaming_is_off()
        {
            GivenFellowship();
            GivenFile(228);
            GivenAddEditionCreates(5);

            Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" } }).Single().Ok.Should().BeTrue();

            Mocker.GetMock<IRenameMovieFileService>().Verify(s => s.RenameMovieFilesNow(It.IsAny<int>(), It.IsAny<List<int>>()), Times.Never());
            Mocker.GetMock<IManageCommandQueue>()
                  .Verify(s => s.Push(It.IsAny<RenameFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void apply_de_pals_the_recorded_runtime()
        {
            GivenFellowship();
            GivenFile(228 * 24.0 / 25.0);
            GivenAddEditionCreates(5);

            Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" } }).Single().Ok.Should().BeTrue();

            Mocker.GetMock<IEditionCatalogService>()
                .Verify(s => s.Upsert(It.Is<EditionCatalogEntry>(e => e.RuntimeMinutes == 228)), Times.Once());
        }

        [Test]
        public void apply_uses_the_given_runtime_and_an_existing_empty_row()
        {
            GivenFile(196);
            var row = GivenEditionRow(2, "Director's Cut", null);

            var result = Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "directors cut", RuntimeMinutes = 195 } }).Single();

            result.Ok.Should().BeTrue();
            result.EditionMovieId.Should().Be(2);
            row.MovieFileId.Should().Be(100);
            row.ExpectedRuntimeMinutes.Should().Be(195);

            Mocker.GetMock<IAddMovieEditionService>().Verify(s => s.AddEdition(It.IsAny<Movie>(), It.IsAny<int>()), Times.Never());
            Mocker.GetMock<IEditionCatalogService>()
                .Verify(s => s.Upsert(It.Is<EditionCatalogEntry>(e => e.Name == "Director's Cut" && e.RuntimeMinutes == 195)), Times.Once());
        }

        [Test]
        public void apply_isolates_errors_per_item_and_never_moves_a_file_twice()
        {
            GivenFellowship();
            GivenFile(228);
            GivenAddEditionCreates(5);

            var other = new Movie { Id = 8, MovieMetadataId = 30, MovieMetadata = new MovieMetadata { Id = 30, TmdbId = 999 }, Edition = "", MovieFileId = 0 };
            _rows.Add(other);

            var results = Subject.Apply(new List<EditionAuditApplyItem>
            {
                new EditionAuditApplyItem { MovieId = 8, EditionName = "Extended" },
                new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" },
                new EditionAuditApplyItem { MovieId = 1, EditionName = "Director's Cut" },
                new EditionAuditApplyItem { MovieId = 42, EditionName = "Extended" }
            });

            results.Should().HaveCount(4);
            results[0].Ok.Should().BeFalse();
            results[0].Error.Should().NotBeNullOrWhiteSpace();
            results[1].Ok.Should().BeTrue();
            results[2].Ok.Should().BeFalse();
            results[3].Ok.Should().BeFalse();

            _file.MovieId.Should().Be(5);
            Mocker.GetMock<IAddMovieEditionService>().Verify(s => s.AddEdition(It.IsAny<Movie>(), It.IsAny<int>()), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(_file), Times.Once());
        }

        [Test]
        public void apply_refuses_a_target_row_that_already_has_a_file_before_changing_anything()
        {
            GivenFellowship();
            GivenFile(228);
            GivenEditionRow(2, "Extended", 228, 300);

            var result = Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" } }).Single();

            result.Ok.Should().BeFalse();
            result.Error.Should().Contain("already has a file");
            _file.MovieId.Should().Be(1);
            Mocker.GetMock<IEditionCatalogService>().Verify(s => s.Upsert(It.IsAny<EditionCatalogEntry>()), Times.Never());
        }

        [Test]
        public void apply_reports_a_failing_row_creation_and_continues()
        {
            GivenFellowship();
            GivenFile(228);
            Mocker.GetMock<IAddMovieEditionService>().Setup(s => s.AddEdition(It.IsAny<Movie>(), TmdbId)).Throws(new InvalidOperationException("db down"));

            var result = Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "Extended" } }).Single();

            result.Ok.Should().BeFalse();
            result.Error.Should().Be("db down");
            _file.MovieId.Should().Be(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void apply_rejects_an_invalid_edition_name()
        {
            GivenFile(228);

            var result = Subject.Apply(new List<EditionAuditApplyItem> { new EditionAuditApplyItem { MovieId = 1, EditionName = "  " } }).Single();

            result.Ok.Should().BeFalse();
            _file.MovieId.Should().Be(1);
        }
    }
}
