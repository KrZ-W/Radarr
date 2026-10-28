// krzw(edition-runtime-catalog): reads, writes and merges the edition catalog of a title
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public interface IEditionCatalogService
    {
        List<EditionCatalogEntry> GetForMovie(Movie movie);
        List<EditionCatalogEntry> GetForMetadata(int metadataId);
        EditionCatalogEntry Upsert(EditionCatalogEntry entry);
        void ReplaceSource(int metadataId, EditionCatalogSource source, List<EditionCatalogEntry> entries);
        List<EditionCatalogEntry> Merged(int metadataId);
    }

    public class EditionCatalogService : IEditionCatalogService
    {
        private readonly IEditionCatalogRepository _repository;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public EditionCatalogService(IEditionCatalogRepository repository, IConfigService configService, Logger logger)
        {
            _repository = repository;
            _configService = configService;
            _logger = logger;
        }

        public List<EditionCatalogEntry> GetForMovie(Movie movie)
        {
            if (movie == null || movie.MovieMetadataId <= 0)
            {
                return new List<EditionCatalogEntry>();
            }

            return GetForMetadata(movie.MovieMetadataId);
        }

        // Entries of a provider switched off in settings are kept in the table (switching it back on restores them
        // without a refresh) but hidden from every reader
        public List<EditionCatalogEntry> GetForMetadata(int metadataId)
        {
            return Active(_repository.GetByMetadata(metadataId), _configService);
        }

        public static List<EditionCatalogEntry> Active(IEnumerable<EditionCatalogEntry> entries, IConfigService configService)
        {
            return (entries ?? Enumerable.Empty<EditionCatalogEntry>())
                   .Where(e => e != null &&
                               (e.Source != EditionCatalogSource.Imdb || configService.EditionCatalogImdbEnabled) &&
                               (e.Source != EditionCatalogSource.Wikidata || configService.EditionCatalogWikidataEnabled))
                   .ToList();
        }

        // Same title, same source, same normalised name and a runtime within the runtime tolerance = same entry: its
        // runtime, confidence and attributes are updated. The same name further apart is another release of that cut
        // (Fellowship Extended 208 on DVD, 228 on Blu-ray) and gets an entry of its own.
        public EditionCatalogEntry Upsert(EditionCatalogEntry entry)
        {
            Validate(entry);

            entry.Name = entry.Name.Trim();

            if (entry.Updated == default)
            {
                entry.Updated = DateTime.UtcNow;
            }

            var existing = FindSame(_repository.GetByMetadata(entry.MovieMetadataId), entry.Source, entry.Name, entry.RuntimeMinutes, Tolerance());

            if (existing == null)
            {
                entry.Id = 0;

                _logger.Debug("Adding edition catalog entry {0} for metadata {1}", entry, entry.MovieMetadataId);

                return _repository.Insert(entry);
            }

            existing.Name = entry.Name;
            existing.RuntimeMinutes = entry.RuntimeMinutes;
            existing.Confidence = entry.Confidence;
            existing.Attributes = entry.Attributes;
            existing.Updated = entry.Updated;

            _logger.Debug("Updating edition catalog entry {0} for metadata {1}", existing, existing.MovieMetadataId);

            return _repository.Update(existing);
        }

        // Replaces everything one source says about a title (provider refresh). Entries with the same normalised name and
        // runtimes within the tolerance keep the strongest one (confidence, then first listed); the same name further
        // apart stays once per runtime, since (title, name, source, runtime) is unique.
        public void ReplaceSource(int metadataId, EditionCatalogSource source, List<EditionCatalogEntry> entries)
        {
            var now = DateTime.UtcNow;
            var tolerance = Tolerance();
            var kept = new List<EditionCatalogEntry>();

            var ordered = (entries ?? new List<EditionCatalogEntry>())
                          .Where(e => e != null && e.Name.IsNotNullOrWhiteSpace() && e.RuntimeMinutes > 0)
                          .Select((e, index) => new { Entry = e, Index = index })
                          .OrderByDescending(x => x.Entry.Confidence)
                          .ThenBy(x => x.Index)
                          .Select(x => x.Entry);

            foreach (var e in ordered)
            {
                if (FindSame(kept, source, e.Name, e.RuntimeMinutes, tolerance) != null)
                {
                    continue;
                }

                kept.Add(new EditionCatalogEntry
                {
                    MovieMetadataId = metadataId,
                    Name = e.Name.Trim(),
                    RuntimeMinutes = e.RuntimeMinutes,
                    Source = source,
                    Confidence = e.Confidence,
                    Attributes = e.Attributes,
                    Updated = e.Updated == default ? now : e.Updated
                });
            }

            var toInsert = kept.OrderBy(e => e.RuntimeMinutes).ToList();

            _repository.DeleteBySource(metadataId, source);

            if (toInsert.Any())
            {
                _repository.InsertMany(toInsert);
            }
        }

        // User > Library > Imdb > Wikidata > Tmdb. An entry whose normalised name equals that of a stronger entry and whose
        // runtime is within the runtime tolerance of it collapses into the stronger one; its source's attributes are kept.
        // Same name, runtimes further apart: both stay (two releases of one cut, e.g. DVD and Blu-ray extended).
        public List<EditionCatalogEntry> Merged(int metadataId)
        {
            return Merge(GetForMetadata(metadataId), Tolerance());
        }

        // The stored entry an incoming one updates: same source, same normalised name, runtime within the tolerance
        // (closest first, then the exact spelling), or null
        public static EditionCatalogEntry FindSame(IEnumerable<EditionCatalogEntry> entries, EditionCatalogSource source, string name, int runtimeMinutes, int toleranceMinutes)
        {
            var normalized = EditionNormalizer.Normalize(name);
            var trimmed = name?.Trim();

            return (entries ?? Enumerable.Empty<EditionCatalogEntry>())
                   .Where(e => e != null &&
                               e.Source == source &&
                               EditionNormalizer.Normalize(e.Name) == normalized &&
                               Math.Abs(e.RuntimeMinutes - runtimeMinutes) <= Math.Max(0, toleranceMinutes))
                   .OrderBy(e => Math.Abs(e.RuntimeMinutes - runtimeMinutes))
                   .ThenBy(e => e.Name == trimmed ? 0 : 1)
                   .FirstOrDefault();
        }

        public static List<EditionCatalogEntry> Merge(IEnumerable<EditionCatalogEntry> entries, int toleranceMinutes)
        {
            var kept = new List<EditionCatalogEntry>();

            var ordered = (entries ?? Enumerable.Empty<EditionCatalogEntry>())
                          .Where(e => e != null && e.Name.IsNotNullOrWhiteSpace())
                          .OrderByDescending(e => EditionCatalogPriority.Rank(e.Source))
                          .ThenByDescending(e => e.Confidence)
                          .ThenByDescending(e => e.Updated)
                          .ThenBy(e => e.Id);

            foreach (var entry in ordered)
            {
                var normalized = EditionNormalizer.Normalize(entry.Name);
                var stronger = kept.FirstOrDefault(k => EditionNormalizer.Normalize(k.Name) == normalized &&
                                                        Math.Abs(k.RuntimeMinutes - entry.RuntimeMinutes) <= toleranceMinutes);

                if (stronger != null)
                {
                    foreach (var attribute in entry.Attributes.Where(a => !stronger.Attributes.Contains(a)))
                    {
                        stronger.Attributes.Add(attribute);
                    }

                    continue;
                }

                kept.Add(Copy(entry));
            }

            return kept.OrderBy(e => e.RuntimeMinutes)
                       .ThenByDescending(e => EditionCatalogPriority.Rank(e.Source))
                       .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                       .ToList();
        }

        private int Tolerance()
        {
            return Math.Max(0, _configService.EditionRuntimeToleranceMinutes);
        }

        private static EditionCatalogEntry Copy(EditionCatalogEntry entry)
        {
            return new EditionCatalogEntry
            {
                Id = entry.Id,
                MovieMetadataId = entry.MovieMetadataId,
                Name = entry.Name,
                RuntimeMinutes = entry.RuntimeMinutes,
                Source = entry.Source,
                Confidence = entry.Confidence,
                Attributes = entry.Attributes.ToList(),
                Updated = entry.Updated
            };
        }

        private static void Validate(EditionCatalogEntry entry)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (entry.MovieMetadataId <= 0)
            {
                throw new ArgumentException("An edition catalog entry needs a MovieMetadataId", nameof(entry));
            }

            if (entry.Name.IsNullOrWhiteSpace())
            {
                throw new ArgumentException("An edition catalog entry needs a name", nameof(entry));
            }

            if (entry.RuntimeMinutes <= 0)
            {
                throw new ArgumentException("An edition catalog entry needs a positive runtime", nameof(entry));
            }
        }
    }
}
