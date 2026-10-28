using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(edition-file-reassign)
    [TestFixture]
    public class MovieEditionFileServiceFixture : CoreTest<MovieEditionFileService>
    {
        private Movie _default;
        private Movie _extended;
        private Movie _other;
        private MovieFile _file;
        private List<SubtitleFile> _subtitles;

        [SetUp]
        public void Setup()
        {
            _default = new Movie { Id = 1, MovieMetadataId = 10, Edition = "", MovieFileId = 100 };
            _extended = new Movie { Id = 2, MovieMetadataId = 10, Edition = "Extended", EditionAliases = new List<string> { "extended" } };
            _other = new Movie { Id = 3, MovieMetadataId = 20, Edition = "" };
            _file = new MovieFile { Id = 100, MovieId = 1, RelativePath = "Movie (2001) Bluray-1080p.mkv", Edition = "Extended" };
            _subtitles = new List<SubtitleFile> { new SubtitleFile { Id = 5, MovieId = 1, MovieFileId = 100 } };

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetMovie(100)).Returns(_file);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(1)).Returns(_default);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(2)).Returns(_extended);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(3)).Returns(_other);
            Mocker.GetMock<ISubtitleFileService>().Setup(s => s.GetFilesByMovieFile(100)).Returns(_subtitles);
            Mocker.GetMock<IMetadataFileService>().Setup(s => s.GetFilesByMovieFile(100)).Returns(new List<MetadataFile>());
            Mocker.GetMock<IOtherExtraFileService>().Setup(s => s.GetFilesByMovieFile(100)).Returns(new List<OtherExtraFile>());
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameMovies = true });
        }

        [Test]
        public void should_move_file_extras_and_file_ids_to_the_target_row()
        {
            Subject.MoveFileToEdition(100, 2);

            _file.MovieId.Should().Be(2);
            _default.MovieFileId.Should().Be(0);
            _extended.MovieFileId.Should().Be(100);
            _subtitles.Single().MovieId.Should().Be(2);

            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(_file), Times.Once());
            Mocker.GetMock<ISubtitleFileService>().Verify(s => s.Upsert(_subtitles), Times.Once());
            Mocker.GetMock<IMetadataFileService>().Verify(s => s.Upsert(It.IsAny<List<MetadataFile>>()), Times.Never());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(_default), Times.Once());
            Mocker.GetMock<IMovieService>().Verify(s => s.UpdateMovie(_extended), Times.Once());
        }

        [Test]
        public void should_queue_a_rename_of_the_moved_file_when_renaming_is_enabled()
        {
            Subject.MoveFileToEdition(100, 2);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(s => s.Push(It.Is<RenameFilesCommand>(c => c.MovieId == 2 && c.Files.Single() == 100), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());

            // krzw(edition-runtime-catalog): only the audit apply renames on the caller's thread
            Mocker.GetMock<IRenameMovieFileService>().Verify(s => s.RenameMovieFilesNow(It.IsAny<int>(), It.IsAny<List<int>>()), Times.Never());
        }

        [Test]
        public void should_not_queue_a_rename_when_renaming_is_disabled()
        {
            Mocker.GetMock<INamingConfigService>().Setup(s => s.GetConfig()).Returns(new NamingConfig { RenameMovies = false });

            Subject.MoveFileToEdition(100, 2);

            Mocker.GetMock<IManageCommandQueue>()
                .Verify(s => s.Push(It.IsAny<RenameFilesCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Never());
        }

        [Test]
        public void should_move_back_from_an_edition_to_the_default_row()
        {
            _file.MovieId = 2;
            _default.MovieFileId = 0;
            _extended.MovieFileId = 100;

            Subject.MoveFileToEdition(100, 1);

            _file.MovieId.Should().Be(1);
            _default.MovieFileId.Should().Be(100);
            _extended.MovieFileId.Should().Be(0);
        }

        [Test]
        public void should_reject_a_target_of_another_movie()
        {
            Assert.Throws<ValidationException>(() => Subject.MoveFileToEdition(100, 3));

            _file.MovieId.Should().Be(1);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void should_reject_a_target_that_already_has_a_file()
        {
            _extended.MovieFileId = 200;

            Assert.Throws<ValidationException>(() => Subject.MoveFileToEdition(100, 2));

            _file.MovieId.Should().Be(1);
            _default.MovieFileId.Should().Be(100);
        }

        [Test]
        public void should_reject_moving_to_the_row_that_already_owns_the_file()
        {
            Assert.Throws<ValidationException>(() => Subject.MoveFileToEdition(100, 1));
        }

        [TestCase("Extended", EditionAuditKind.Cut, "Extended")]
        [TestCase("Directors Cut", EditionAuditKind.Cut, "Director's Cut")]
        [TestCase("UNRATED", EditionAuditKind.Cut, "Unrated")]
        [TestCase("Theatrical Cut", EditionAuditKind.Theatrical, "Theatrical")]
        [TestCase("REMASTERED", EditionAuditKind.Variant, "Remastered")]
        [TestCase("iMAX", EditionAuditKind.Variant, "IMAX")]
        [TestCase("RESTORED", EditionAuditKind.Variant, "Restored")]
        [TestCase("Directors", EditionAuditKind.Cut, "Director's Cut")]
        [TestCase("Workprint", EditionAuditKind.Unrecognised, null)]
        public void audit_should_classify_the_file_edition(string fileEdition, EditionAuditKind kind, string suggested)
        {
            _file.Edition = fileEdition;
            _default.MovieFile = _file;
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _default });

            var item = Subject.Audit().Single();

            item.Kind.Should().Be(kind);
            item.SuggestedEdition.Should().Be(suggested);
            item.EditionRowId.Should().BeNull();
        }

        [Test]
        public void audit_should_flag_multi_cut_editions_as_ambiguous()
        {
            _file.Edition = "Extended Directors Cut";
            _default.MovieFile = _file;
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _default });

            Subject.Audit().Single().Ambiguous.Should().BeTrue();
        }

        [Test]
        public void audit_should_point_at_an_existing_edition_row_that_claims_the_file()
        {
            _default.MovieFile = _file;
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _default, _extended });

            var item = Subject.Audit().Single();

            item.EditionRowId.Should().Be(2);
            item.EditionRowHasFile.Should().BeFalse();
        }

        [Test]
        public void audit_should_prefer_a_plex_tag_in_the_file_name()
        {
            _file.Edition = "Remastered";
            _file.RelativePath = "Movie (2001) {edition-Extended} Bluray-1080p.mkv";
            _default.MovieFile = _file;
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _default });

            var item = Subject.Audit().Single();

            item.FileEdition.Should().Be("Extended");
            item.Kind.Should().Be(EditionAuditKind.Cut);
        }

        [Test]
        public void audit_should_skip_files_without_an_edition_and_edition_rows()
        {
            _file.Edition = "";
            _default.MovieFile = _file;
            _extended.MovieFileId = 300;
            _extended.MovieFile = new MovieFile { Id = 300, MovieId = 2, RelativePath = "x.mkv", Edition = "Extended" };
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _default, _extended });

            Subject.Audit().Should().BeEmpty();
        }
    }
}
