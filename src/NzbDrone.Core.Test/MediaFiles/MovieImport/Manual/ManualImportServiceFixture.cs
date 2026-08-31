using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport.Manual;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Manual
{
    [TestFixture]
    public class ManualImportServiceFixture : CoreTest<ManualImportService>
    {
        private Movie _movie;
        private MovieFile _movieFile;

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

            Mocker.GetMock<ICustomFormatCalculationService>()
                  .Setup(s => s.ParseCustomFormat(It.IsAny<MovieFile>(), It.IsAny<Movie>()))
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
    }
}
