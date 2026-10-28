// krzw(edition-runtime-catalog): library learning - a file imported into an edition row whose cut the catalog does not know yet records that cut's runtime as a Library entry
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public class EditionCatalogImportLearning : IHandle<MovieFileImportedEvent>
    {
        public const int LearnedConfidence = 60;
        public const string LearnedAttribute = "learned at import";

        private const double PalToFilm = 25.0 / 24.0;

        private readonly IEditionCatalogService _catalogService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public EditionCatalogImportLearning(IEditionCatalogService catalogService, IConfigService configService, Logger logger)
        {
            _catalogService = catalogService;
            _configService = configService;
            _logger = logger;
        }

        public void Handle(MovieFileImportedEvent message)
        {
            var movie = message.MovieInfo?.Movie;

            if (movie == null || !movie.IsEdition || movie.MovieMetadataId <= 0)
            {
                return;
            }

            var mediaInfo = message.ImportedMovie?.MediaInfo ?? message.MovieInfo.MediaInfo;
            var minutes = mediaInfo?.RunTime.TotalMinutes ?? 0;

            if (minutes <= 0)
            {
                return;
            }

            try
            {
                var merged = _catalogService.Merged(movie.MovieMetadataId) ?? new List<EditionCatalogEntry>();

                if (merged.Any(e => EditionAuditClassifier.SameCut(e.Name, movie.Edition) || EditionMatcher.RowMatchesEdition(movie, e.Name)))
                {
                    return;
                }

                var tolerance = Math.Max(0, _configService.EditionRuntimeToleranceMinutes);

                // The row's expected runtime is the user's word for this cut: a file that contradicts it (manual pick,
                // untagged grab) teaches nothing
                if (movie.ExpectedRuntimeMinutes > 0 && !EditionRuntime.Matches(minutes, movie.ExpectedRuntimeMinutes.Value, tolerance))
                {
                    _logger.Debug("Runtime {0:0} min of the imported {1} file of {2} does not match its expected {3} min, not learning it", minutes, movie.Edition, movie.Title, movie.ExpectedRuntimeMinutes);
                    return;
                }

                var runtime = FilmRuntime(minutes, mediaInfo.VideoFps, movie.ExpectedRuntimeMinutes, tolerance);

                if (runtime <= 0)
                {
                    return;
                }

                _catalogService.Upsert(new EditionCatalogEntry
                {
                    MovieMetadataId = movie.MovieMetadataId,
                    Name = movie.Edition,
                    RuntimeMinutes = runtime,
                    Source = EditionCatalogSource.Library,
                    Confidence = LearnedConfidence,
                    Attributes = new List<string> { LearnedAttribute }
                });

                _logger.Debug("Learned the {0} edition of {1} at {2} min from an imported file", movie.Edition, movie.Title, runtime);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not record the runtime of the {0} edition of {1}", movie.Edition, movie.Title);
            }
        }

        // The runtime in film time: the row's expected runtime settles a PAL speed-up when it matches through 25/24 or
        // 24/25; without one, a 25 or 50 fps file is taken as a PAL transfer of a 24 fps film.
        public static int FilmRuntime(double fileMinutes, decimal fps, int? expectedMinutes, int tolerance)
        {
            double minutes;

            if (expectedMinutes > 0 && EditionRuntime.Matches(fileMinutes, expectedMinutes.Value, tolerance))
            {
                minutes = EditionRuntime.Match(fileMinutes,
                                               new[] { new EditionCatalogEntry { Name = "expected", RuntimeMinutes = expectedMinutes.Value, Confidence = 100 } },
                                               tolerance)
                                        .MatchedMinutes;
            }
            else if (IsPalRate(fps))
            {
                minutes = fileMinutes * PalToFilm;
            }
            else
            {
                minutes = fileMinutes;
            }

            return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        }

        private static bool IsPalRate(decimal fps)
        {
            return (fps >= 24.95m && fps <= 25.05m) || (fps >= 49.9m && fps <= 50.1m);
        }
    }
}
