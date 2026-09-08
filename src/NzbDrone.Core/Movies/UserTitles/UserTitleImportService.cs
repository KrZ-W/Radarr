using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Translations;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(user-titles): the curated-title import pipeline, shared by
    // POST /api/v3/alttitle/user/import and POST /api/v3/translation/user/import.
    // Resolution (tmdb -> imdb), the cross-movie guard, language mapping and the summary live
    // here; the controllers only translate resources. Work is done per movie: a movie that
    // throws is reported in MoviesFailed and the request continues with the next one.
    public interface IUserTitleImportService
    {
        UserTitleImportResult ImportAlternativeTitles(List<UserTitleImportRequest> requests);
        UserTitleImportResult ImportTranslations(List<UserTitleImportRequest> requests);
    }

    public class UserTitleImportService : IUserTitleImportService
    {
        private readonly IMovieService _movieService;
        private readonly IAlternativeTitleService _alternativeTitleService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IUserTitleGuard _guard;
        private readonly Logger _logger;

        public UserTitleImportService(IMovieService movieService,
                                      IAlternativeTitleService alternativeTitleService,
                                      IMovieTranslationService movieTranslationService,
                                      IUserTitleGuard guard,
                                      Logger logger)
        {
            _movieService = movieService;
            _alternativeTitleService = alternativeTitleService;
            _movieTranslationService = movieTranslationService;
            _guard = guard;
            _logger = logger;
        }

        public UserTitleImportResult ImportAlternativeTitles(List<UserTitleImportRequest> requests)
        {
            return Import(requests, (movie, entries, result) =>
            {
                var titles = entries.Select(e => new AlternativeTitle(e.Title, SourceType.User)).ToList();

                return _alternativeTitleService.UpsertUserTitles(titles, movie.MovieMetadata.Value).Count;
            });
        }

        public UserTitleImportResult ImportTranslations(List<UserTitleImportRequest> requests)
        {
            return Import(requests, (movie, entries, result) =>
            {
                var translations = new List<MovieTranslation>();

                foreach (var entry in entries)
                {
                    var translation = UserTranslationFactory.Create(entry.Title, entry.Language, entry.Region);

                    if (translation == null)
                    {
                        _logger.Debug("Skipping user translation '{0}' for {1}: unknown language '{2}'", entry.Title, movie.Title, entry.Language);
                        result.TitlesUnknownLanguage++;
                        continue;
                    }

                    translations.Add(translation);
                }

                return _movieTranslationService.UpsertUserTranslations(translations, movie.MovieMetadata.Value).Count;
            });
        }

        private UserTitleImportResult Import(List<UserTitleImportRequest> requests, Func<Movie, List<UserTitleImportEntry>, UserTitleImportResult, int> upsert)
        {
            var result = new UserTitleImportResult();

            foreach (var request in requests ?? new List<UserTitleImportRequest>())
            {
                var label = Describe(request);

                try
                {
                    var movie = Resolve(request);

                    if (movie == null)
                    {
                        result.MoviesNotFound.Add(label);
                        continue;
                    }

                    var entries = (request.Titles ?? new List<UserTitleImportEntry>())
                        .Where(e => e.Title.IsNotNullOrWhiteSpace())
                        .ToList();

                    var safeTitles = _guard.FilterSafe(movie, entries.Select(e => e.Title).ToList(), out var guarded);

                    foreach (var title in guarded)
                    {
                        _logger.Warn("Skipping user title '{0}' for {1}: it already resolves to a different movie", title, movie.Title);
                    }

                    var safeEntries = entries.Where(e => safeTitles.Contains(e.Title)).ToList();
                    var unknownBefore = result.TitlesUnknownLanguage;
                    var added = upsert(movie, safeEntries, result);
                    var unknown = result.TitlesUnknownLanguage - unknownBefore;

                    result.MoviesProcessed++;
                    result.TitlesGuarded += guarded.Count;
                    result.TitlesAdded += added;
                    result.TitlesAlreadyPresent += safeEntries.Count - unknown - added;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "User title import failed for {0}", label);
                    result.MoviesFailed.Add($"{label}: {ex.Message}");
                }
            }

            return result;
        }

        private Movie Resolve(UserTitleImportRequest request)
        {
            var movie = request.TmdbId > 0 ? _movieService.FindByTmdbId(request.TmdbId) : null;

            if (movie == null && request.ImdbId.IsNotNullOrWhiteSpace())
            {
                movie = _movieService.FindByImdbId(request.ImdbId);
            }

            return movie;
        }

        private static string Describe(UserTitleImportRequest request)
        {
            return $"{request.MovieTitle} ({request.Year}) [tmdb:{request.TmdbId}]";
        }
    }
}
