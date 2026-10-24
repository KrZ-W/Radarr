using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Housekeeping.Housekeepers;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Housekeeping.Housekeepers
{
    public class CleanupOrphanedMovieMovieFileIdsFixture : DbTest<CleanupOrphanedMovieMovieFileIds, Movie>
    {
        [Test]
        public void should_remove_moviefileid_from_movie_referencing_deleted_moviefile()
        {
            var removedId = 2;

            var movie = Builder<Movie>.CreateNew()
                                          .With(e => e.MovieFileId = removedId)
                                          .BuildNew();

            Db.Insert(movie);

            Subject.Clean();
            AllStoredModels.Should().HaveCount(1);
            Db.All<Movie>().Should().Contain(e => e.MovieFileId == 0);
        }

        [Test]
        public void should_not_remove_moviefileid_from_movie_referencing_valid_moviefile()
        {
            var movieFiles = Builder<MovieFile>.CreateListOfSize(2)
                                                   .All()
                                                   .With(h => h.Quality = new QualityModel())
                                                   .With(h => h.Languages = new List<Language> { Language.English })
                                                   .BuildListOfNew();

            Db.InsertMany(movieFiles);

            var movie = Builder<Movie>.CreateNew()
                                          .With(e => e.MovieFileId = movieFiles.First().Id)
                                          .BuildNew();

            Db.Insert(movie);

            Subject.Clean();
            AllStoredModels.Should().HaveCount(1);
            Db.All<Movie>().Should().Contain(e => e.MovieFileId == movieFiles.First().Id);
        }

        // krzw(multi-edition) begin
        [Test]
        public void should_only_reset_the_edition_row_whose_file_is_gone()
        {
            var movieFile = Builder<MovieFile>.CreateNew()
                                              .With(h => h.Quality = new QualityModel())
                                              .With(h => h.Languages = new List<Language> { Language.English })
                                              .BuildNew();

            Db.Insert(movieFile);

            var defaultRow = Builder<Movie>.CreateNew()
                                           .With(e => e.MovieMetadataId = 7)
                                           .With(e => e.Edition = "")
                                           .With(e => e.MovieFileId = movieFile.Id)
                                           .BuildNew();

            var editionRow = Builder<Movie>.CreateNew()
                                           .With(e => e.MovieMetadataId = 7)
                                           .With(e => e.Edition = "Extended")
                                           .With(e => e.MovieFileId = movieFile.Id + 100)
                                           .BuildNew();

            Db.Insert(defaultRow);
            Db.Insert(editionRow);

            Subject.Clean();

            var rows = Db.All<Movie>().ToList();

            rows.Single(r => r.Id == defaultRow.Id).MovieFileId.Should().Be(movieFile.Id);
            rows.Single(r => r.Id == editionRow.Id).MovieFileId.Should().Be(0);
        }

        // krzw(multi-edition) end
    }
}
