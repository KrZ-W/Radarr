using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Download;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    // krzw(symlink-import-guard): the spec as wired into the decision maker, for the automatic
    // (completed download handling) and the Manual Import reprocess entry points
    [TestFixture]
    public class NotSymlinkImportDecisionFixture : CoreTest<ImportDecisionMaker>
    {
        private Movie _movie;
        private string _realFile;
        private string _linkFile;
        private string _linkTarget;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                   .With(m => m.Path = @"C:\Test\Movies\Movie Title (2010)".AsOsAgnostic())
                                   .With(m => m.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                   .Build();

            _realFile = @"C:\Test\Downloads\Shared\Movie.Title.2010.1080p.WEB-DL.mkv".AsOsAgnostic();
            _linkFile = @"C:\Test\Downloads\Shared\Movie.Title.2010.1080p.BluRay.mkv".AsOsAgnostic();
            _linkTarget = @"C:\Test\Movies\Movie Title (2010)\Movie Title (2010).mkv".AsOsAgnostic();

            Mocker.SetConstant<IEnumerable<IImportDecisionEngineSpecification>>(new[] { Mocker.Resolve<NotSymlinkSpecification>() });

            Mocker.GetMock<IConfigService>()
                  .SetupGet(s => s.RejectSymlinkImportSources)
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.GetSymbolicLinkTarget(_linkFile))
                  .Returns(_linkTarget);

            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.FilterExistingFiles(It.IsAny<List<string>>(), It.IsAny<Movie>()))
                  .Returns<List<string>, Movie>((files, movie) => files);

            Mocker.GetMock<IAggregationService>()
                  .Setup(s => s.Augment(It.IsAny<LocalMovie>(), It.IsAny<DownloadClientItem>()))
                  .Callback<LocalMovie, DownloadClientItem>((localMovie, downloadClientItem) =>
                  {
                      localMovie.Movie = _movie;
                      localMovie.Quality = new QualityModel(Quality.Bluray1080p);
                  });
        }

        [Test]
        public void should_reject_only_the_symlink_permanently_during_completed_download_handling()
        {
            var downloadClientItem = Builder<DownloadClientItem>.CreateNew().Build();

            var decisions = Subject.GetImportDecisions(new List<string> { _realFile, _linkFile }, _movie, downloadClientItem, null, true);

            decisions.Single(d => d.LocalMovie.Path == _realFile).Approved.Should().BeTrue();

            var rejection = decisions.Single(d => d.LocalMovie.Path == _linkFile).Rejections.Single();
            rejection.Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);
            rejection.Type.Should().Be(RejectionType.Permanent);
            rejection.Message.Should().Be($"Source is a symbolic link → {_linkTarget}");

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_reject_symlink_when_manual_import_reprocesses_it()
        {
            var localMovie = new LocalMovie
            {
                Movie = _movie,
                Path = _linkFile,
                Quality = new QualityModel(Quality.Bluray1080p)
            };

            var decision = Subject.GetDecision(localMovie, null);

            decision.Approved.Should().BeFalse();
            decision.Rejections.Single().Reason.Should().Be(ImportRejectionReason.SourceIsSymlink);

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
