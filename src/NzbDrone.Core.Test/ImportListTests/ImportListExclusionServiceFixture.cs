using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.ImportLists.ImportExclusions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ImportListTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class ImportListExclusionServiceFixture : CoreTest<ImportListExclusionService>
    {
        private Movie _default;
        private Movie _extended;

        [SetUp]
        public void Setup()
        {
            var metadata = new MovieMetadata { Id = 7, TmdbId = 603, Title = "The Matrix", Year = 1999 };

            _default = new Movie { Id = 1, MovieMetadataId = 7, MovieMetadata = metadata, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, MovieMetadata = metadata, Edition = "Extended" };

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Setup(s => s.AllExcludedTmdbIds())
                  .Returns(new List<int>());
        }

        [Test]
        public void should_not_exclude_a_title_when_only_an_edition_row_is_deleted()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _extended }, false, true));

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(v => v.InsertMany(It.IsAny<IList<ImportListExclusion>>()), Times.Never());
        }

        [Test]
        public void should_exclude_a_title_once_when_its_default_row_is_deleted_with_an_edition_row()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _default, _extended }, false, true));

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(v => v.InsertMany(It.Is<IList<ImportListExclusion>>(l => l.Count == 1 && l[0].TmdbId == 603)), Times.Once());
        }

        [Test]
        public void should_do_nothing_when_exclusion_was_not_requested()
        {
            Subject.HandleAsync(new MoviesDeletedEvent(new List<Movie> { _default }, false, false));

            Mocker.GetMock<IImportListExclusionRepository>()
                  .Verify(v => v.InsertMany(It.IsAny<IList<ImportListExclusion>>()), Times.Never());
        }
    }
}
