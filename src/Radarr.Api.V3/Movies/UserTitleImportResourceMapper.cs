using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies.UserTitles;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Movies
{
    // krzw(user-titles): resource <-> Core request/result mapping plus shape validation. This is
    // the only place that knows about the curated dataset's field names and language default.
    public static class UserTitleImportResourceMapper
    {
        public const int MaxMoviesPerRequest = 5000;
        public const int MaxTitlesPerMovie = 100;
        public const int MaxTitleLength = 500;

        public static List<UserTitleImportRequest> ToImportRequests(this List<UserAlternativeTitleImportResource> resources, string defaultLanguage)
        {
            if (resources == null)
            {
                return new List<UserTitleImportRequest>();
            }

            if (resources.Count > MaxMoviesPerRequest)
            {
                throw new BadRequestException($"At most {MaxMoviesPerRequest} movies per request (got {resources.Count})");
            }

            return resources.Select(r => ToImportRequest(r, defaultLanguage)).ToList();
        }

        public static UserAlternativeTitleImportSummaryResource ToResource(this UserTitleImportResult result)
        {
            return new UserAlternativeTitleImportSummaryResource
            {
                MoviesProcessed = result.MoviesProcessed,
                TitlesAdded = result.TitlesAdded,
                TitlesSkipped = result.TitlesSkipped,
                TitlesGuarded = result.TitlesGuarded,
                TitlesUnknownLanguage = result.TitlesUnknownLanguage,
                TitlesAlreadyPresent = result.TitlesAlreadyPresent,
                MoviesNotFound = result.MoviesNotFound.ToList(),
                MoviesFailed = result.MoviesFailed.ToList()
            };
        }

        private static UserTitleImportRequest ToImportRequest(UserAlternativeTitleImportResource resource, string defaultLanguage)
        {
            var titles = resource.MissingFrenchTitles ?? new List<UserAlternativeTitleImportEntryResource>();

            if (titles.Count > MaxTitlesPerMovie)
            {
                throw new BadRequestException($"At most {MaxTitlesPerMovie} titles per movie (tmdb:{resource.TmdbId} has {titles.Count})");
            }

            var tooLong = titles.FirstOrDefault(t => t.Title != null && t.Title.Length > MaxTitleLength);

            if (tooLong != null)
            {
                throw new BadRequestException($"Title longer than {MaxTitleLength} characters for tmdb:{resource.TmdbId}");
            }

            // A region is stored verbatim in the RegionalLanguage tag ("fr-ca"); anything but a two-letter
            // ISO 3166-1 code would never match a Regional Translation Variants entry.
            var badRegion = titles.FirstOrDefault(t => t.Region.IsNotNullOrWhiteSpace() && !IsRegionCode(t.Region));

            if (badRegion != null)
            {
                throw new BadRequestException($"Region must be a two-letter ISO 3166-1 code (got '{badRegion.Region}' for tmdb:{resource.TmdbId})");
            }

            return new UserTitleImportRequest
            {
                TmdbId = resource.TmdbId,
                ImdbId = resource.ImdbId,
                MovieTitle = resource.MovieTitle,
                Year = resource.Year,
                Titles = titles.Select(t => new UserTitleImportEntry
                {
                    Title = t.Title,
                    Language = t.Language.IsNotNullOrWhiteSpace() ? t.Language : defaultLanguage,
                    Region = t.Region
                }).ToList()
            };
        }

        private static bool IsRegionCode(string region)
        {
            var trimmed = region.Trim();

            return trimmed.Length == 2 && trimmed.All(char.IsLetter);
        }
    }
}
