using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.UserTitles;  // krzw(user-titles)
using Radarr.Http;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Movies
{
    [V3ApiController("alttitle")]
    public class AlternativeTitleController : RestController<AlternativeTitleResource>
    {
        private readonly IAlternativeTitleService _altTitleService;
        private readonly IMovieService _movieService;
        private readonly IUserTitleImportService _userTitleImportService;  // krzw(user-titles)

        public AlternativeTitleController(IAlternativeTitleService altTitleService, IMovieService movieService, IUserTitleImportService userTitleImportService)
        {
            _altTitleService = altTitleService;
            _movieService = movieService;
            _userTitleImportService = userTitleImportService;
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

        // krzw(user-titles): bulk import of curated alt titles
        // krzw(user-titles): bulk import of curated alt titles; all logic lives in IUserTitleImportService
        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserAlternativeTitleImportSummaryResource ImportUserTitles([FromBody] List<UserAlternativeTitleImportResource> resources)
        {
            return _userTitleImportService.ImportAlternativeTitles(resources.ToImportRequests(defaultLanguage: null)).ToResource();
        }
    }
}
