// krzw(edition-file-reassign): move an existing file to another edition of the same movie, and audit default-row files that carry an edition
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies.Editions;
using Radarr.Http;

namespace Radarr.Api.V3.Movies
{
    public class MoveFileToEditionResource
    {
        public int MovieFileId { get; set; }
        public int MovieId { get; set; }
    }

    [V3ApiController("edition")]
    public class MovieEditionFileController : Controller
    {
        private readonly IMovieEditionFileService _editionFileService;

        public MovieEditionFileController(IMovieEditionFileService editionFileService)
        {
            _editionFileService = editionFileService;
        }

        [HttpGet("audit")]
        [Produces("application/json")]
        public List<EditionAuditItem> GetAudit([FromQuery] bool includeAll = false)  // krzw(edition-runtime-catalog): includeAll
        {
            return _editionFileService.Audit(includeAll);  // krzw(edition-runtime-catalog)
        }

        // krzw(edition-runtime-catalog) begin: per item, create the edition row if missing, move the file, record a Library runtime
        [HttpPost("audit/apply")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public List<EditionAuditApplyResult> ApplyAudit([FromBody] List<EditionAuditApplyItem> items)
        {
            return _editionFileService.Apply(items ?? new List<EditionAuditApplyItem>());
        }

        // krzw(edition-runtime-catalog) end

        [HttpPost("movefile")]
        [Consumes("application/json")]
        [Produces("application/json")]
        public MoveFileToEditionResource MoveFile([FromBody] MoveFileToEditionResource resource)
        {
            var movieFile = _editionFileService.MoveFileToEdition(resource.MovieFileId, resource.MovieId);

            return new MoveFileToEditionResource { MovieFileId = movieFile.Id, MovieId = movieFile.MovieId };
        }
    }
}
