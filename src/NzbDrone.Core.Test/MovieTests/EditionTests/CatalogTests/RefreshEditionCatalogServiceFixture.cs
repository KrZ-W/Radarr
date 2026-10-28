// krzw(edition-runtime-catalog): catalog refresh executor (staleness, Force, provider order, failure keeps the cache)
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class RefreshEditionCatalogServiceFixture : CoreTest<RefreshEditionCatalogService>
    {
        private List<Movie> _movies;
        private List<EditionCatalogEntry> _stored;
        private List<string> _calls;
        private Mock<IEditionCatalogProvider> _imdb;
        private Mock<IEditionCatalogProvider> _wikidata;
        private Mock<IEditionCatalogProvider> _tmdb;

        [SetUp]
        public void Setup()
        {
            _stored = new List<EditionCatalogEntry>();
            _calls = new List<string>();

            _movies = new List<Movie>
            {
                GivenMovie(1, 101, "tt0114898"),
                GivenMovie(2, 102, "tt0120737"),

                // An edition row of movie 2: same metadata, fetched once
                GivenMovie(3, 102, "tt0120737")
            };

            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(() => _movies);
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetMovies(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _movies.Where(m => ids.Contains(m.Id)).ToList());

            Mocker.GetMock<IEditionCatalogRepository>()
                  .Setup(r => r.GetByMetadataIds(It.IsAny<IEnumerable<int>>()))
                  .Returns((IEnumerable<int> ids) => _stored.Where(e => ids.Contains(e.MovieMetadataId)).ToList());

            Mocker.GetMock<IEditionCatalogService>()
                  .Setup(s => s.ReplaceSource(It.IsAny<int>(), It.IsAny<EditionCatalogSource>(), It.IsAny<List<EditionCatalogEntry>>()))
                  .Callback((int id, EditionCatalogSource source, List<EditionCatalogEntry> entries) => _calls.Add($"{source}:{id}:{entries.Count}"));

            _imdb = GivenProvider(EditionCatalogSource.Imdb);
            _wikidata = GivenProvider(EditionCatalogSource.Wikidata);
            _tmdb = GivenProvider(EditionCatalogSource.Tmdb);

            // Registration order must not matter
            Mocker.SetConstant<IEnumerable<IEditionCatalogProvider>>(new[] { _tmdb.Object, _wikidata.Object, _imdb.Object });
        }

        private static Movie GivenMovie(int id, int metadataId, string imdbId)
        {
            return new Movie
            {
                Id = id,
                MovieMetadataId = metadataId,
                MovieMetadata = new MovieMetadata { Id = metadataId, ImdbId = imdbId, Title = imdbId, Runtime = 120 }
            };
        }

        private Mock<IEditionCatalogProvider> GivenProvider(EditionCatalogSource source)
        {
            var mock = new Mock<IEditionCatalogProvider>();
            mock.SetupGet(p => p.Source).Returns(source);
            mock.SetupGet(p => p.Enabled).Returns(true);
            mock.Setup(p => p.Fetch(It.IsAny<List<MovieMetadata>>()))
                .Returns((List<MovieMetadata> titles) =>
                {
                    _calls.Add($"fetch {source}");
                    return titles.ToDictionary(t => t.Id, t => new List<EditionCatalogEntry>
                    {
                        new EditionCatalogEntry { MovieMetadataId = t.Id, Name = "Theatrical", RuntimeMinutes = 120, Confidence = 80 }
                    });
                });

            return mock;
        }

        private void GivenTmdbMarker(int metadataId, DateTime updated)
        {
            _stored.Add(new EditionCatalogEntry { MovieMetadataId = metadataId, Name = "Default", RuntimeMinutes = 120, Source = EditionCatalogSource.Tmdb, Confidence = 30, Updated = updated });
        }

        [Test]
        public void should_fetch_each_title_once_imdb_first_and_tmdb_last()
        {
            Subject.Execute(new RefreshEditionCatalogCommand());

            _calls.Where(c => c.StartsWith("fetch")).Should().Equal("fetch Imdb", "fetch Wikidata", "fetch Tmdb");
            _imdb.Verify(p => p.Fetch(It.Is<List<MovieMetadata>>(l => l.Select(m => m.Id).SequenceEqual(new[] { 101, 102 }))), Times.Once());

            _calls.Should().Contain(new[] { "Imdb:101:1", "Imdb:102:1", "Wikidata:101:1", "Tmdb:101:1", "Tmdb:102:1" });
            _calls.IndexOf("Tmdb:101:1").Should().BeGreaterThan(_calls.IndexOf("Imdb:101:1"));
        }

        [Test]
        public void should_skip_titles_refreshed_within_30_days()
        {
            GivenTmdbMarker(101, DateTime.UtcNow.AddDays(-29));
            GivenTmdbMarker(102, DateTime.UtcNow.AddDays(-31));

            Subject.Execute(new RefreshEditionCatalogCommand());

            _imdb.Verify(p => p.Fetch(It.Is<List<MovieMetadata>>(l => l.Single().Id == 102)), Times.Once());
            _calls.Should().NotContain(c => c.Contains(":101:"));
        }

        [Test]
        public void should_only_use_the_tmdb_marker_for_staleness()
        {
            _stored.Add(new EditionCatalogEntry { MovieMetadataId = 101, Name = "Theatrical", RuntimeMinutes = 120, Source = EditionCatalogSource.Imdb, Updated = DateTime.UtcNow });

            Subject.Execute(new RefreshEditionCatalogCommand { MovieIds = new List<int> { 1 } });

            _calls.Should().Contain("Imdb:101:1");
        }

        [Test]
        public void should_refresh_fresh_titles_when_forced()
        {
            GivenTmdbMarker(101, DateTime.UtcNow);
            GivenTmdbMarker(102, DateTime.UtcNow);

            Subject.Execute(new RefreshEditionCatalogCommand { Force = true });

            _calls.Should().Contain(new[] { "Imdb:101:1", "Imdb:102:1", "Tmdb:101:1", "Tmdb:102:1" });
        }

        [Test]
        public void should_limit_to_the_given_movies()
        {
            Subject.Execute(new RefreshEditionCatalogCommand { MovieIds = new List<int> { 3 } });

            Mocker.GetMock<IMovieService>().Verify(s => s.GetAllMovies(), Times.Never());
            _calls.Should().Contain("Imdb:102:1");
            _calls.Should().NotContain(c => c.Contains(":101:"));
        }

        [Test]
        public void should_not_call_providers_when_everything_is_fresh()
        {
            GivenTmdbMarker(101, DateTime.UtcNow);
            GivenTmdbMarker(102, DateTime.UtcNow);

            Subject.Execute(new RefreshEditionCatalogCommand());

            _calls.Should().BeEmpty();
            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.Is<EditionCatalogRefreshedEvent>(m => m.Titles == 0)), Times.Once());
        }

        [Test]
        public void should_skip_disabled_providers()
        {
            _wikidata.SetupGet(p => p.Enabled).Returns(false);

            Subject.Execute(new RefreshEditionCatalogCommand());

            _wikidata.Verify(p => p.Fetch(It.IsAny<List<MovieMetadata>>()), Times.Never());
            _calls.Should().NotContain(c => c.StartsWith("Wikidata"));
            _calls.Should().Contain("Tmdb:101:1");
        }

        [Test]
        public void should_keep_cache_and_stay_stale_when_a_provider_did_not_answer_a_title()
        {
            _imdb.Setup(p => p.Fetch(It.IsAny<List<MovieMetadata>>()))
                 .Returns(new Dictionary<int, List<EditionCatalogEntry>> { { 102, new List<EditionCatalogEntry>() } });

            Subject.Execute(new RefreshEditionCatalogCommand());

            // 101: IMDb failed -> no IMDb replace, no TMDb marker, Wikidata still stored
            _calls.Should().NotContain(c => c.StartsWith("Imdb:101") || c.StartsWith("Tmdb:101"));
            _calls.Should().Contain("Wikidata:101:1");

            // 102: answered with nothing -> its IMDb entries are cleared, marker written
            _calls.Should().Contain(new[] { "Imdb:102:0", "Tmdb:102:1" });

            Mocker.GetMock<NzbDrone.Core.Messaging.Events.IEventAggregator>()
                  .Verify(e => e.PublishEvent(It.Is<EditionCatalogRefreshedEvent>(m => m.Titles == 2 && m.Incomplete == 1)), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_survive_a_throwing_provider()
        {
            _imdb.Setup(p => p.Fetch(It.IsAny<List<MovieMetadata>>())).Throws(new InvalidOperationException("boom"));

            Subject.Execute(new RefreshEditionCatalogCommand());

            _calls.Should().Contain("Wikidata:101:1");
            _calls.Should().NotContain(c => c.StartsWith("Tmdb"));

            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_continue_when_storing_one_title_fails()
        {
            Mocker.GetMock<IEditionCatalogService>()
                  .Setup(s => s.ReplaceSource(101, EditionCatalogSource.Imdb, It.IsAny<List<EditionCatalogEntry>>()))
                  .Throws(new InvalidOperationException("db"));

            Subject.Execute(new RefreshEditionCatalogCommand());

            _calls.Should().Contain("Imdb:102:1");

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
