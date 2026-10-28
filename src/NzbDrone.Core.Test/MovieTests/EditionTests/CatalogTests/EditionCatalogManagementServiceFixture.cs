// krzw(edition-runtime-catalog): user entries and the catalog export/import behind the edition catalog API
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionCatalogManagementServiceFixture : CoreTest<EditionCatalogManagementService>
    {
        private const int TmdbId = 120;
        private const int MetadataId = 7;

        private Movie _default;
        private Movie _extended;
        private List<EditionCatalogEntry> _stored;
        private List<EditionCatalogEntry> _upserted;

        [SetUp]
        public void Setup()
        {
            var metadata = new MovieMetadata { Id = MetadataId, TmdbId = TmdbId, ImdbId = "tt0120737", Title = "The Fellowship of the Ring", Year = 2001 };

            _default = new Movie { Id = 1, MovieMetadataId = MetadataId, MovieMetadata = metadata, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = MetadataId, MovieMetadata = metadata, Edition = "Extended" };

            _stored = new List<EditionCatalogEntry>();
            _upserted = new List<EditionCatalogEntry>();

            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(1)).Returns(_default);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(2)).Returns(_extended);
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(99)).Throws(new ModelNotFoundException(typeof(Movie), 99));
            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _extended, _default });
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbIds(It.IsAny<List<int>>()))
                  .Returns((List<int> ids) => new List<Movie> { _default, _extended }.Where(m => ids.Contains(m.TmdbId)).ToList());

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.GetByMetadataIds(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _stored.Where(e => ids.Contains(e.MovieMetadataId)).ToList());

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.GetBySources(It.IsAny<IEnumerable<EditionCatalogSource>>()))
                  .Returns((IEnumerable<EditionCatalogSource> sources) => _stored.Where(e => sources.Contains(e.Source)).ToList());

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.Get(It.IsAny<int>()))
                  .Returns((int id) => _stored.SingleOrDefault(e => e.Id == id) ?? throw new ModelNotFoundException(typeof(EditionCatalogEntry), id));

            Mocker.GetMock<IEditionCatalogService>()
                  .Setup(s => s.Upsert(It.IsAny<EditionCatalogEntry>()))
                  .Returns((EditionCatalogEntry e) =>
                  {
                      _upserted.Add(e);
                      return e;
                  });
        }

        private EditionCatalogEntry GivenStored(int id, string name, int runtime, EditionCatalogSource source, int confidence = 90, int metadataId = MetadataId)
        {
            var entry = new EditionCatalogEntry
            {
                Id = id,
                MovieMetadataId = metadataId,
                Name = name,
                RuntimeMinutes = runtime,
                Source = source,
                Confidence = confidence,
                Updated = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            _stored.Add(entry);

            return entry;
        }

        private static EditionCatalogExchangeItem Item(string name, int runtime, EditionCatalogSource? source = null, int? confidence = null, int tmdbId = TmdbId)
        {
            return new EditionCatalogExchangeItem { TmdbId = tmdbId, Name = name, RuntimeMinutes = runtime, Source = source, Confidence = confidence };
        }

        [Test]
        public void add_user_entry_should_store_a_user_entry_on_the_rows_title()
        {
            Subject.AddUserEntry(2, " Extended ", 228);

            var entry = _upserted.Single();
            entry.MovieMetadataId.Should().Be(MetadataId);
            entry.Name.Should().Be("Extended");
            entry.RuntimeMinutes.Should().Be(228);
            entry.Source.Should().Be(EditionCatalogSource.User);
            entry.Confidence.Should().Be(EditionCatalogManagementService.UserConfidence);
        }

        [TestCase("", 200)]
        [TestCase("...", 200)]
        [TestCase("Extended", 0)]
        [TestCase("Extended", 1001)]
        public void add_user_entry_should_reject_a_bad_name_or_runtime(string name, int runtime)
        {
            Assert.Throws<ValidationException>(() => Subject.AddUserEntry(2, name, runtime));

            _upserted.Should().BeEmpty();
        }

        [Test]
        public void add_user_entry_should_404_for_an_unknown_movie()
        {
            Assert.Throws<ModelNotFoundException>(() => Subject.AddUserEntry(99, "Extended", 228));
        }

        [Test]
        public void delete_should_remove_a_user_entry()
        {
            GivenStored(5, "Extended", 228, EditionCatalogSource.User);

            Subject.DeleteUserEntry(5);

            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.Delete(5), Times.Once());
        }

        [TestCase(EditionCatalogSource.Imdb)]
        [TestCase(EditionCatalogSource.Library)]
        [TestCase(EditionCatalogSource.Tmdb)]
        public void delete_should_refuse_entries_that_are_not_user_entries(EditionCatalogSource source)
        {
            GivenStored(5, "Extended", 228, source);

            Assert.Throws<ValidationException>(() => Subject.DeleteUserEntry(5));

            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void export_should_key_library_and_user_entries_by_the_default_rows_tmdb_id()
        {
            GivenStored(1, "Extended", 228, EditionCatalogSource.Library);
            GivenStored(2, "Workprint", 240, EditionCatalogSource.User, 100);
            GivenStored(3, "Theatrical", 178, EditionCatalogSource.Imdb, 80);
            GivenStored(4, "Extended", 150, EditionCatalogSource.Library, 90, metadataId: 404);

            var exported = Subject.Export();

            exported.Should().HaveCount(2);
            exported.Should().OnlyContain(i => i.TmdbId == TmdbId && i.ImdbId == "tt0120737" && i.Title == "The Fellowship of the Ring");
            exported.Select(i => i.Source).Should().Equal(EditionCatalogSource.Library, EditionCatalogSource.User);
            exported.Select(i => i.RuntimeMinutes).Should().Equal(228, 240);
        }

        [Test]
        public void import_should_store_items_as_library_unless_user_is_given()
        {
            var result = Subject.Import(new List<EditionCatalogExchangeItem>
            {
                Item("Extended", 228),
                Item("Final Cut", 230, EditionCatalogSource.Imdb),
                Item("Workprint", 240, EditionCatalogSource.User)
            });

            result.Added.Should().Be(3);
            _upserted.Select(e => e.Source).Should().Equal(EditionCatalogSource.Library, EditionCatalogSource.Library, EditionCatalogSource.User);
            _upserted.Select(e => e.Confidence).Should().Equal(90, 90, 100);
            _upserted.Should().OnlyContain(e => e.MovieMetadataId == MetadataId);
        }

        [Test]
        public void import_should_skip_unknown_titles_and_invalid_items_with_a_reason()
        {
            var result = Subject.Import(new List<EditionCatalogExchangeItem>
            {
                Item("Extended", 228, tmdbId: 555),
                Item("", 228),
                Item("Extended", -1),
                null
            });

            result.Added.Should().Be(0);
            result.Skipped.Should().Be(3);
            result.Errors.Should().HaveCount(3);
            result.Errors[0].Should().Contain("555");
            _upserted.Should().BeEmpty();
        }

        [Test]
        public void import_should_not_weaken_a_stronger_local_entry()
        {
            GivenStored(1, "Extended", 228, EditionCatalogSource.Library, 90);

            var result = Subject.Import(new List<EditionCatalogExchangeItem> { Item("extended", 226, confidence: 60) });

            result.Unchanged.Should().Be(1);
            _upserted.Should().BeEmpty();
        }

        [Test]
        public void import_should_count_an_identical_entry_as_unchanged_and_update_a_different_runtime()
        {
            GivenStored(1, "Extended", 228, EditionCatalogSource.Library, 90);
            GivenStored(2, "Director's Cut", 190, EditionCatalogSource.Library, 90);

            var result = Subject.Import(new List<EditionCatalogExchangeItem>
            {
                Item("Extended", 228),
                Item("Director's Cut", 194)
            });

            result.Unchanged.Should().Be(1);
            result.Updated.Should().Be(1);
            _upserted.Single().RuntimeMinutes.Should().Be(194);
        }

        [Test]
        public void import_should_clamp_the_confidence()
        {
            Subject.Import(new List<EditionCatalogExchangeItem> { Item("Extended", 228, confidence: 500) });

            _upserted.Single().Confidence.Should().Be(100);
        }

        [Test]
        public void import_of_nothing_should_do_nothing()
        {
            var result = Subject.Import(null);

            result.Added.Should().Be(0);
            result.Skipped.Should().Be(0);
        }
    }
}
