using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.OrganizerTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class FileNameSampleServiceFixture : CoreTest<FileNameSampleService>
    {
        private NamingConfig _namingConfig;
        private Movie _fileSampleMovie;
        private Movie _folderSampleMovie;

        [SetUp]
        public void Setup()
        {
            _namingConfig = NamingConfig.Default;
            _fileSampleMovie = null;
            _folderSampleMovie = null;

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(b => b.BuildFileName(It.IsAny<Movie>(), It.IsAny<MovieFile>(), It.IsAny<NamingConfig>(), It.IsAny<List<CustomFormat>>()))
                  .Callback<Movie, MovieFile, NamingConfig, List<CustomFormat>>((movie, file, config, formats) => _fileSampleMovie = movie)
                  .Returns("sample");

            Mocker.GetMock<IBuildFileNames>()
                  .Setup(b => b.GetMovieFolder(It.IsAny<Movie>(), It.IsAny<NamingConfig>()))
                  .Callback<Movie, NamingConfig>((movie, config) => _folderSampleMovie = movie)
                  .Returns("folder");
        }

        [Test]
        public void should_use_default_row_when_format_has_no_edition_name_token()
        {
            var result = Subject.GetMovieSample(_namingConfig);

            _fileSampleMovie.Should().NotBeNull();
            _fileSampleMovie.IsDefaultEdition.Should().BeTrue();
            _fileSampleMovie.Edition.Should().Be("");
            result.Movie.Should().BeSameAs(_fileSampleMovie);
            result.FileName.Should().Be("sample");
        }

        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full}")]
        [TestCase("{Movie Title} ({Release Year}) [{Edition Name}]")]
        [TestCase("{Movie Title} ({Release Year}) {Edition Name:10}")]
        [TestCase("{Movie Title} ({Release Year}) {edition name}")]
        [TestCase("{Movie Title} ({Release Year}) {Edition.Name}")]
        public void should_use_edition_row_when_format_has_edition_name_token(string format)
        {
            _namingConfig.StandardMovieFormat = format;

            var result = Subject.GetMovieSample(_namingConfig);

            _fileSampleMovie.Should().NotBeNull();
            _fileSampleMovie.IsEdition.Should().BeTrue();
            _fileSampleMovie.Edition.Should().Be("Director's Cut");
            _fileSampleMovie.EditionAliases.Should().NotBeEmpty();
            _fileSampleMovie.Title.Should().Be("The Movie: Title");
            _fileSampleMovie.Year.Should().Be(2010);
            result.Movie.Should().BeSameAs(_fileSampleMovie);
        }

        [Test]
        public void should_keep_default_row_for_edition_tags_token()
        {
            _namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) {edition-{Edition Tags}}";

            Subject.GetMovieSample(_namingConfig);

            _fileSampleMovie.IsDefaultEdition.Should().BeTrue();
        }

        [Test]
        public void should_use_default_row_for_the_folder_sample()
        {
            _namingConfig.StandardMovieFormat = "{Movie Title} ({Release Year}) {edition-{Edition Name}}";

            Subject.GetMovieFolderSample(_namingConfig).Should().Be("folder");

            _folderSampleMovie.Should().NotBeNull();
            _folderSampleMovie.IsDefaultEdition.Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void should_fall_back_to_default_row_for_blank_standard_format(string format)
        {
            _namingConfig.StandardMovieFormat = format;

            Subject.GetMovieSample(_namingConfig).Movie.IsDefaultEdition.Should().BeTrue();
        }
    }
}
