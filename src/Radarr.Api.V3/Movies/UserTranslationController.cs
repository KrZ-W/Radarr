using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Languages;
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

                var translations = (resource.MissingFrenchTitles ?? new List<UserAlternativeTitleImportEntryResource>())
                    .Where(t => t.Title.IsNotNullOrWhiteSpace())
                    .Where(t => TitleIsNotAnotherMovies(t.Title, movie))
                    .Select(t => new MovieTranslation
                    {
                        Title = t.Title,
                        CleanTitle = t.Title.CleanMovieTitle(),
                        Language = Language.French,
                        RegionalLanguage = MapRegion(t.Region)
                    })
                    .ToList();

                var added = _movieTranslationService.UpsertUserTranslations(translations, movie.MovieMetadata.Value);

                summary.MoviesProcessed++;
                summary.TitlesAdded += added.Count;
                summary.TitlesSkipped += translations.Count - added.Count;
            }

            return summary;
        }

        // "CA" is the curated dataset's marker for Quebec titles; every other region with a
        // French title (FR, BE, ...) searches under the bare language.
        private static string MapRegion(string region)
        {
            return region?.Trim().ToUpperInvariant() == "CA" ? "fr-CA" : "fr";
        }

        // The parser maps releases to movies by clean title globally across movie titles,
        // alternative titles, and translations; a title owned by any other movie is skipped.
        // FindByTitleCandidates sweeps all three tables (plus roman-numeral variants).
        private bool TitleIsNotAnotherMovies(string title, Movie movie)
        {
            var candidates = _movieService.FindByTitleCandidates(new List<string> { title }, out _);

            return candidates.All(c => c.TmdbId == movie.TmdbId);
        }
    }
}
