using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Movies.UserTitles;
using Radarr.Http;

namespace Radarr.Api.V3.Movies
{
    // krzw(user-titles): bulk import of curated translations; all logic lives in IUserTitleImportService.
    // The "fr" language default exists for the curated FR/QC dataset and is applied here only.
    [V3ApiController("translation")]
    public class UserTranslationController : Controller
    {
        private const string DefaultLanguage = "fr";

        private readonly IUserTitleImportService _userTitleImportService;

        public UserTranslationController(IUserTitleImportService userTitleImportService)
        {
            _userTitleImportService = userTitleImportService;
        }

        [HttpPost("user/import")]
        [Consumes("application/json")]
        public UserAlternativeTitleImportSummaryResource ImportUserTranslations([FromBody] List<UserAlternativeTitleImportResource> resources)
        {
            return _userTitleImportService.ImportTranslations(resources.ToImportRequests(DefaultLanguage)).ToResource();
        }
    }
}
