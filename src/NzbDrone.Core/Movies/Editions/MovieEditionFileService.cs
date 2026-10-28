// krzw(edition-file-reassign): moves an existing movie file between the rows of one title and audits which default-row files are really another cut
using System;  // krzw(edition-runtime-catalog)
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;  // krzw(edition-runtime-catalog)
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Movies.Editions.Catalog;  // krzw(edition-runtime-catalog)
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Movies.Editions
{
    public interface IMovieEditionFileService
    {
        MovieFile MoveFileToEdition(int movieFileId, int targetMovieId);
        List<EditionAuditItem> Audit(bool includeAll = false);  // krzw(edition-runtime-catalog): includeAll
        List<EditionAuditApplyResult> Apply(List<EditionAuditApplyItem> items);  // krzw(edition-runtime-catalog)
    }

    public enum EditionAuditKind
    {
        Theatrical,
        Cut,
        Variant,
        Unrecognised,
        Untagged  // krzw(edition-runtime-catalog): audit v2 also covers untagged files
    }

    public class EditionAuditItem
    {
        public int MovieId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int MovieFileId { get; set; }
        public string RelativePath { get; set; }
        public string FileEdition { get; set; }
        public string SuggestedEdition { get; set; }
        public EditionAuditKind Kind { get; set; }
        public bool Ambiguous { get; set; }
        public int? EditionRowId { get; set; }
        public bool EditionRowHasFile { get; set; }

        // krzw(edition-runtime-catalog) begin: audit v2, the file runtime against the title's edition catalog
        public double? FileRuntimeMinutes { get; set; }
        public bool IsPal { get; set; }
        public RuntimeMatchKind RuntimeMatchKind { get; set; }
        public string RuntimeSuggestedEdition { get; set; }
        public int? RuntimeSuggestedMinutes { get; set; }
        public EditionCatalogSource? CatalogSource { get; set; }
        public EditionAuditVerdict Verdict { get; set; }
        public int? ExpectedRuntimeMinutes { get; set; }

        // krzw(edition-runtime-catalog) end
    }

    public class MovieEditionFileService : IMovieEditionFileService
    {
        private const int LibraryConfidence = 90;  // krzw(edition-runtime-catalog)

        private static readonly Regex PlexEditionTagRegex = new Regex(@"\{edition-(?<edition>[^}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Release variants of the same cut: worth knowing about, but not a separate edition by default.
        // krzw(edition-runtime-catalog): what needs the user first
        private static readonly EditionAuditVerdict[] VerdictOrder =
        {
            EditionAuditVerdict.Conflict, EditionAuditVerdict.CheckIdentification, EditionAuditVerdict.RuntimeOnly, EditionAuditVerdict.TagOnly,
            EditionAuditVerdict.Pal, EditionAuditVerdict.Consistent, EditionAuditVerdict.Unknown
        };

        private static readonly HashSet<string> VariantPresets = new HashSet<string> { "IMAX", "Criterion", "Remastered", "Open Matte", "Restored" };

        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISubtitleFileService _subtitleFileService;
        private readonly IMetadataFileService _metadataFileService;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly INamingConfigService _namingConfigService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IEditionCatalogRepository _catalogRepository;  // krzw(edition-runtime-catalog)
        private readonly IEditionCatalogService _catalogService;  // krzw(edition-runtime-catalog)
        private readonly IAddMovieEditionService _addMovieEditionService;  // krzw(edition-runtime-catalog)
        private readonly IConfigService _configService;  // krzw(edition-runtime-catalog)
        private readonly IRenameMovieFileService _renameMovieFileService;  // krzw(edition-runtime-catalog)
        private readonly Logger _logger;

        public MovieEditionFileService(IMovieService movieService,
                                       IMediaFileService mediaFileService,
                                       ISubtitleFileService subtitleFileService,
                                       IMetadataFileService metadataFileService,
                                       IOtherExtraFileService otherExtraFileService,
                                       INamingConfigService namingConfigService,
                                       IManageCommandQueue commandQueueManager,
                                       IEditionCatalogRepository catalogRepository,  // krzw(edition-runtime-catalog)
                                       IEditionCatalogService catalogService,  // krzw(edition-runtime-catalog)
                                       IAddMovieEditionService addMovieEditionService,  // krzw(edition-runtime-catalog)
                                       IConfigService configService,  // krzw(edition-runtime-catalog)
                                       IRenameMovieFileService renameMovieFileService,  // krzw(edition-runtime-catalog)
                                       Logger logger)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _subtitleFileService = subtitleFileService;
            _metadataFileService = metadataFileService;
            _otherExtraFileService = otherExtraFileService;
            _namingConfigService = namingConfigService;
            _commandQueueManager = commandQueueManager;
            _catalogRepository = catalogRepository;  // krzw(edition-runtime-catalog)
            _catalogService = catalogService;  // krzw(edition-runtime-catalog)
            _addMovieEditionService = addMovieEditionService;  // krzw(edition-runtime-catalog)
            _configService = configService;  // krzw(edition-runtime-catalog)
            _renameMovieFileService = renameMovieFileService;  // krzw(edition-runtime-catalog)
            _logger = logger;
        }

        public MovieFile MoveFileToEdition(int movieFileId, int targetMovieId)
        {
            return MoveFileToEdition(movieFileId, targetMovieId, false);  // krzw(edition-runtime-catalog)
        }

        // krzw(edition-runtime-catalog): renameNow renames before returning instead of queueing (audit apply)
        private MovieFile MoveFileToEdition(int movieFileId, int targetMovieId, bool renameNow)
        {
            var movieFile = _mediaFileService.GetMovie(movieFileId);
            var source = _movieService.GetMovie(movieFile.MovieId);
            var target = _movieService.GetMovie(targetMovieId);

            if (source.Id == target.Id)
            {
                throw Failure("MovieId", "The file already belongs to this edition", targetMovieId);
            }

            if (source.MovieMetadataId != target.MovieMetadataId)
            {
                throw Failure("MovieId", "A file can only be moved between editions of the same movie", targetMovieId);
            }

            if (target.MovieFileId > 0)
            {
                throw Failure("MovieId", $"The {DisplayName(target)} edition already has a file; delete it or move it to another edition first", targetMovieId);
            }

            _logger.Info("Moving file {0} from the {1} edition to the {2} edition of {3}", movieFile.RelativePath, DisplayName(source), DisplayName(target), target.Title);

            movieFile.MovieId = target.Id;
            _mediaFileService.Update(movieFile);

            MoveExtras(_subtitleFileService, movieFile.Id, target.Id);
            MoveExtras(_metadataFileService, movieFile.Id, target.Id);
            MoveExtras(_otherExtraFileService, movieFile.Id, target.Id);

            if (source.MovieFileId == movieFile.Id)
            {
                source.MovieFileId = 0;
                _movieService.UpdateMovie(source);
            }

            target.MovieFileId = movieFile.Id;
            _movieService.UpdateMovie(target);

            // The file name now has to carry the target row's {Edition Name}; the rename also renames its extras.
            // krzw(edition-runtime-catalog): the audit apply renames now: a queued rename raced the scan of the edition
            // row it had just created (MovieAddedEvent -> RefreshMovie -> DiskScan of the shared folder)
            if (_namingConfigService.GetConfig().RenameMovies)
            {
                if (renameNow)
                {
                    _renameMovieFileService.RenameMovieFilesNow(target.Id, new List<int> { movieFile.Id });
                }
                else
                {
                    _commandQueueManager.Push(new RenameFilesCommand(target.Id, new List<int> { movieFile.Id }));
                }
            }

            return movieFile;
        }

        // krzw(edition-runtime-catalog) begin: audit v2 covers every default-row file and adds a runtime verdict
        public List<EditionAuditItem> Audit(bool includeAll = false)
        {
            var all = _movieService.GetAllMovies();
            var byMetadata = all.GroupBy(m => m.MovieMetadataId).ToDictionary(g => g.Key, g => g.ToList());
            var result = new List<EditionAuditItem>();
            var tolerance = Math.Max(0, _configService.EditionRuntimeToleranceMinutes);

            var withFiles = all.Where(m => m.IsDefaultEdition && m.MovieFileId > 0 && m.MovieFile != null).ToList();
            var catalogs = EditionCatalogService.Active(_catalogRepository.GetByMetadataIds(withFiles.Select(m => m.MovieMetadataId).Distinct().ToList()), _configService)
                                             .GroupBy(e => e.MovieMetadataId)
                                             .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var movie in withFiles)
            {
                var fileEdition = FileEdition(movie.MovieFile);
                var tagged = fileEdition.IsNotNullOrWhiteSpace();
                var rows = byMetadata[movie.MovieMetadataId];
                var raw = catalogs.TryGetValue(movie.MovieMetadataId, out var entries) ? entries : new List<EditionCatalogEntry>();
                var catalog = EditionAuditClassifier.PrepareCatalog(raw, rows, tolerance);

                var matches = tagged ? EditionPresets.All.Where(p => EditionMatcher.RowMatchesEdition(EditionAuditClassifier.PresetRow(p), fileEdition)).ToList() : new List<EditionPreset>();
                var preset = matches.FirstOrDefault();

                var item = new EditionAuditItem
                {
                    MovieId = movie.Id,
                    Title = movie.Title,
                    Year = movie.Year,
                    MovieFileId = movie.MovieFileId,
                    RelativePath = movie.MovieFile.RelativePath,
                    FileEdition = tagged ? fileEdition : null,
                    SuggestedEdition = preset?.Name,
                    Ambiguous = matches.Count > 1,
                    ExpectedRuntimeMinutes = movie.ExpectedRuntimeMinutes,
                    Kind = !tagged ? EditionAuditKind.Untagged
                        : preset == null ? EditionAuditKind.Unrecognised
                        : preset.Name == "Theatrical" ? EditionAuditKind.Theatrical
                        : VariantPresets.Contains(preset.Name) ? EditionAuditKind.Variant
                        : EditionAuditKind.Cut
                };

                var runtime = movie.MovieFile.MediaInfo?.RunTime.TotalMinutes;
                var references = EditionAuditClassifier.TheatricalReferences(raw, MetadataRuntime(movie));

                EditionAuditClassifier.Classify(item, TagCut(item.Kind, preset, fileEdition, catalog), runtime > 0 ? runtime : null, catalog, references, tolerance);

                var siblings = rows.Where(r => r.Id != movie.Id && r.IsEdition).ToList();

                // A runtime that names another cut points at that cut's row; otherwise the tag does (audit v1)
                var claimingRow = item.Verdict == EditionAuditVerdict.Conflict || item.Verdict == EditionAuditVerdict.RuntimeOnly
                    ? (item.RuntimeSuggestedEdition.IsNotNullOrWhiteSpace() ? EditionMatcher.SelectRow(siblings, item.RuntimeSuggestedEdition) : null)
                    : tagged ? EditionMatcher.SelectRow(siblings, fileEdition) : null;

                if (claimingRow != null && claimingRow.IsEdition)
                {
                    item.EditionRowId = claimingRow.Id;
                    item.EditionRowHasFile = claimingRow.MovieFileId > 0;
                }

                // A tagged file on the default row is always worth listing (audit v1); an untagged one only when the runtime says something
                if (includeAll || tagged || (item.Verdict != EditionAuditVerdict.Consistent && item.Verdict != EditionAuditVerdict.Unknown))
                {
                    result.Add(item);
                }
            }

            return result.OrderBy(i => Array.IndexOf(VerdictOrder, i.Verdict)).ThenBy(i => i.Kind).ThenBy(i => i.Title).ToList();
        }

        // Per item: find or create the edition row, move the file there, record the confirmed runtime as a Library entry.
        // Items are independent: one failing never stops the others, and a file is moved at most once per request.
        public List<EditionAuditApplyResult> Apply(List<EditionAuditApplyItem> items)
        {
            var results = new List<EditionAuditApplyResult>();
            var movedFiles = new HashSet<int>();

            foreach (var item in items ?? new List<EditionAuditApplyItem>())
            {
                var result = new EditionAuditApplyResult { MovieId = item?.MovieId ?? 0 };

                try
                {
                    ApplyOne(item, result, movedFiles);
                    result.Ok = true;
                }
                catch (ValidationException ex)
                {
                    result.Error = ex.Errors.Any() ? string.Join("; ", ex.Errors.Select(e => e.ErrorMessage)) : ex.Message;
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Edition audit apply failed for movie {0}", result.MovieId);
                    result.Error = ex.Message;
                }

                results.Add(result);
            }

            return results;
        }

        private void ApplyOne(EditionAuditApplyItem item, EditionAuditApplyResult result, HashSet<int> movedFiles)
        {
            if (item == null)
            {
                throw Failure("MovieId", "Empty apply item", null);
            }

            var source = _movieService.GetMovie(item.MovieId);
            var name = item.EditionName?.Trim();

            if (source.MovieFileId <= 0)
            {
                throw Failure("MovieId", "This movie has no file to move", item.MovieId);
            }

            result.MovieFileId = source.MovieFileId;

            if (movedFiles.Contains(source.MovieFileId))
            {
                throw Failure("MovieId", "This file was already moved by another item of this request", item.MovieId);
            }

            var problem = EditionValidation.NameProblem(name);

            if (problem != null)
            {
                throw Failure("EditionName", problem, name);
            }

            if (item.RuntimeMinutes.HasValue && item.RuntimeMinutes.Value <= 0)
            {
                throw Failure("RuntimeMinutes", "Runtime must be a positive number of minutes", item.RuntimeMinutes);
            }

            var movieFile = _mediaFileService.GetMovie(source.MovieFileId);
            var runtime = item.RuntimeMinutes ?? ConfirmedRuntime(source.MovieMetadataId, name, movieFile);
            var normalized = EditionNormalizer.Normalize(name);
            var target = _movieService.FindAllByTmdbId(source.TmdbId)
                                      .Where(r => r.IsEdition && r.MovieMetadataId == source.MovieMetadataId)
                                      .FirstOrDefault(r => EditionNormalizer.Normalize(r.Edition) == normalized ||
                                                           r.EditionAliases.Any(a => EditionNormalizer.Normalize(a) == normalized));

            if (target != null && target.Id == source.Id)
            {
                throw Failure("EditionName", "The file already belongs to this edition", name);
            }

            if (target != null && target.MovieFileId > 0)
            {
                throw Failure("EditionName", $"The {target.Edition} edition already has a file; delete it or move it to another edition first", name);
            }

            if (target == null)
            {
                var defaultRow = source.IsDefaultEdition ? source : _movieService.FindByTmdbId(source.TmdbId) ?? source;

                target = _addMovieEditionService.AddEdition(new Movie
                {
                    Edition = name,
                    EditionAliases = item.Aliases ?? new List<string>(),
                    ExpectedRuntimeMinutes = runtime,
                    QualityProfileId = defaultRow.QualityProfileId,
                    Monitored = defaultRow.Monitored,
                    MinimumAvailability = defaultRow.MinimumAvailability,
                    Tags = defaultRow.Tags != null ? new HashSet<int>(defaultRow.Tags) : new HashSet<int>(),
                    AddOptions = new AddMovieOptions { SearchForMovie = false, Monitor = MonitorTypes.MovieOnly, AddMethod = AddMovieMethod.Manual }
                },
                source.TmdbId);

                _logger.Info("Edition audit: created the {0} edition of {1}", name, source.Title);
            }
            else if (!target.ExpectedRuntimeMinutes.HasValue && runtime.HasValue)
            {
                target.ExpectedRuntimeMinutes = runtime;
                _movieService.UpdateMovie(target);
            }

            result.EditionMovieId = target.Id;

            MoveFileToEdition(source.MovieFileId, target.Id, true);
            movedFiles.Add(source.MovieFileId);

            if (!runtime.HasValue)
            {
                _logger.Debug("Edition audit: no runtime known for {0}, no catalog entry recorded for the {1} edition", movieFile.RelativePath, name);

                return;
            }

            try
            {
                _catalogService.Upsert(new EditionCatalogEntry
                {
                    MovieMetadataId = source.MovieMetadataId,
                    Name = target.Edition,
                    RuntimeMinutes = runtime.Value,
                    Source = EditionCatalogSource.Library,
                    Confidence = LibraryConfidence,
                    Attributes = new List<string>()
                });
            }
            catch (Exception ex)
            {
                // The file has moved; a missing catalog entry only costs a later suggestion
                _logger.Warn(ex, "Edition audit: could not record the {0} runtime of {1}", name, source.Title);
            }
        }

        // The file's runtime in film time: de-PAL'd when it matches a catalog entry of that cut through 25/24, else as is
        private int? ConfirmedRuntime(int metadataId, string editionName, MovieFile movieFile)
        {
            var minutes = movieFile?.MediaInfo?.RunTime.TotalMinutes ?? 0;

            if (minutes <= 0)
            {
                return null;
            }

            var tolerance = Math.Max(0, _configService.EditionRuntimeToleranceMinutes);
            var sameCut = (_catalogService.Merged(metadataId) ?? new List<EditionCatalogEntry>())
                          .Where(e => e.RuntimeMinutes > 0 && EditionAuditClassifier.SameCut(e.Name, editionName))
                          .ToList();

            // Direct first, then de-PAL (25/24), then 24/25: a cut known at two runtimes must not pull a PAL file onto the wrong one
            foreach (var candidate in new[] { minutes, minutes * 25.0 / 24.0, minutes * 24.0 / 25.0 })
            {
                var entry = sameCut.Where(e => Math.Abs(candidate - e.RuntimeMinutes) <= tolerance + 1e-6)
                                   .OrderBy(e => Math.Abs(candidate - e.RuntimeMinutes))
                                   .FirstOrDefault();

                if (entry != null)
                {
                    return (int)Math.Round(candidate, MidpointRounding.AwayFromZero);
                }
            }

            return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        }

        // The cut the tag claims, for the runtime verdict: a preset cut or Theatrical, or a raw tag the catalog knows by name
        private static string TagCut(EditionAuditKind kind, EditionPreset preset, string fileEdition, List<EditionCatalogEntry> catalog)
        {
            switch (kind)
            {
                case EditionAuditKind.Cut:
                case EditionAuditKind.Theatrical:
                    return preset.Name;
                case EditionAuditKind.Unrecognised:
                    return catalog.Any(e => EditionNormalizer.Normalize(e.Name) == EditionNormalizer.Normalize(fileEdition)) ? fileEdition : null;
                default:
                    return null;
            }
        }

        private static int MetadataRuntime(Movie movie)
        {
            return movie.MovieMetadata?.Value?.Runtime ?? 0;
        }

        // krzw(edition-runtime-catalog) end

        // A Plex {edition-X} tag in the file name wins over the edition parsed from the release name at import.
        public static string FileEdition(MovieFile movieFile)
        {
            var match = PlexEditionTagRegex.Match(movieFile.RelativePath ?? string.Empty);

            return match.Success ? match.Groups["edition"].Value.Trim() : movieFile.Edition;
        }

        private static string DisplayName(Movie movie)
        {
            return movie.IsEdition ? movie.Edition : "default";
        }

        private static void MoveExtras<TExtraFile>(IExtraFileService<TExtraFile> service, int movieFileId, int targetMovieId)
            where TExtraFile : ExtraFile, new()
        {
            var extras = service.GetFilesByMovieFile(movieFileId);

            if (extras.Empty())
            {
                return;
            }

            extras.ForEach(e => e.MovieId = targetMovieId);
            service.Upsert(extras);
        }

        private static ValidationException Failure(string property, string message, object value)
        {
            return new ValidationException(new List<ValidationFailure> { new ValidationFailure(property, message, value) });
        }
    }
}
