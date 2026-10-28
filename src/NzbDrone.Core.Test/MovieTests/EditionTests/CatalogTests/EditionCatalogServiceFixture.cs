// krzw(edition-runtime-catalog): catalog service (merge priority and collapse, upsert, source replacement)
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionCatalogServiceFixture : CoreTest<EditionCatalogService>
    {
        private const int MetadataId = 7;
        private List<EditionCatalogEntry> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<EditionCatalogEntry>();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionRuntimeToleranceMinutes).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogWikidataEnabled).Returns(true);

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.GetByMetadata(It.IsAny<int>()))
                  .Returns((int id) => _stored.Where(e => e.MovieMetadataId == id).ToList());

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.Insert(It.IsAny<EditionCatalogEntry>()))
                  .Returns((EditionCatalogEntry e) =>
                  {
                      e.Id = _stored.Count + 1;
                      _stored.Add(e);
                      return e;
                  });

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.Update(It.IsAny<EditionCatalogEntry>()))
                  .Returns((EditionCatalogEntry e) => e);
        }

        private static EditionCatalogEntry Entry(string name, int runtime, EditionCatalogSource source, int confidence = 70, int id = 0, params string[] attributes)
        {
            return new EditionCatalogEntry
            {
                Id = id,
                MovieMetadataId = MetadataId,
                Name = name,
                RuntimeMinutes = runtime,
                Source = source,
                Confidence = confidence,
                Attributes = attributes.ToList(),
                Updated = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            };
        }

        private void GivenStored(params EditionCatalogEntry[] entries)
        {
            _stored.AddRange(entries);
        }

        [Test]
        public void merged_should_collapse_the_same_name_within_tolerance_into_the_strongest_source()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Imdb, 60, 1, "Special DVD Extended Edition"),
                        Entry("Extended", 209, EditionCatalogSource.Wikidata, 70, 2),
                        Entry("extended", 207, EditionCatalogSource.Library, 90, 3),
                        Entry("Theatrical", 178, EditionCatalogSource.Imdb, 80, 4));

            var merged = Subject.Merged(MetadataId);

            merged.Should().HaveCount(2);
            merged[0].Name.Should().Be("Theatrical");
            merged[1].Source.Should().Be(EditionCatalogSource.Library);
            merged[1].RuntimeMinutes.Should().Be(207);
            merged[1].Attributes.Should().Contain("Special DVD Extended Edition");
        }

        [TestCase(EditionCatalogSource.User, EditionCatalogSource.Library)]
        [TestCase(EditionCatalogSource.Library, EditionCatalogSource.Imdb)]
        [TestCase(EditionCatalogSource.Imdb, EditionCatalogSource.Wikidata)]
        [TestCase(EditionCatalogSource.Wikidata, EditionCatalogSource.Tmdb)]
        public void merged_should_prefer_the_higher_priority_source(EditionCatalogSource stronger, EditionCatalogSource weaker)
        {
            // the weaker source has the higher confidence: priority still wins
            GivenStored(Entry("Director's Cut", 190, weaker, 95, 1), Entry("Directors Cut", 191, stronger, 10, 2));

            Subject.Merged(MetadataId).Should().ContainSingle().Which.Source.Should().Be(stronger);
        }

        [Test]
        public void should_hide_the_entries_of_a_provider_switched_off_in_settings()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Imdb, 70, 1),
                        Entry("Director's Cut", 228, EditionCatalogSource.Wikidata, 70, 2),
                        Entry("Default", 178, EditionCatalogSource.Tmdb, 30, 3));

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(false);

            Subject.GetForMetadata(MetadataId).Select(e => e.Source).Should().BeEquivalentTo(new[] { EditionCatalogSource.Wikidata, EditionCatalogSource.Tmdb });
            Subject.Merged(MetadataId).Should().NotContain(e => e.Source == EditionCatalogSource.Imdb);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogWikidataEnabled).Returns(false);

            Subject.Merged(MetadataId).Select(e => e.Source).Should().Equal(EditionCatalogSource.Tmdb);
        }

        [Test]
        public void merged_should_keep_the_same_name_when_the_runtimes_are_further_apart_than_the_tolerance()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Imdb, 60, 1), Entry("Extended", 228, EditionCatalogSource.Wikidata, 70, 2));

            Subject.Merged(MetadataId).Select(e => e.RuntimeMinutes).Should().Equal(208, 228);
        }

        [Test]
        public void merged_should_honour_the_configured_tolerance_boundary()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Imdb, 60, 1), Entry("Extended", 211, EditionCatalogSource.Wikidata, 70, 2));

            Subject.Merged(MetadataId).Should().ContainSingle();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionRuntimeToleranceMinutes).Returns(2);

            Subject.Merged(MetadataId).Should().HaveCount(2);
        }

        [Test]
        public void merged_should_not_change_the_stored_entries()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Library, 90, 1), Entry("Extended", 208, EditionCatalogSource.Imdb, 60, 2, "Blu Ray"));

            Subject.Merged(MetadataId);

            _stored[0].Attributes.Should().BeEmpty();
        }

        [Test]
        public void merged_should_be_empty_for_an_unknown_title()
        {
            Subject.Merged(99).Should().BeEmpty();
        }

        [Test]
        public void get_for_movie_should_use_the_metadata_id()
        {
            GivenStored(Entry("Theatrical", 178, EditionCatalogSource.Imdb, 80, 1));

            Subject.GetForMovie(new Movie { MovieMetadataId = MetadataId }).Should().ContainSingle();
            Subject.GetForMovie(null).Should().BeEmpty();
        }

        [Test]
        public void upsert_should_insert_a_new_entry_with_an_update_time()
        {
            var entry = Entry("Extended", 228, EditionCatalogSource.Library, 90);
            entry.Updated = default;

            Subject.Upsert(entry);

            _stored.Should().ContainSingle();
            _stored[0].Updated.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void upsert_should_update_the_entry_with_the_same_normalised_name_and_source()
        {
            GivenStored(Entry("Director's Cut", 190, EditionCatalogSource.User, 100, 5));

            var result = Subject.Upsert(Entry("directors cut", 192, EditionCatalogSource.User, 100));

            result.Id.Should().Be(5);
            result.RuntimeMinutes.Should().Be(192);
            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.Insert(It.IsAny<EditionCatalogEntry>()), Times.Never());
            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.Update(It.Is<EditionCatalogEntry>(e => e.Id == 5)), Times.Once());
        }

        [Test]
        public void upsert_should_insert_when_only_the_source_differs()
        {
            GivenStored(Entry("Extended", 208, EditionCatalogSource.Imdb, 60, 1));

            Subject.Upsert(Entry("Extended", 208, EditionCatalogSource.Library, 90));

            _stored.Should().HaveCount(2);
        }

        [TestCase(0, "Extended", 120)]
        [TestCase(MetadataId, "", 120)]
        [TestCase(MetadataId, "Extended", 0)]
        public void upsert_should_refuse_incomplete_entries(int metadataId, string name, int runtime)
        {
            var entry = Entry(name, runtime, EditionCatalogSource.User);
            entry.MovieMetadataId = metadataId;

            Assert.Throws<ArgumentException>(() => Subject.Upsert(entry));
        }

        [Test]
        public void replace_source_should_delete_that_source_and_insert_deduplicated_entries()
        {
            List<EditionCatalogEntry> inserted = null;

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.InsertMany(It.IsAny<IList<EditionCatalogEntry>>()))
                  .Callback((IList<EditionCatalogEntry> list) => inserted = list.ToList());

            Subject.ReplaceSource(MetadataId, EditionCatalogSource.Imdb, new List<EditionCatalogEntry>
            {
                new EditionCatalogEntry { Name = "Theatrical", RuntimeMinutes = 144, Confidence = 80 },
                new EditionCatalogEntry { Name = "Director's Cut", RuntimeMinutes = 190, Confidence = 70 },
                new EditionCatalogEntry { Name = "Director's Cut", RuntimeMinutes = 194, Confidence = 60, Attributes = new List<string> { "director's cut roadshow" } },
                new EditionCatalogEntry { Name = "Broken", RuntimeMinutes = 0, Confidence = 20 }
            });

            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.DeleteBySource(MetadataId, EditionCatalogSource.Imdb), Times.Once());

            inserted.Should().HaveCount(2);
            inserted.Should().OnlyContain(e => e.MovieMetadataId == MetadataId && e.Source == EditionCatalogSource.Imdb && e.Updated != default);
            inserted.Single(e => e.Name == "Director's Cut").RuntimeMinutes.Should().Be(190);
        }

        [Test]
        public void replace_source_with_nothing_should_only_delete()
        {
            Subject.ReplaceSource(MetadataId, EditionCatalogSource.Wikidata, new List<EditionCatalogEntry>());

            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.DeleteBySource(MetadataId, EditionCatalogSource.Wikidata), Times.Once());
            Mocker.GetMock<IEditionCatalogRepository>().Verify(r => r.InsertMany(It.IsAny<IList<EditionCatalogEntry>>()), Times.Never());
        }
    }
}
