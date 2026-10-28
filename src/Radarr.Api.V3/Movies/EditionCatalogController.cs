// krzw(edition-runtime-catalog): edition catalog API (per-movie view, user entries, cross-instance export/import)
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using Radarr.Http;

namespace Radarr.Api.V3.Movies
{
    [V3ApiController("edition/catalog")]
    public class EditionCatalogController : Controller
    {
        private readonly IEditionCatalogService _catalogService;
        private readonly IEditionCatalogManagementService _managementService;
        private readonly IMovieService _movieService;
        private readonly IConfigService _configService;

        public EditionCatalogController(IEditionCatalogService catalogService,
                                        IEditionCatalogManagementService managementService,
                                        IMovieService movieService,
                                        IConfigService configService)
        {
            _catalogService = catalogService;
            _managementService = managementService;
            _movieService = movieService;
            _configService = configService;
        }

        // merged = what runtime matching uses (User > Library > Imdb > Wikidata > Tmdb, same name within tolerance collapsed);
        // entries = every stored row with its source
        [HttpGet]
        [Produces("application/json")]
        public EditionCatalogResource GetCatalog([FromQuery] int? movieId)
        {
            if (!movieId.HasValue || movieId.Value <= 0)
            {
                throw new ValidationException(new List<ValidationFailure> { new ValidationFailure("MovieId", "movieId is required", movieId) });
            }

            var movie = _movieService.GetMovie(movieId.Value);

            return new EditionCatalogResource
            {
                MovieId = movie.Id,
                MovieMetadataId = movie.MovieMetadataId,
                TmdbId = movie.TmdbId,
                ToleranceMinutes = _configService.EditionRuntimeToleranceMinutes,
                Merged = _catalogService.Merged(movie.MovieMetadataId).ToResource(),
                Entries = _catalogService.GetForMetadata(movie.MovieMetadataId)
                                         .OrderBy(e => e.RuntimeMinutes)
                                         .ThenByDescending(e => EditionCatalogPriority.Rank(e.Source))
                                         .ToList()
                                         .ToResource()
            };
        }

        [HttpPost]
        [Consumes("application/json")]
        [Produces("application/json")]
        public EditionCatalogEntryResource AddUserEntry([FromBody] EditionCatalogUserEntryResource resource)
        {
            if (resource == null)
            {
                throw new ValidationException(new List<ValidationFailure> { new ValidationFailure("MovieId", "A body is required", null) });
            }

            return _managementService.AddUserEntry(resource.MovieId, resource.Name, resource.RuntimeMinutes).ToResource();
        }

        [HttpDelete("{id:int}")]
        public object DeleteUserEntry(int id)
        {
            _managementService.DeleteUserEntry(id);

            return new { };
        }

        [HttpGet("export")]
        [Produces("application/json")]
        public List<EditionCatalogExchangeItem> Export()
        {
            return _managementService.Export();
        }

        [HttpPost("import")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public EditionCatalogImportResult Import([FromBody] List<EditionCatalogExchangeItem> items)
        {
            return _managementService.Import(items ?? new List<EditionCatalogExchangeItem>());
        }
    }
}
