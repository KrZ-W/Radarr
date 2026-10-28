// krzw(edition-runtime-catalog): MovieEditionCatalog storage
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionCatalogRepositoryFixture : DbTest<EditionCatalogRepository, EditionCatalogEntry>
    {
        private static EditionCatalogEntry Entry(int metadataId, string name, int runtime, EditionCatalogSource source, params string[] attributes)
        {
            return new EditionCatalogEntry
            {
                MovieMetadataId = metadataId,
                Name = name,
                RuntimeMinutes = runtime,
                Source = source,
                Confidence = 70,
                Attributes = attributes.ToList(),
                Updated = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc)
            };
        }

        [SetUp]
        public void Setup()
        {
            Subject.InsertMany(new List<EditionCatalogEntry>
            {
                Entry(1, "Theatrical", 178, EditionCatalogSource.Imdb),
                Entry(1, "Extended", 228, EditionCatalogSource.Imdb, "Blu Ray Extended Edition"),
                Entry(1, "Extended", 228, EditionCatalogSource.Wikidata),
                Entry(2, "Director's Cut", 190, EditionCatalogSource.Imdb),
                Entry(3, "Default", 113, EditionCatalogSource.Tmdb)
            });
        }

        [Test]
        public void should_round_trip_every_field()
        {
            var stored = Subject.GetByMetadata(1).Single(e => e.Source == EditionCatalogSource.Imdb && e.Name == "Extended");

            stored.RuntimeMinutes.Should().Be(228);
            stored.Confidence.Should().Be(70);
            stored.Attributes.Should().Equal("Blu Ray Extended Edition");
            stored.Updated.Should().Be(new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc));
        }

        [Test]
        public void should_read_an_empty_attribute_list_back_as_empty()
        {
            Subject.GetByMetadata(2).Single().Attributes.Should().NotBeNull().And.BeEmpty();
        }

        [Test]
        public void get_by_metadata_should_only_return_that_title()
        {
            Subject.GetByMetadata(1).Should().HaveCount(3).And.OnlyContain(e => e.MovieMetadataId == 1);
            Subject.GetByMetadata(99).Should().BeEmpty();
        }

        [Test]
        public void get_by_metadata_ids_should_return_the_titles_asked_for()
        {
            Subject.GetByMetadataIds(new[] { 1, 3, 1 }).Select(e => e.MovieMetadataId).Should().BeEquivalentTo(new[] { 1, 1, 1, 3 });
            Subject.GetByMetadataIds(new List<int>()).Should().BeEmpty();
            Subject.GetByMetadataIds(null).Should().BeEmpty();
        }

        [Test]
        public void delete_by_source_should_only_remove_that_source_of_that_title()
        {
            Subject.DeleteBySource(1, EditionCatalogSource.Imdb);

            Subject.GetByMetadata(1).Should().ContainSingle().Which.Source.Should().Be(EditionCatalogSource.Wikidata);
            Subject.GetByMetadata(2).Should().ContainSingle();
        }

        [Test]
        public void should_refuse_a_second_entry_with_the_same_title_name_and_source()
        {
            Action insert = () => Subject.Insert(Entry(1, "Theatrical", 180, EditionCatalogSource.Imdb));

            insert.Should().Throw<Exception>().WithMessage("*UNIQUE*");
        }
    }
}
