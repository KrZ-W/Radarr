using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.History;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    /// <summary>
    /// krzw(grabbed-release-title)
    /// Fills MovieFiles.GrabbedReleaseTitle from the grab history for files that were imported before
    /// the column existed.
    ///
    /// Matching is an oracle first, a heuristic only as a fallback. A downloadFolderImported history row
    /// carries the imported MovieFile id in Data["fileId"], which is an exact link. When that row exists
    /// but has no DownloadId the import was a manual one, and the file is SKIPPED rather than matched by
    /// time: measured against the fileId oracle, pure time proximity mis-attributed 39 of 2,322 files and
    /// 38 of those 39 were exactly this case - a manual import whose nearest neighbour in time is some
    /// other torrent's grab.
    /// </summary>
    public class BackfillGrabbedReleaseTitlesService : IExecute<BackfillGrabbedReleaseTitlesCommand>
    {
        private const int BatchSize = 500;
        private static readonly TimeSpan ImportProximityWindow = TimeSpan.FromHours(6);

        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IHistoryService _historyService;
        private readonly Logger _logger;

        public BackfillGrabbedReleaseTitlesService(IMediaFileService mediaFileService,
                                                   IMovieService movieService,
                                                   IHistoryService historyService,
                                                   Logger logger)
        {
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _historyService = historyService;
            _logger = logger;
        }

        private enum SkipReason
        {
            NoImportEvent,
            NoDownloadId
        }

        public void Execute(BackfillGrabbedReleaseTitlesCommand message)
        {
            var movieIds = _movieService.GetAllMovies().Select(m => m.Id).ToList();
            var files = _mediaFileService.GetFilesByMovies(movieIds);

            // Loaded once, never per file: the history table is far larger than the file table.
            var imports = _historyService.AllByEventType(MovieHistoryEventType.DownloadFolderImported);
            var grabs = _historyService.AllByEventType(MovieHistoryEventType.Grabbed);

            _logger.ProgressInfo("Backfilling grabbed release titles for {0} movie files from {1} import and {2} grab history records", files.Count, imports.Count, grabs.Count);

            var importsByFileId = BuildImportsByFileId(imports);
            var importsByMovieId = imports.Where(h => h.DownloadId.IsNotNullOrWhiteSpace())
                                          .GroupBy(h => h.MovieId)
                                          .ToDictionary(g => g.Key, g => g.ToList());
            var grabsByDownloadId = BuildGrabsByDownloadId(grabs);

            var scanned = 0;
            var set = 0;
            var noImportEvent = 0;
            var noDownloadId = 0;
            var noGrab = 0;
            var unchanged = 0;

            var pending = new List<MovieFile>();

            foreach (var file in files)
            {
                scanned++;

                var downloadId = FindDownloadId(file, importsByFileId, importsByMovieId, out var skipReason);

                if (downloadId == null)
                {
                    if (skipReason == SkipReason.NoDownloadId)
                    {
                        noDownloadId++;
                    }
                    else
                    {
                        noImportEvent++;
                    }

                    continue;
                }

                if (!grabsByDownloadId.TryGetValue(downloadId, out var grab))
                {
                    noGrab++;
                    continue;
                }

                var title = GrabbedReleaseTitleSanitizer.Sanitize(grab.SourceTitle);

                if (title == null)
                {
                    noGrab++;
                    continue;
                }

                if (title.Equals(file.GrabbedReleaseTitle, StringComparison.Ordinal))
                {
                    unchanged++;
                    continue;
                }

                file.GrabbedReleaseTitle = title;
                pending.Add(file);
                set++;

                if (pending.Count >= BatchSize)
                {
                    _mediaFileService.Update(pending);
                    pending.Clear();
                }
            }

            if (pending.Any())
            {
                _mediaFileService.Update(pending);
            }

            _logger.ProgressInfo("Grabbed release title backfill finished. scanned: {0}, set: {1}, no-import-event: {2}, no-download-id: {3}, no-grab: {4}, unchanged: {5}",
                scanned,
                set,
                noImportEvent,
                noDownloadId,
                noGrab,
                unchanged);
        }

        /// <summary>
        /// The exact link: Data["fileId"] on a downloadFolderImported row is the MovieFile id. Keys are
        /// stored camelCase but MovieHistory.Data is case-insensitive, so the lookup is spelling-proof.
        /// The newest row wins when a file was imported more than once.
        /// </summary>
        private static Dictionary<int, MovieHistory> BuildImportsByFileId(List<MovieHistory> imports)
        {
            var byFileId = new Dictionary<int, MovieHistory>();

            foreach (var import in imports)
            {
                if (import.Data == null || !import.Data.TryGetValue("fileId", out var rawFileId) || !int.TryParse(rawFileId, out var fileId) || fileId <= 0)
                {
                    continue;
                }

                if (!byFileId.TryGetValue(fileId, out var existing) || import.Date > existing.Date)
                {
                    byFileId[fileId] = import;
                }
            }

            return byFileId;
        }

        /// <summary>
        /// DownloadId casing is not consistent between clients and indexers, so the index is upper-cased.
        /// The latest grab for a download id is the one that produced the imported file.
        /// </summary>
        private static Dictionary<string, MovieHistory> BuildGrabsByDownloadId(List<MovieHistory> grabs)
        {
            var byDownloadId = new Dictionary<string, MovieHistory>(StringComparer.Ordinal);

            foreach (var grab in grabs)
            {
                if (grab.DownloadId.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var key = grab.DownloadId.ToUpperInvariant();

                if (!byDownloadId.TryGetValue(key, out var existing) || grab.Date > existing.Date)
                {
                    byDownloadId[key] = grab;
                }
            }

            return byDownloadId;
        }

        private static string FindDownloadId(MovieFile file,
                                             Dictionary<int, MovieHistory> importsByFileId,
                                             Dictionary<int, List<MovieHistory>> importsByMovieId,
                                             out SkipReason skipReason)
        {
            skipReason = SkipReason.NoImportEvent;

            if (importsByFileId.TryGetValue(file.Id, out var exactImport))
            {
                // The import that actually produced this file is known. A manual import has no DownloadId
                // and therefore no grab; falling back to time proximity here is what mis-attributes files.
                if (exactImport.DownloadId.IsNullOrWhiteSpace())
                {
                    skipReason = SkipReason.NoDownloadId;
                    return null;
                }

                return exactImport.DownloadId.ToUpperInvariant();
            }

            if (!importsByMovieId.TryGetValue(file.MovieId, out var candidates))
            {
                return null;
            }

            MovieHistory closest = null;
            var closestDistance = TimeSpan.Zero;

            foreach (var candidate in candidates)
            {
                var distance = candidate.Date > file.DateAdded ? candidate.Date - file.DateAdded : file.DateAdded - candidate.Date;

                if (distance > ImportProximityWindow)
                {
                    continue;
                }

                if (closest == null || distance < closestDistance)
                {
                    closest = candidate;
                    closestDistance = distance;
                }
            }

            return closest?.DownloadId.ToUpperInvariant();
        }
    }
}
