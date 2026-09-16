using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieFileMovingServiceTests
{
    [TestFixture]
    public class MoveMovieFileFixture : CoreTest<MovieFileMovingService>
    {
        private Movie _movie;
        private MovieFile _movieFile;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                     .With(s => s.Path = @"C:\Test\Movies\Movie".AsOsAgnostic())
                                     .Build();

            _movieFile = Builder<MovieFile>.CreateNew()
                                               .With(f => f.Path = null)
                                               .With(f => f.RelativePath = @"File.avi")
                                               .Build();

            _localMovie = Builder<LocalMovie>.CreateNew()
                                                 .With(l => l.Movie = _movie)
                                                 .Build();

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), null, null))
                  .Returns("File Name");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.BuildFilePath(It.IsAny<Movie>(), It.IsAny<string>(), It.IsAny<string>()))
                  .Returns(@"C:\Test\Movies\Movie\File Name.avi".AsOsAgnostic());

            var rootFolder = @"C:\Test\Movies\".AsOsAgnostic();

            Mocker.GetMock<IRootFolderService>()
                .Setup(s => s.GetBestRootFolderPath(It.IsAny<string>(), null))
                .Returns(rootFolder);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(rootFolder))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FileExists(It.IsAny<string>()))
                  .Returns(true);
        }

        // krzw(grabbed-release-title): naming is the single carve-out from "a file's custom formats are
        // those of the highest-scoring candidate title". MovieFileMovingService is the only naming
        // consumer of a LocalMovie's formats, and it must render the LEGACY ladder. If this ever starts
        // handing BuildFileName the scoring ladder (LocalMovie.CustomFormats), switching the setting on
        // would make Rename Files propose a library-wide rename.
        [Test]
        public void should_name_from_the_naming_custom_formats_and_never_from_the_scoring_ones()
        {
            var namingFormats = new List<CustomFormat> { new CustomFormat { Id = 1, Name = "LegacyLadder" } };
            var scoringFormats = new List<CustomFormat> { new CustomFormat { Id = 2, Name = "ScoringLadder" } };

            _localMovie.NamingCustomFormats = namingFormats;
            _localMovie.CustomFormats = scoringFormats;

            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFileName(_movie, _movieFile, null, namingFormats), Times.Once());

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), It.IsAny<NamingConfig>(), scoringFormats), Times.Never());
        }

        // krzw(grabbed-release-title): the copy/hardlink path names the file the same way.
        [Test]
        public void should_name_a_copied_file_from_the_naming_custom_formats_too()
        {
            var namingFormats = new List<CustomFormat> { new CustomFormat { Id = 1, Name = "LegacyLadder" } };
            var scoringFormats = new List<CustomFormat> { new CustomFormat { Id = 2, Name = "ScoringLadder" } };

            _localMovie.NamingCustomFormats = namingFormats;
            _localMovie.CustomFormats = scoringFormats;

            Subject.CopyMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFileName(_movie, _movieFile, null, namingFormats), Times.Once());

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), It.IsAny<NamingConfig>(), scoringFormats), Times.Never());
        }

        // krzw(grabbed-release-title): a LocalMovie built outside ImportDecisionMaker/ManualImportService
        // never sets NamingCustomFormats; it must keep naming exactly as it did before the feature.
        [Test]
        public void should_fall_back_to_custom_formats_when_naming_custom_formats_were_never_set()
        {
            var formats = new List<CustomFormat> { new CustomFormat { Id = 1, Name = "LegacyLadder" } };

            _localMovie.CustomFormats = formats;

            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IBuildFileNames>()
                  .Verify(s => s.BuildFileName(_movie, _movieFile, null, formats), Times.Once());
        }

        [Test]
        public void should_catch_UnauthorizedAccessException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<UnauthorizedAccessException>();

            Subject.MoveMovieFile(_movieFile, _localMovie);
        }

        [Test]
        public void should_catch_InvalidOperationException_during_folder_inheritance()
        {
            WindowsOnly();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.InheritFolderPermissions(It.IsAny<string>()))
                  .Throws<InvalidOperationException>();

            Subject.MoveMovieFile(_movieFile, _localMovie);
        }

        [Test]
        public void should_notify_on_movie_folder_creation()
        {
            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<MovieFolderCreatedEvent>(It.Is<MovieFolderCreatedEvent>(p =>
                      p.MovieFolder.IsNotNullOrWhiteSpace())),
                      Times.Once());
        }

        [Test]
        public void should_not_notify_if_movie_folder_already_exists()
        {
            Mocker.GetMock<IDiskProvider>()
                  .Setup(s => s.FolderExists(_movie.Path))
                  .Returns(true);

            Subject.MoveMovieFile(_movieFile, _localMovie);

            Mocker.GetMock<IEventAggregator>()
                  .Verify(s => s.PublishEvent<MovieFolderCreatedEvent>(It.Is<MovieFolderCreatedEvent>(p =>
                      p.MovieFolder.IsNotNullOrWhiteSpace())),
                      Times.Never());
        }
    }
}
