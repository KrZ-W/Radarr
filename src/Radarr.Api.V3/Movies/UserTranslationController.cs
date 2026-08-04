using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Parser;
using Radarr.Http;

namespace Radarr.Api.V3.Movies
{
    [V3ApiController("translation")]
    public class UserTranslationController : Controller
    {
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IMovieService _movieService;

        public UserTranslationController(IMovieTranslationService movieTranslationService, IMovieService movieService)
        {
            _movieTranslationService = movieTranslationService;
            _movieService = movieService;
        }

        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserAlternativeTitleImportSummaryResource ImportUserTranslations([FromBody] List<UserAlternativeTitleImportResource> resources)
        {
            var summary = new UserAlternativeTitleImportSummaryResource();

            if (resources == null)
            {
                return summary;
            }

            foreach (var resource in resources)
            {
                var movie = _movieService.FindByTmdbId(resource.TmdbId);

                if (movie == null && resource.ImdbId.IsNotNullOrWhiteSpace())
                {
                    movie = _movieService.FindByImdbId(resource.ImdbId);
                }

                if (movie == null)
                {
                    summary.MoviesNotFound.Add($"{resource.MovieTitle} ({resource.Year}) [tmdb:{resource.TmdbId}]");
                    continue;
                }

                var candidates = (resource.MissingFrenchTitles ?? new List<UserAlternativeTitleImportEntryResource>())
                    .Where(t => t.Title.IsNotNullOrWhiteSpace())
                    .ToList();

                var translations = candidates
                    .Where(t => UserTitleImportGuard.IsSafeForMovie(_movieService, t.Title, movie))
                    .Select(UserTranslationMapper.Map)
                    .Where(t => t != null)
                    .ToList();

                var added = _movieTranslationService.UpsertUserTranslations(translations, movie.MovieMetadata.Value);

                summary.MoviesProcessed++;
                summary.TitlesAdded += added.Count;
                summary.TitlesSkipped += candidates.Count - added.Count;
            }

            return summary;
        }
    }

    public static class UserTranslationMapper
    {
        // Builds a translation from standard identifiers: ISO 639-1 language (defaulting to
        // "fr" for the curated dataset) plus optional ISO 3166-1 region. The stored tag is
        // "{language}" or "{language}-{region}", lowercase — the same shape SkyHook stores
        // for TMDB rows, so search-side region dedupe treats user and TMDB rows alike.
        // Returns null for an unknown language code.
        public static MovieTranslation Map(UserAlternativeTitleImportEntryResource entry)
        {
            var languageInput = (entry.Language.IsNotNullOrWhiteSpace() ? entry.Language : "fr").Trim().ToLowerInvariant();
            var iso = IsoLanguages.Find(languageInput);

            if (iso == null)
            {
                return null;
            }

            // Canonical two-letter code, not the caller's spelling: IsoLanguages also resolves
            // ISO 639-2 ("fra") and full tags ("fr-CA"), but the stored value must match the
            // two-letter lowercase shape SkyHook writes for TMDB rows. Otherwise the Regional
            // Translation Variants filter (configured as "fr-CA") drops the row from search and
            // OnePerRegion counts it as a separate region.
            var languageCode = iso.TwoLetterCode;

            var region = entry.Region?.Trim().ToLowerInvariant();

            // Honour a region carried in the language field when none is given explicitly.
            if (region.IsNullOrWhiteSpace())
            {
                var parts = languageInput.Split('-');
                region = parts.Length > 1 ? parts[1] : null;
            }

            var tag = region.IsNotNullOrWhiteSpace() ? $"{languageCode}-{region}" : languageCode;

            return new MovieTranslation
            {
                Title = entry.Title,
                CleanTitle = entry.Title.CleanMovieTitle(),
                Language = iso.Language,
                RegionalLanguage = tag
            };
        }
    }
}
