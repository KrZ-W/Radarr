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

        // Same title, same source and same normalised name = same entry: its runtime, confidence and attributes are updated
        public EditionCatalogEntry Upsert(EditionCatalogEntry entry)
        {
            Validate(entry);

            entry.Name = entry.Name.Trim();

            if (entry.Updated == default)
            {
                entry.Updated = DateTime.UtcNow;
            }

            var normalized = EditionNormalizer.Normalize(entry.Name);
            var existing = _repository.GetByMetadata(entry.MovieMetadataId)
                                      .FirstOrDefault(e => e.Source == entry.Source && EditionNormalizer.Normalize(e.Name) == normalized);

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

        // Replaces everything one source says about a title (provider refresh). Entries with the same normalised name keep
        // the strongest one (confidence, then first listed), since (title, name, source) is unique.
        public void ReplaceSource(int metadataId, EditionCatalogSource source, List<EditionCatalogEntry> entries)
        {
            var now = DateTime.UtcNow;

            var toInsert = (entries ?? new List<EditionCatalogEntry>())
                           .Where(e => e != null && e.Name.IsNotNullOrWhiteSpace() && e.RuntimeMinutes > 0)
                           .Select((e, index) => new { Entry = e, Index = index })
                           .GroupBy(x => EditionNormalizer.Normalize(x.Entry.Name))
                           .Select(g => g.OrderByDescending(x => x.Entry.Confidence).ThenBy(x => x.Index).First().Entry)
                           .Select(e => new EditionCatalogEntry
                           {
                               MovieMetadataId = metadataId,
                               Name = e.Name.Trim(),
                               RuntimeMinutes = e.RuntimeMinutes,
                               Source = source,
                               Confidence = e.Confidence,
                               Attributes = e.Attributes,
                               Updated = e.Updated == default ? now : e.Updated
                           })
                           .ToList();

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
            var tolerance = Math.Max(0, _configService.EditionRuntimeToleranceMinutes);

            return Merge(GetForMetadata(metadataId), tolerance);
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
