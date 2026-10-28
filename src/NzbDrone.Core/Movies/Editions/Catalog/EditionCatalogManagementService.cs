// krzw(edition-runtime-catalog): user entries and the cross-instance export/import of the edition catalog (API backing)
using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public interface IEditionCatalogManagementService
    {
        EditionCatalogEntry AddUserEntry(int movieId, string name, int runtimeMinutes);
        void DeleteUserEntry(int id);
        List<EditionCatalogExchangeItem> Export();
        EditionCatalogImportResult Import(List<EditionCatalogExchangeItem> items);
    }

    // One catalog entry as exchanged between instances; titles are keyed by TMDb id since metadata ids are local
    public class EditionCatalogExchangeItem
    {
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string Name { get; set; }
        public int RuntimeMinutes { get; set; }

        // Library unless User is given; any other value is read as Library
        public EditionCatalogSource? Source { get; set; }
        public int? Confidence { get; set; }
        public List<string> Attributes { get; set; }
    }

    public class EditionCatalogImportResult
    {
        public EditionCatalogImportResult()
        {
            Errors = new List<string>();
        }

        public int Added { get; set; }
        public int Updated { get; set; }
        public int Unchanged { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; }
    }

    public class EditionCatalogManagementService : IEditionCatalogManagementService
    {
        public const int MaxRuntimeMinutes = 1000;
        public const int UserConfidence = 100;
        public const int LibraryConfidence = 90;
        public const int MaxReportedErrors = 50;

        public const string RuntimeInvalidMessage = "Runtime must be between 1 and {0} minutes";
        public const string UserOnlyDeleteMessage = "Only user entries can be deleted; {0} entries are replaced on the next catalog refresh";
        public const string UnknownTmdbIdMessage = "No movie with TMDb id {0} in this library";

        private static readonly EditionCatalogSource[] ExportedSources = { EditionCatalogSource.Library, EditionCatalogSource.User };

        private readonly IEditionCatalogService _catalogService;
        private readonly IEditionCatalogRepository _repository;
        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public EditionCatalogManagementService(IEditionCatalogService catalogService,
                                               IEditionCatalogRepository repository,
                                               IMovieService movieService,
                                               Logger logger)
        {
            _catalogService = catalogService;
            _repository = repository;
            _movieService = movieService;
            _logger = logger;
        }

        public EditionCatalogEntry AddUserEntry(int movieId, string name, int runtimeMinutes)
        {
            var problem = Problem(name, runtimeMinutes);

            if (problem != null)
            {
                throw Failure(problem.Item1, problem.Item2, problem.Item3);
            }

            // ModelNotFoundException (404) for an unknown movie
            var movie = _movieService.GetMovie(movieId);

            var entry = _catalogService.Upsert(new EditionCatalogEntry
            {
                MovieMetadataId = movie.MovieMetadataId,
                Name = name.Trim(),
                RuntimeMinutes = runtimeMinutes,
                Source = EditionCatalogSource.User,
                Confidence = UserConfidence,
                Updated = DateTime.UtcNow
            });

            _logger.Info("Edition catalog: user entry {0} for {1}", entry, movie);

            return entry;
        }

        public void DeleteUserEntry(int id)
        {
            // ModelNotFoundException (404) for an unknown id
            var entry = _repository.Get(id);

            if (entry.Source != EditionCatalogSource.User)
            {
                throw Failure("Id", string.Format(UserOnlyDeleteMessage, entry.Source), id);
            }

            _repository.Delete(id);

            _logger.Info("Edition catalog: deleted user entry {0} of metadata {1}", entry, entry.MovieMetadataId);
        }

        public List<EditionCatalogExchangeItem> Export()
        {
            var entries = _repository.GetBySources(ExportedSources);

            if (entries.Count == 0)
            {
                return new List<EditionCatalogExchangeItem>();
            }

            // Default rows first so the title/year shown is the default row's; entries of titles no longer in the library are left out
            var rows = _movieService.GetAllMovies()
                                    .Where(m => m.MovieMetadata?.Value != null)
                                    .OrderBy(m => m.IsEdition)
                                    .GroupBy(m => m.MovieMetadataId)
                                    .ToDictionary(g => g.Key, g => g.First().MovieMetadata.Value);

            return entries.Where(e => rows.ContainsKey(e.MovieMetadataId))
                          .Select(e =>
                          {
                              var metadata = rows[e.MovieMetadataId];

                              return new EditionCatalogExchangeItem
                              {
                                  TmdbId = metadata.TmdbId,
                                  ImdbId = metadata.ImdbId,
                                  Title = metadata.Title,
                                  Year = metadata.Year,
                                  Name = e.Name,
                                  RuntimeMinutes = e.RuntimeMinutes,
                                  Source = e.Source,
                                  Confidence = e.Confidence,
                                  Attributes = e.Attributes.ToList()
                              };
                          })
                          .OrderBy(i => i.Title, StringComparer.OrdinalIgnoreCase)
                          .ThenBy(i => i.TmdbId)
                          .ThenBy(i => i.RuntimeMinutes)
                          .ToList();
        }

        // Entries become Library (or User when the item says so). An existing entry of the same title, source and name
        // is only replaced by an item of at least its confidence, so an import never weakens what this instance confirmed.
        public EditionCatalogImportResult Import(List<EditionCatalogExchangeItem> items)
        {
            var result = new EditionCatalogImportResult();
            var list = (items ?? new List<EditionCatalogExchangeItem>()).Where(i => i != null).ToList();

            if (list.Count == 0)
            {
                return result;
            }

            var metadataIds = list.Select(i => i.TmdbId)
                                  .Where(id => id > 0)
                                  .Distinct()
                                  .Chunk(500)
                                  .SelectMany(chunk => _movieService.FindAllByTmdbIds(chunk.ToList()))
                                  .Where(m => m.MovieMetadataId > 0)
                                  .GroupBy(m => m.TmdbId)
                                  .ToDictionary(g => g.Key, g => g.First().MovieMetadataId);

            var existing = _repository.GetByMetadataIds(metadataIds.Values)
                                      .GroupBy(e => e.MovieMetadataId)
                                      .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var item in list)
            {
                var label = $"{item.TmdbId} {item.Name}".Trim();
                var problem = Problem(item.Name, item.RuntimeMinutes);

                if (problem != null)
                {
                    Skip(result, $"{label}: {problem.Item2}");
                    continue;
                }

                if (!metadataIds.TryGetValue(item.TmdbId, out var metadataId))
                {
                    Skip(result, $"{label}: {string.Format(UnknownTmdbIdMessage, item.TmdbId)}");
                    continue;
                }

                var source = item.Source == EditionCatalogSource.User ? EditionCatalogSource.User : EditionCatalogSource.Library;
                var confidence = Math.Clamp(item.Confidence ?? (source == EditionCatalogSource.User ? UserConfidence : LibraryConfidence), 0, 100);
                var name = item.Name.Trim();
                var normalized = EditionNormalizer.Normalize(name);

                var stored = existing.TryGetValue(metadataId, out var rows)
                    ? rows.FirstOrDefault(e => e.Source == source && EditionNormalizer.Normalize(e.Name) == normalized)
                    : null;

                if (stored != null && (stored.Confidence > confidence ||
                                       (stored.Confidence == confidence && stored.RuntimeMinutes == item.RuntimeMinutes)))
                {
                    result.Unchanged++;
                    continue;
                }

                try
                {
                    var saved = _catalogService.Upsert(new EditionCatalogEntry
                    {
                        MovieMetadataId = metadataId,
                        Name = name,
                        RuntimeMinutes = item.RuntimeMinutes,
                        Source = source,
                        Confidence = confidence,
                        Attributes = (item.Attributes ?? new List<string>()).Where(a => a.IsNotNullOrWhiteSpace()).Distinct().ToList(),
                        Updated = DateTime.UtcNow
                    });

                    if (stored != null)
                    {
                        result.Updated++;
                        rows.Remove(stored);
                    }
                    else
                    {
                        result.Added++;
                    }

                    if (!existing.ContainsKey(metadataId))
                    {
                        existing[metadataId] = new List<EditionCatalogEntry>();
                    }

                    existing[metadataId].Add(saved);
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Edition catalog import: could not store {0}", label);
                    Skip(result, $"{label}: {ex.Message}");
                }
            }

            _logger.Info("Edition catalog import: {0} added, {1} updated, {2} unchanged, {3} skipped", result.Added, result.Updated, result.Unchanged, result.Skipped);

            return result;
        }

        // (property, message, value) of the first problem, or null
        private static Tuple<string, string, object> Problem(string name, int runtimeMinutes)
        {
            var nameProblem = EditionValidation.NameProblem(name);

            if (nameProblem != null)
            {
                return Tuple.Create<string, string, object>("Name", nameProblem, name);
            }

            if (runtimeMinutes <= 0 || runtimeMinutes > MaxRuntimeMinutes)
            {
                return Tuple.Create<string, string, object>("RuntimeMinutes", string.Format(RuntimeInvalidMessage, MaxRuntimeMinutes), runtimeMinutes);
            }

            return null;
        }

        private static void Skip(EditionCatalogImportResult result, string error)
        {
            result.Skipped++;

            if (result.Errors.Count < MaxReportedErrors)
            {
                result.Errors.Add(error);
            }
        }

        private static ValidationException Failure(string property, string message, object value)
        {
            return new ValidationException(new List<ValidationFailure> { new ValidationFailure(property, message, value) });
        }
    }
}
