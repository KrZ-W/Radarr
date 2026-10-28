// krzw(edition-runtime-catalog): library learning at import - edition rows only, only for a cut the catalog does not know
using System;
using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionCatalogImportLearningFixture : CoreTest<EditionCatalogImportLearning>
    {
        private const int MetadataId = 9;

        private Movie _extended;
        private List<EditionCatalogEntry> _merged;

        [SetUp]
        public void Setup()
        {
            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = MetadataId,
                Edition = "Extended",
                EditionAliases = new List<string> { "Extended", "Extended Cut", "Extended Edition" }
            };

            _merged = new List<EditionCatalogEntry>
            {
                new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Theatrical", RuntimeMinutes = 178, Source = EditionCatalogSource.Imdb, Confidence = 80 }
            };

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRuntimeToleranceMinutes).Returns(3);
            Mocker.GetMock<IEditionCatalogService>().Setup(s => s.Merged(MetadataId)).Returns(() => _merged);
        }

        private MovieFileImportedEvent GivenImport(Movie movie, double minutes, decimal fps = 23.976m)
        {
            var mediaInfo = new MediaInfoModel { RunTime = TimeSpan.FromMinutes(minutes), VideoFps = fps };
            var localMovie = new LocalMovie { Movie = movie, MediaInfo = mediaInfo, Path = "/downloads/movie.mkv" };

            return new MovieFileImportedEvent(localMovie, new MovieFile { MediaInfo = mediaInfo }, new List<DeletedMovieFile>(), true, null);
        }

        private void VerifyLearned(int minutes)
        {
            Mocker.GetMock<IEditionCatalogService>()
                  .Verify(s => s.Upsert(It.Is<EditionCatalogEntry>(e => e.MovieMetadataId == MetadataId &&
                                                                        e.Name == "Extended" &&
                                                                        e.RuntimeMinutes == minutes &&
                                                                        e.Source == EditionCatalogSource.Library &&
                                                                        e.Confidence == 60)),
                          Times.Once());
        }

        private void VerifyNotLearned()
        {
            Mocker.GetMock<IEditionCatalogService>().Verify(s => s.Upsert(It.IsAny<EditionCatalogEntry>()), Times.Never());
        }

        [Test]
        public void should_learn_the_runtime_of_an_unknown_cut_imported_into_an_edition_row()
        {
            Subject.Handle(GivenImport(_extended, 208.4));

            VerifyLearned(208);
        }

        [Test]
        public void should_learn_the_film_runtime_of_a_pal_file()
        {
            // 208 min film at 25 fps
            Subject.Handle(GivenImport(_extended, 199.7, 25m));

            VerifyLearned(208);
        }

        [Test]
        public void should_take_the_pal_speed_up_from_the_expected_runtime()
        {
            _extended.ExpectedRuntimeMinutes = 208;

            Subject.Handle(GivenImport(_extended, 199.7));

            VerifyLearned(208);
        }

        [TestCase("Extended")]
        [TestCase("Extended Edition")]
        public void should_not_learn_when_the_catalog_already_knows_the_cut(string name)
        {
            _merged.Add(new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = name, RuntimeMinutes = 228, Source = EditionCatalogSource.Wikidata, Confidence = 60 });

            Subject.Handle(GivenImport(_extended, 208));

            VerifyNotLearned();
        }

        [Test]
        public void should_not_learn_for_a_default_row()
        {
            var movie = new Movie { Id = 1, MovieMetadataId = MetadataId, Edition = "" };

            Subject.Handle(GivenImport(movie, 178));

            VerifyNotLearned();
            Mocker.GetMock<IEditionCatalogService>().Verify(s => s.Merged(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_not_learn_without_a_runtime()
        {
            Subject.Handle(GivenImport(_extended, 0));

            VerifyNotLearned();
        }
    }
}
