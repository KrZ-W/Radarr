using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles
{
    // krzw(multi-edition)
    [TestFixture]
    public class MediaFileRepositoryEditionFixture : DbTest<MediaFileRepository, MovieFile>
    {
        private Movie _default;
        private Movie _extended;
        private Movie _otherTitle;
        private MovieFile _defaultFile;
        private MovieFile _extendedFile;
        private MovieFile _otherFile;

        [SetUp]
        public void Setup()
        {
            _default = NewRow(7, "");
            _extended = NewRow(7, "Extended");
            _otherTitle = NewRow(8, "");

            Db.Insert(_default);
            Db.Insert(_extended);
            Db.Insert(_otherTitle);

            _defaultFile = NewFile(_default.Id, "Movie (2010).mkv");
            _extendedFile = NewFile(_extended.Id, "Movie (2010) {edition-Extended}.mkv");
            _otherFile = NewFile(_otherTitle.Id, "Other (2011).mkv");

            Db.InsertMany(new List<MovieFile> { _defaultFile, _extendedFile, _otherFile });
        }

        private static Movie NewRow(int movieMetadataId, string edition)
        {
            return Builder<Movie>.CreateNew()
                                 .With(m => m.Id = 0)
                                 .With(m => m.MovieMetadataId = movieMetadataId)
                                 .With(m => m.MovieFileId = 0)
                                 .With(m => m.Edition = edition)
                                 .With(m => m.EditionAliases = new List<string>())
                                 .BuildNew();
        }

        private static MovieFile NewFile(int movieId, string relativePath)
        {
            return Builder<MovieFile>.CreateNew()
                                     .With(f => f.Id = 0)
                                     .With(f => f.MovieId = movieId)
                                     .With(f => f.RelativePath = relativePath)
                                     .With(f => f.Quality = new QualityModel(Quality.Bluray1080p))
                                     .With(f => f.Languages = new List<Language> { Language.English })
                                     .BuildNew();
        }

        [Test]
        public void should_return_the_files_of_the_other_rows_of_the_title()
        {
            var fromDefault = Subject.GetFilesBySiblingEditions(7, _default.Id);
            var fromExtended = Subject.GetFilesBySiblingEditions(7, _extended.Id);

            fromDefault.Should().ContainSingle().Which.Id.Should().Be(_extendedFile.Id);
            fromExtended.Should().ContainSingle().Which.Id.Should().Be(_defaultFile.Id);
        }

        [Test]
        public void should_return_nothing_for_a_title_without_other_rows()
        {
            Subject.GetFilesBySiblingEditions(8, _otherTitle.Id).Should().BeEmpty();
        }

        [Test]
        public void should_map_the_movie_file_columns()
        {
            var file = Subject.GetFilesBySiblingEditions(7, _default.Id)[0];

            file.MovieId.Should().Be(_extended.Id);
            file.RelativePath.Should().Be("Movie (2010) {edition-Extended}.mkv");
            file.Quality.Quality.Should().Be(Quality.Bluray1080p);
        }
    }
}
