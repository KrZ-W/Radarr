// krzw(edition-runtime-catalog): TMDb baseline entry and the suspect rule
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests.ProviderTests
{
    [TestFixture]
    public class TmdbBaselineProviderFixture : CoreTest<TmdbBaselineProvider>
    {
        private List<EditionCatalogEntry> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<EditionCatalogEntry>();

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.GetByMetadataIds(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _stored.Where(e => ids.Contains(e.MovieMetadataId)).ToList());
        }

        private void GivenImdb(int metadataId, string name, int minutes, int confidence = 80)
        {
            _stored.Add(new EditionCatalogEntry { MovieMetadataId = metadataId, Name = name, RuntimeMinutes = minutes, Source = EditionCatalogSource.Imdb, Confidence = confidence });
        }

        [Test]
        public void should_give_a_default_entry_at_confidence_30()
        {
            GivenImdb(1, "Theatrical", 178);

            var entry = Subject.Fetch(new List<MovieMetadata> { new MovieMetadata { Id = 1, Runtime = 179 } })[1].Single();

            entry.Name.Should().Be("Default");
            entry.RuntimeMinutes.Should().Be(179);
            entry.Source.Should().Be(EditionCatalogSource.Tmdb);
            entry.Confidence.Should().Be(30);
            entry.Attributes.Should().BeEmpty();
        }

        [Test]
        public void should_mark_suspect_when_far_from_imdb_theatrical()
        {
            // Seven matched to an 18-minute short
            GivenImdb(1, "Theatrical", 127);

            var entry = Subject.Fetch(new List<MovieMetadata> { new MovieMetadata { Id = 1, Runtime = 18 } })[1].Single();

            entry.Confidence.Should().Be(0);
            entry.Attributes.Should().Equal("suspect");
        }

        [Test]
        public void should_compare_with_theatrical_not_with_other_cuts()
        {
            GivenImdb(1, "Director's Cut", 30, 70);

            var entry = Subject.Fetch(new List<MovieMetadata> { new MovieMetadata { Id = 1, Runtime = 120 } })[1].Single();

            entry.Confidence.Should().Be(30);
        }

        [TestCase(100, 100, false)]
        [TestCase(150, 100, false)]
        [TestCase(151, 100, true)]
        [TestCase(49, 100, true)]
        [TestCase(50, 100, false)]
        [TestCase(18, 0, false)]
        public void should_apply_the_50_percent_rule(int tmdb, int imdb, bool suspect)
        {
            TmdbBaselineProvider.IsSuspect(tmdb, imdb).Should().Be(suspect);
        }

        [Test]
        public void should_answer_with_no_entry_when_runtime_unknown()
        {
            var result = Subject.Fetch(new List<MovieMetadata> { new MovieMetadata { Id = 4, Runtime = 0 } });

            result[4].Should().BeEmpty();
        }
    }
}
