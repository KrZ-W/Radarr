// krzw(edition-runtime-catalog): executor of RefreshEditionCatalogCommand (providers -> ReplaceSource per title)
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    // Staleness is read from the TMDb baseline entry: it is written last, for every title (it needs no network), but
    // with an already-stale Updated when an enabled online provider did not answer for the title, so that title is
    // retried on the next refresh while its cached entries are kept. Titles without a TMDb runtime have no marker and
    // are re-asked on every refresh (rare: unreleased titles).
    public class RefreshEditionCatalogService : IExecute<RefreshEditionCatalogCommand>
    {
        public const int StaleAfterDays = 30;
        public const int ChunkSize = 250;

        private readonly IMovieService _movieService;
        private readonly IEditionCatalogService _catalogService;
        private readonly IEditionCatalogRepository _repository;
        private readonly List<IEditionCatalogProvider> _providers;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public RefreshEditionCatalogService(IMovieService movieService,
                                            IEditionCatalogService catalogService,
                                            IEditionCatalogRepository repository,
                                            IEnumerable<IEditionCatalogProvider> providers,
                                            IEventAggregator eventAggregator,
                                            Logger logger)
        {
            _movieService = movieService;
            _catalogService = catalogService;
            _repository = repository;

            // Online providers first (IMDb before Wikidata), the TMDb baseline last since it reads IMDb's entries
            _providers = providers.OrderByDescending(p => EditionCatalogPriority.Rank(p.Source)).ToList();
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void Execute(RefreshEditionCatalogCommand message)
        {
            var movieIds = message.MovieIds ?? new List<int>();
            var movies = movieIds.Any() ? _movieService.GetMovies(movieIds) : _movieService.GetAllMovies();

            var titles = movies.Select(m => m.MovieMetadata?.Value)
                               .Where(m => m != null && m.Id > 0)
                               .GroupBy(m => m.Id)
                               .Select(g => g.First())
                               .ToList();

            var total = titles.Count;

            if (!message.Force)
            {
                var fresh = FreshTitles(titles.Select(m => m.Id));
                titles = titles.Where(m => !fresh.Contains(m.Id)).ToList();
            }

            if (titles.Count == 0)
            {
                _logger.Debug("Edition catalog up to date for {0} titles", total);
                _eventAggregator.PublishEvent(new EditionCatalogRefreshedEvent(0, 0));
                return;
            }

            _logger.ProgressInfo("Refreshing edition catalog of {0} titles", titles.Count);

            var incomplete = 0;

            foreach (var chunk in titles.Chunk(ChunkSize))
            {
                incomplete += RefreshChunk(chunk.ToList());
            }

            if (incomplete > 0)
            {
                _logger.Warn("Edition catalog refreshed for {0} titles; {1} could not be fetched from every source and will be retried", titles.Count, incomplete);
            }
            else
            {
                _logger.Info("Edition catalog refreshed for {0} titles", titles.Count);
            }

            _eventAggregator.PublishEvent(new EditionCatalogRefreshedEvent(titles.Count, incomplete));
        }

        private HashSet<int> FreshTitles(IEnumerable<int> metadataIds)
        {
            var cutoff = DateTime.UtcNow.AddDays(-StaleAfterDays);

            return _repository.GetByMetadataIds(metadataIds)
                              .Where(e => e.Source == EditionCatalogSource.Tmdb && e.Updated >= cutoff)
                              .Select(e => e.MovieMetadataId)
                              .ToHashSet();
        }

        // Returns the number of titles left incomplete
        private int RefreshChunk(List<MovieMetadata> chunk)
        {
            var incomplete = new HashSet<int>();

            foreach (var provider in _providers.Where(p => p.Source != EditionCatalogSource.Tmdb))
            {
                if (!provider.Enabled)
                {
                    continue;
                }

                var result = Fetch(provider, chunk);

                foreach (var title in chunk)
                {
                    if (result.TryGetValue(title.Id, out var entries))
                    {
                        Replace(title, provider.Source, entries);
                    }
                    else
                    {
                        incomplete.Add(title.Id);
                    }
                }
            }

            foreach (var provider in _providers.Where(p => p.Source == EditionCatalogSource.Tmdb && p.Enabled))
            {
                var result = Fetch(provider, chunk);

                var staleStamp = DateTime.UtcNow.AddDays(-StaleAfterDays - 1);

                foreach (var title in chunk)
                {
                    result.TryGetValue(title.Id, out var entries);
                    entries ??= new List<EditionCatalogEntry>();

                    if (incomplete.Contains(title.Id))
                    {
                        entries.ForEach(e => e.Updated = staleStamp);
                    }

                    Replace(title, provider.Source, entries);
                }
            }

            return incomplete.Count;
        }

        private Dictionary<int, List<EditionCatalogEntry>> Fetch(IEditionCatalogProvider provider, List<MovieMetadata> chunk)
        {
            try
            {
                return provider.Fetch(chunk) ?? new Dictionary<int, List<EditionCatalogEntry>>();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Edition catalog provider {0} failed for {1} titles", provider.Source, chunk.Count);
                return new Dictionary<int, List<EditionCatalogEntry>>();
            }
        }

        private void Replace(MovieMetadata title, EditionCatalogSource source, List<EditionCatalogEntry> entries)
        {
            try
            {
                _catalogService.ReplaceSource(title.Id, source, entries ?? new List<EditionCatalogEntry>());
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Could not store {0} edition catalog entries for {1}", source, title.Title);
            }
        }
    }
}
