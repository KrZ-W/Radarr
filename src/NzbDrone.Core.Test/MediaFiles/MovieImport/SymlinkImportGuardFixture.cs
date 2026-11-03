using System.Collections.Generic;
using System.IO;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    // krzw(symlink-import-guard)
    [TestFixture]
    public class SymlinkImportGuardFixture : CoreTest<SymlinkImportGuard>
    {
        private Movie _movie;
        private Movie _otherMovie;
        private MovieFile _currentFile;
        private MovieFile _otherFile;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _currentFile = Builder<MovieFile>.CreateNew()
                                             .With(f => f.Id = 10)
                                             .With(f => f.MovieId = 5)
                                             .With(f => f.Path = null)
                                             .With(f => f.RelativePath = "The Sound of Music (1965) Bluray-1080p.mkv")
                                             .Build();

            _otherFile = Builder<MovieFile>.CreateNew()
                                           .With(f => f.Id = 11)
                                           .With(f => f.MovieId = 6)
                                           .With(f => f.Path = null)
                                           .With(f => f.RelativePath = "Jennifer's Body (2009) Bluray-1080p.mkv")
                                           .Build();

            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Id = 5)
                                   .With(m => m.Title = "The Sound of Music")
                                   .With(m => m.Year = 1965)
                                   .With(m => m.Edition = null)
                                   .With(m => m.Path = @"C:\Test\Movies\The Sound of Music (1965)".AsOsAgnostic())
                                   .With(m => m.MovieFileId = _currentFile.Id)
                                   .Build();

            _otherMovie = Builder<Movie>.CreateNew()
                                        .With(m => m.Id = 6)
                                        .With(m => m.Title = "Jennifer's Body")
                                        .With(m => m.Year = 2009)
                                        .With(m => m.Edition = null)
                                        .With(m => m.Path = @"C:\Test\Movies\Jennifer's Body (2009)".AsOsAgnostic())
                                        .With(m => m.MovieFileId = _otherFile.Id)
                                        .Build();

            _localMovie = new LocalMovie
            {
                Path = @"C:\seed\The.Sound.of.Music.1965.1080p.BluRay-GROUP\The.Sound.of.Music.1965.mkv".AsOsAgnostic(),
                Movie = _movie
            };

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.RejectSymlinkImportSources)
                  .Returns(true);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(It.IsAny<int>()))
                  .Returns(new List<MovieFile>());

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(_movie.Id))
                  .Returns(new List<MovieFile> { _currentFile });

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(_otherMovie.Id))
                  .Returns(new List<MovieFile> { _otherFile });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.AllMoviePaths())
                  .Returns(new Dictionary<int, string> { { _movie.Id, _movie.Path }, { _otherMovie.Id, _otherMovie.Path } });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovie(_otherMovie.Id))
                  .Returns(_otherMovie);
        }

        private void GivenLinkTo(string target)
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetSymbolicLinkTarget(_localMovie.Path))
                  .Returns(target);
        }

        private static string LibraryPath(Movie movie, MovieFile file)
        {
            return Path.Combine(movie.Path, file.RelativePath);
        }

        [Test]
        public void should_return_null_for_regular_file()
        {
            Subject.Check(_localMovie).Should().BeNull();
        }

        [Test]
        public void should_return_null_when_setting_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.RejectSymlinkImportSources).Returns(false);
            GivenLinkTo(LibraryPath(_movie, _currentFile));

            Subject.Check(_localMovie).Should().BeNull();

            Mocker.GetMock<IDiskProvider>().Verify(v => v.GetSymbolicLinkTarget(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_null_for_a_candidate_without_path()
        {
            Subject.Check(new LocalMovie()).Should().BeNull();
            Subject.Check(null).Should().BeNull();
        }

        [Test]
        public void should_report_plain_symlink_when_target_is_not_in_the_library()
        {
            var target = @"C:\somewhere\else.mkv".AsOsAgnostic();
            GivenLinkTo(target);

            var check = Subject.Check(_localMovie);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
            check.Target.Should().Be(target);
            check.LibraryFile.Should().BeNull();
            check.IsCurrentFile.Should().BeFalse();
            check.Message.Should().Be($"Source is a symbolic link → {target}");
        }

        [Test]
        public void should_report_current_file_when_target_is_the_movies_own_file()
        {
            // The queue items stuck forever: a seed payload relinked to the library copy it was imported as
            GivenLinkTo(LibraryPath(_movie, _currentFile));

            var check = Subject.Check(_localMovie);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToCurrentFile);
            check.IsCurrentFile.Should().BeTrue();
            check.LibraryFile.Id.Should().Be(_currentFile.Id);
            check.Message.Should().Be($"Source is a symbolic link → {LibraryPath(_movie, _currentFile)}, which is already the current file of this movie");
        }

        [Test]
        public void should_report_library_file_when_the_movie_has_no_current_file()
        {
            _movie.MovieFileId = 0;
            GivenLinkTo(LibraryPath(_movie, _currentFile));

            var check = Subject.Check(_localMovie);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);
            check.Message.Should().EndWith("which is already the library file of The Sound of Music (1965)");
        }

        [Test]
        public void should_report_library_file_of_another_movie_with_a_readable_description()
        {
            GivenLinkTo(LibraryPath(_otherMovie, _otherFile));

            var check = Subject.Check(_localMovie);

            check.Reason.Should().Be(ImportRejectionReason.SourceIsSymlinkToLibraryFile);
            check.IsCurrentFile.Should().BeFalse();
            check.LibraryFile.Id.Should().Be(_otherFile.Id);
            check.Message.Should().Be($"Source is a symbolic link → {LibraryPath(_otherMovie, _otherFile)}, which is already the library file of Jennifer's Body (2009)");
        }

        [Test]
        public void should_name_the_edition_of_the_owning_row()
        {
            _otherMovie.Edition = "Extended";
            GivenLinkTo(LibraryPath(_otherMovie, _otherFile));

            Subject.Check(_localMovie).Message.Should().EndWith("Jennifer's Body (2009) [Extended]");
        }

        [Test]
        public void should_not_treat_an_untracked_file_in_a_movie_folder_as_a_library_file()
        {
            GivenLinkTo(Path.Combine(_movie.Path, "untracked.mkv"));

            Subject.Check(_localMovie).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
        }

        [Test]
        public void should_report_plain_symlink_when_candidate_has_no_movie_and_target_is_unknown()
        {
            _localMovie.Movie = null;
            GivenLinkTo(@"C:\somewhere\else.mkv".AsOsAgnostic());

            Subject.Check(_localMovie).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
        }
    }
}
