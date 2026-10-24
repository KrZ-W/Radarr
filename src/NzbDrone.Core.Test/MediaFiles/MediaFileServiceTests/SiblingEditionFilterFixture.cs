using System.Collections.Generic;
using System.IO;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MediaFileServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class SiblingEditionFilterFixture : CoreTest<MediaFileService>
    {
        private Movie _default;
        private List<string> _files;

        [SetUp]
        public void Setup()
        {
            _default = new Movie
            {
                Id = 10,
                MovieMetadataId = 7,
                Edition = "",
                Path = @"C:\Movies\Movie (2010)".AsOsAgnostic()
            };

            _files = new List<string>
            {
                Path.Combine(_default.Path, "Movie (2010).mkv"),
                Path.Combine(_default.Path, "Movie (2010) {edition-Extended}.mkv"),
                Path.Combine(_default.Path, "Movie (2010) {edition-Unrated}.mkv")
            };

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesByMovie(10))
                  .Returns(new List<MovieFile> { new MovieFile { MovieId = 10, RelativePath = "Movie (2010).mkv" } });
        }

        [Test]
        public void should_treat_files_of_sibling_edition_rows_as_existing()
        {
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesBySiblingEditions(7, 10))
                  .Returns(new List<MovieFile> { new MovieFile { MovieId = 11, RelativePath = "Movie (2010) {edition-Extended}.mkv" } });

            var result = Subject.FilterExistingFiles(_files, _default);

            result.Should().BeEquivalentTo(new[] { _files[2] });
        }

        [Test]
        public void should_only_filter_own_files_when_the_title_has_no_edition_rows()
        {
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesBySiblingEditions(7, 10))
                  .Returns(new List<MovieFile>());

            var result = Subject.FilterExistingFiles(_files, _default);

            result.Should().BeEquivalentTo(new[] { _files[1], _files[2] });
        }

        [Test]
        public void should_treat_a_null_sibling_result_as_no_siblings()
        {
            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesBySiblingEditions(7, 10))
                  .Returns((List<MovieFile>)null);

            Subject.FilterExistingFiles(_files, _default).Should().HaveCount(2);
        }

        [Test]
        public void should_not_query_siblings_for_an_unsaved_movie()
        {
            var unsaved = new Movie { Id = 0, MovieMetadataId = 0, Path = _default.Path };

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesByMovie(0))
                  .Returns(new List<MovieFile>());

            Subject.FilterExistingFiles(_files, unsaved).Should().BeEquivalentTo(_files);

            Mocker.GetMock<IMediaFileRepository>()
                  .Verify(v => v.GetFilesBySiblingEditions(It.IsAny<int>(), It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void get_sibling_edition_files_should_return_the_repository_result()
        {
            var siblingFile = new MovieFile { MovieId = 11, RelativePath = "Movie (2010) {edition-Extended}.mkv" };

            Mocker.GetMock<IMediaFileRepository>()
                  .Setup(c => c.GetFilesBySiblingEditions(7, 10))
                  .Returns(new List<MovieFile> { siblingFile });

            Subject.GetSiblingEditionFiles(_default).Should().ContainSingle().Which.Should().BeSameAs(siblingFile);
        }

        [Test]
        public void static_filter_should_exclude_sibling_files_handed_in_with_own_files()
        {
            var ownFiles = new List<MovieFile>
            {
                new MovieFile { MovieId = 10, RelativePath = "Movie (2010).mkv" },
                new MovieFile { MovieId = 11, RelativePath = "Movie (2010) {edition-Extended}.mkv" }
            };

            MediaFileService.FilterExistingFiles(_files, ownFiles, _default).Should().BeEquivalentTo(new[] { _files[2] });
        }
    }
}
