using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Movies.UserTitles;

namespace NzbDrone.Core.Movies.ImdbTitles
{
    // krzw(imdb-title-provider): turns indexed akas rows into UserTitleImportRequests and hands
    // them to the user-title pipeline, as both alternative titles (parse/import matching) and
    // translations (search). Owns nothing else: guards, upserts, refresh survival and the summary
    // all belong to IUserTitleImportService.
    public interface IImdbTitleSyncService
    {
        ImdbTitleSyncSummary SyncAll();
        ImdbTitleSyncSummary SyncMovie(Movie movie);
        ImdbTitleSyncCandidates BuildCandidates(Movie movie);
    }

    public class ImdbTitleSyncService : IImdbTitleSyncService, IHandleAsync<MovieAddedEvent>, IHandleAsync<MovieUpdatedEvent>
    {
        private readonly IConfigService _configService;
        private readonly IImdbAkasDatabase _database;
        private readonly IMovieService _movieService;
        private readonly IAlternativeTitleService _alternativeTitleService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IUserTitleImportService _importService;
        private readonly Logger _logger;

        public ImdbTitleSyncService(IConfigService configService,
                                    IImdbAkasDatabase database,
                                    IMovieService movieService,
                                    IAlternativeTitleService alternativeTitleService,
                                    IMovieTranslationService movieTranslationService,
                                    IUserTitleImportService importService,
                                    Logger logger)
        {
            _configService = configService;
            _database = database;
            _movieService = movieService;
            _alternativeTitleService = alternativeTitleService;
            _movieTranslationService = movieTranslationService;
            _importService = importService;
            _logger = logger;
        }

        public ImdbTitleSyncSummary SyncAll()
        {
            if (!IsReady(logWhenDisabled: true))
            {
                return new ImdbTitleSyncSummary();
            }

            var movies = _movieService.GetAllMovies().Where(m => m.ImdbId.IsNotNullOrWhiteSpace()).ToList();

            _logger.Debug("Building IMDb title candidates for {0} movies with an IMDb id", movies.Count);

            var candidates = movies.Select(BuildCandidates).ToList();

            return Import(candidates);
        }

        public ImdbTitleSyncSummary SyncMovie(Movie movie)
        {
            if (movie == null || movie.ImdbId.IsNullOrWhiteSpace() || !IsReady(logWhenDisabled: false))
            {
                return new ImdbTitleSyncSummary();
            }

            return Import(new List<ImdbTitleSyncCandidates> { BuildCandidates(movie) });
        }

        // The per-movie decision: which indexed titles the movie does not have yet, as one request
        // for the alt-title importer and one for the translation importer (a title present only as
        // an alt title still needs its translation row, and vice versa).
        public ImdbTitleSyncCandidates BuildCandidates(Movie movie)
        {
            var result = new ImdbTitleSyncCandidates { Movie = movie };
            var rows = _database.GetTitles(movie.ImdbId);

            if (rows.Count == 0)
            {
                return result;
            }

            var filter = ImdbAkasFilter.FromConfig(_configService);
            var variants = ParseVariants(_configService.RegionalTranslationVariants);
            var metadataId = movie.MovieMetadata.Value.Id;

            var own = new HashSet<string>(StringComparer.Ordinal)
            {
                ImdbTitleNormalizer.Normalize(movie.Title),
                ImdbTitleNormalizer.Normalize(movie.MovieMetadata.Value.OriginalTitle)
            };
            own.Remove(string.Empty);

            var knownAlternative = new HashSet<string>(own, StringComparer.Ordinal);
            knownAlternative.UnionWith(_alternativeTitleService.GetAllTitlesForMovieMetadata(metadataId).Select(t => ImdbTitleNormalizer.Normalize(t.Title)));

            var knownTranslation = new HashSet<string>(own, StringComparer.Ordinal);
            knownTranslation.UnionWith(_movieTranslationService.GetAllTranslationsForMovieMetadata(metadataId).Select(t => ImdbTitleNormalizer.Normalize(t.Title)));

            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var row in rows)
            {
                var key = ImdbTitleNormalizer.Normalize(row.Title);

                if (key.Length == 0 || !seen.Add(key))
                {
                    continue;
                }

                var language = row.Language ?? filter.DefaultLanguage;
                var region = KeepRegion(language, row.Region, variants) ? row.Region : null;

                if (!knownAlternative.Contains(key))
                {
                    result.AlternativeTitles.Add(new UserTitleImportEntry { Title = row.Title, Language = language, Region = region });
                }

                if (!knownTranslation.Contains(key))
                {
                    result.Translations.Add(new UserTitleImportEntry { Title = row.Title, Language = language, Region = region });
                }
            }

            return result;
        }

        public void HandleAsync(MovieAddedEvent message)
        {
            Sync(message.Movie, "added");
        }

        public void HandleAsync(MovieUpdatedEvent message)
        {
            Sync(message.Movie, "refreshed");
        }

        // A region is only worth storing when it makes the translation searchable, i.e. the tag is
        // in Regional Translation Variants (fr-CA -> fr-ca). Otherwise the row becomes a bare
        // language that is always searched (a France row as "fr", not "fr-fr").
        public static bool KeepRegion(string language, string region, HashSet<string> variants)
        {
            if (region.IsNullOrWhiteSpace() || language.IsNullOrWhiteSpace())
            {
                return false;
            }

            return variants.Contains(RegionalLanguageTag.Build(language, region));
        }

        public static HashSet<string> ParseVariants(string configured)
        {
            return new HashSet<string>(ImdbAkasFilter.ParseList(configured).Select(RegionalLanguageTag.Normalize), StringComparer.Ordinal);
        }

        private void Sync(Movie movie, string reason)
        {
            try
            {
                var summary = SyncMovie(movie);

                if (summary.AlternativeTitles != null || summary.Translations != null)
                {
                    _logger.Debug("IMDb titles for {0} ({1}): {2}", movie, reason, summary);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "IMDb title sync failed for {0}", movie);
            }
        }

        private bool IsReady(bool logWhenDisabled)
        {
            if (!_configService.ImdbTitleProviderEnabled)
            {
                if (logWhenDisabled)
                {
                    _logger.Debug("IMDb Title Provider is disabled");
                }

                return false;
            }

            if (!_database.Exists)
            {
                _logger.Debug("IMDb title index {0} does not exist yet; run the ImdbTitleDatasetRefresh task", _database.Path);
                return false;
            }

            return true;
        }

        private ImdbTitleSyncSummary Import(List<ImdbTitleSyncCandidates> candidates)
        {
            var summary = new ImdbTitleSyncSummary { MoviesChecked = candidates.Count };

            var alternative = candidates.Where(c => c.AlternativeTitles.Count > 0).Select(c => c.ToRequest(c.AlternativeTitles)).ToList();
            var translations = candidates.Where(c => c.Translations.Count > 0).Select(c => c.ToRequest(c.Translations)).ToList();

            summary.MoviesWithCandidates = candidates.Count(c => c.AlternativeTitles.Count > 0 || c.Translations.Count > 0);

            if (alternative.Count > 0)
            {
                summary.AlternativeTitles = _importService.ImportAlternativeTitles(alternative);
            }

            if (translations.Count > 0)
            {
                summary.Translations = _importService.ImportTranslations(translations);
            }

            return summary;
        }
    }

    public class ImdbTitleSyncCandidates
    {
        public Movie Movie { get; set; }
        public List<UserTitleImportEntry> AlternativeTitles { get; } = new List<UserTitleImportEntry>();
        public List<UserTitleImportEntry> Translations { get; } = new List<UserTitleImportEntry>();

        public UserTitleImportRequest ToRequest(List<UserTitleImportEntry> entries)
        {
            return new UserTitleImportRequest
            {
                TmdbId = Movie.TmdbId,
                ImdbId = Movie.ImdbId,
                MovieTitle = Movie.Title,
                Year = Movie.Year,
                Titles = entries.ToList()
            };
        }
    }

    public class ImdbTitleSyncSummary
    {
        public int MoviesChecked { get; set; }
        public int MoviesWithCandidates { get; set; }
        public UserTitleImportResult AlternativeTitles { get; set; }
        public UserTitleImportResult Translations { get; set; }

        public override string ToString()
        {
            return $"{MoviesChecked} movies checked, {MoviesWithCandidates} with candidates; " +
                   $"alt titles added {AlternativeTitles?.TitlesAdded ?? 0} / skipped {AlternativeTitles?.TitlesSkipped ?? 0}; " +
                   $"translations added {Translations?.TitlesAdded ?? 0} / skipped {Translations?.TitlesSkipped ?? 0}";
        }
    }
}
