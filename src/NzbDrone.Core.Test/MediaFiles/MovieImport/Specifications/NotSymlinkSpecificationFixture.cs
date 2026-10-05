using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    // krzw(symlink-import-guard)
    [TestFixture]
    public class NotSymlinkSpecificationFixture : CoreTest<NotSymlinkSpecification>
    {
        private LocalMovie _localMovie;
        private string _target;

        [SetUp]
        public void Setup()
        {
            _localMovie = new LocalMovie
            {
                Path = @"C:\Test\Downloads\Shared.Torrent\Movie.Title.2010.1080p.BluRay.x264-GROUP.mkv".AsOsAgnostic(),
                Size = 100,
                Movie = Builder<Movie>.CreateNew().Build()
            };

            _target = @"C:\Test\Movies\Other Movie (2010) {tmdb-12345}\Other.Movie.2010.mkv".AsOsAgnostic();

            GivenSettingEnabled(true);
        }

        private void GivenSettingEnabled(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.RejectSymlinkImportSources)
                  .Returns(enabled);
        }

        private void GivenSymlink()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetSymbolicLinkTarget(_localMovie.Path))
                  .Returns(_target);
        }

        [Test]
        public void should_accept_regular_file()
        {
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_symlink_with_reason_and_target()
        {
            GivenSymlink();

            var result = Subject.IsSatisfiedBy(_localMovie, null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
            result.Message.Should().Be($"Source is a symbolic link → {_target}");

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_log_warning_naming_link_and_target()
        {
            var memoryTarget = new MemoryTarget { Layout = "${level}|${message}" };
            var configuration = new LoggingConfiguration();
            configuration.AddRuleForAllLevels(memoryTarget);
            var logFactory = new LogFactory { Configuration = configuration };

            Mocker.SetConstant(logFactory.GetLogger(nameof(NotSymlinkSpecification)));

            GivenSymlink();

            Subject.IsSatisfiedBy(_localMovie, null);

            memoryTarget.Logs.Should().ContainSingle()
                        .Which.Should().StartWith("Warn|")
                        .And.Contain(_localMovie.Path)
                        .And.Contain(_target);
        }

        [Test]
        public void should_accept_symlink_when_setting_is_disabled()
        {
            GivenSettingEnabled(false);
            GivenSymlink();

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.GetSymbolicLinkTarget(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_accept_hardlink()
        {
            // A hardlink is a regular directory entry: no link target, only a link count above one
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetHardLinkCount(_localMovie.Path))
                  .Returns(2);

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();

            Mocker.GetMock<IDiskProvider>()
                  .Verify(s => s.GetHardLinkCount(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_reject_symlink_in_movie_folder_too()
        {
            _localMovie.ExistingFile = true;
            GivenSymlink();

            Subject.IsSatisfiedBy(_localMovie, null).Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_only_query_the_candidate_path()
        {
            Subject.IsSatisfiedBy(_localMovie, null);

            Mocker.GetMock<IDiskProvider>().Verify(s => s.GetSymbolicLinkTarget(_localMovie.Path), Times.Once());
            Mocker.GetMock<IDiskProvider>().VerifyNoOtherCalls();
        }
    }
}
