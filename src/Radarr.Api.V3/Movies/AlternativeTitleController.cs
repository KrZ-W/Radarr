using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using Radarr.Http;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Movies
{
    [V3ApiController("alttitle")]
    public class AlternativeTitleController : RestController<AlternativeTitleResource>
    {
        private readonly IAlternativeTitleService _altTitleService;
        private readonly IMovieService _movieService;

        public AlternativeTitleController(IAlternativeTitleService altTitleService, IMovieService movieService)
        {
            _altTitleService = altTitleService;
            _movieService = movieService;
        }

        protected override AlternativeTitleResource GetResourceById(int id)
        {
            return _altTitleService.GetById(id).ToResource();
        }

        [HttpGet]
        public List<AlternativeTitleResource> GetAltTitles(int? movieId, int? movieMetadataId)
        {
            if (movieMetadataId.HasValue)
            {
                return _altTitleService.GetAllTitlesForMovieMetadata(movieMetadataId.Value).ToResource();
            }

            if (movieId.HasValue)
            {
                var movie = _movieService.GetMovie(movieId.Value);
                return _altTitleService.GetAllTitlesForMovieMetadata(movie.MovieMetadataId).ToResource();
            }

            return _altTitleService.GetAllTitles().ToResource();
        }

        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserAlternativeTitleImportSummaryResource ImportUserTitles([FromBody] List<UserAlternativeTitleImportResource> resources)
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

                var titles = candidates
                    .Where(t => UserTitleImportGuard.IsSafeForMovie(_movieService, t.Title, movie))
                    .Select(t => new AlternativeTitle(t.Title, SourceType.User))
                    .ToList();

                var added = _altTitleService.UpsertUserTitles(titles, movie.MovieMetadata.Value);

                summary.MoviesProcessed++;
                summary.TitlesAdded += added.Count;
                summary.TitlesSkipped += candidates.Count - added.Count;
            }

            return summary;
        }
    }
}
