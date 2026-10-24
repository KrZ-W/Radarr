using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.MovieStats;
using NzbDrone.Core.Tags;
using NzbDrone.SignalR;
using Radarr.Api.V3.Movies;
using Radarr.Http;

namespace Radarr.Api.V3.Calendar
{
    [V3ApiController]
    public class CalendarController : MovieControllerWithSignalR
    {
        private readonly IMovieService _moviesService;
        private readonly ITagService _tagService;

        public CalendarController(IBroadcastSignalRMessage signalR,
                            IMovieService movieService,
                            IMovieTranslationService movieTranslationService,
                            IMovieStatisticsService movieStatisticsService,
                            IUpgradableSpecification upgradableSpecification,
                            ICustomFormatCalculationService formatCalculator,
                            ITagService tagService,
                            IMapCoversToLocal coverMapper,
                            IConfigService configService)
            : base(movieService, movieTranslationService, movieStatisticsService, upgradableSpecification, formatCalculator, configService, coverMapper, signalR)
        {
            _moviesService = movieService;
            _tagService = tagService;
        }

        [NonAction]
        public override ActionResult<MovieResource> GetResourceByIdWithErrorHandler(int id)
        {
            throw new NotImplementedException();
        }

        [HttpGet]
        [Produces("application/json")]
        public List<MovieResource> GetCalendar(DateTime? start, DateTime? end, bool unmonitored = false, string tags = "", bool includeEditions = false)  // krzw(multi-edition)
        {
            var startUse = start ?? DateTime.Today;
            var endUse = end ?? DateTime.Today.AddDays(2);
            var movies = _moviesService.GetMoviesBetweenDates(startUse, endUse, unmonitored);
            var parsedTags = new List<int>();
            var results = new List<Movie>();

            if (tags.IsNotNullOrWhiteSpace())
            {
                parsedTags.AddRange(tags.Split(',').Select(_tagService.GetTag).Select(t => t.Id));
            }

            foreach (var movie in movies)
            {
                if (movie == null)
                {
                    continue;
                }

                if (parsedTags.Any() && parsedTags.None(movie.Tags.Contains))
                {
                    continue;
                }

                // krzw(multi-edition) begin: edition rows share their title's dates; hidden unless asked for
                if (!includeEditions && movie.IsEdition)
                {
                    continue;
                }

                // krzw(multi-edition) end
                results.Add(movie);
            }

            var resources = MapToResource(results);

            // krzw(multi-edition) begin: EditionParentId/Editions from the rows already in hand (edition rows of a title fall in the same date range)
            for (var i = 0; i < results.Count; i++)
            {
                resources[i].LinkEditions(results[i], movies);
            }

            // krzw(multi-edition) end

            return resources
                .OrderBy(m => m.InCinemas)
                .ThenBy(m => m.DigitalRelease)
                .ThenBy(m => m.PhysicalRelease)
                .ToList();
        }
    }
}
