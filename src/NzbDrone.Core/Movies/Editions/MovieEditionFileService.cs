// krzw(edition-file-reassign): moves an existing movie file between the rows of one title and audits which default-row files are really another cut
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Extras.Files;
using NzbDrone.Core.Extras.Metadata.Files;
using NzbDrone.Core.Extras.Others;
using NzbDrone.Core.Extras.Subtitles;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Commands;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Organizer;

namespace NzbDrone.Core.Movies.Editions
{
    public interface IMovieEditionFileService
    {
        MovieFile MoveFileToEdition(int movieFileId, int targetMovieId);
        List<EditionAuditItem> Audit();
    }

    public enum EditionAuditKind
    {
        Theatrical,
        Cut,
        Variant,
        Unrecognised
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
    }

    public class MovieEditionFileService : IMovieEditionFileService
    {
        private static readonly Regex PlexEditionTagRegex = new Regex(@"\{edition-(?<edition>[^}]+)\}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Release variants of the same cut: worth knowing about, but not a separate edition by default.
        private static readonly HashSet<string> VariantPresets = new HashSet<string> { "IMAX", "Criterion", "Remastered", "Open Matte", "Restored" };

        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly ISubtitleFileService _subtitleFileService;
        private readonly IMetadataFileService _metadataFileService;
        private readonly IOtherExtraFileService _otherExtraFileService;
        private readonly INamingConfigService _namingConfigService;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly Logger _logger;

        public MovieEditionFileService(IMovieService movieService,
                                       IMediaFileService mediaFileService,
                                       ISubtitleFileService subtitleFileService,
                                       IMetadataFileService metadataFileService,
                                       IOtherExtraFileService otherExtraFileService,
                                       INamingConfigService namingConfigService,
                                       IManageCommandQueue commandQueueManager,
                                       Logger logger)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _subtitleFileService = subtitleFileService;
            _metadataFileService = metadataFileService;
            _otherExtraFileService = otherExtraFileService;
            _namingConfigService = namingConfigService;
            _commandQueueManager = commandQueueManager;
            _logger = logger;
        }

        public MovieFile MoveFileToEdition(int movieFileId, int targetMovieId)
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
            if (_namingConfigService.GetConfig().RenameMovies)
            {
                _commandQueueManager.Push(new RenameFilesCommand(target.Id, new List<int> { movieFile.Id }));
            }

            return movieFile;
        }

        public List<EditionAuditItem> Audit()
        {
            var all = _movieService.GetAllMovies();
            var byMetadata = all.GroupBy(m => m.MovieMetadataId).ToDictionary(g => g.Key, g => g.ToList());
            var result = new List<EditionAuditItem>();

            foreach (var movie in all.Where(m => m.IsDefaultEdition && m.MovieFileId > 0 && m.MovieFile != null))
            {
                var fileEdition = FileEdition(movie.MovieFile);

                if (fileEdition.IsNullOrWhiteSpace())
                {
                    continue;
                }

                var matches = EditionPresets.All.Where(p => EditionMatcher.RowMatchesEdition(PresetRow(p), fileEdition)).ToList();
                var preset = matches.FirstOrDefault();

                var item = new EditionAuditItem
                {
                    MovieId = movie.Id,
                    Title = movie.Title,
                    Year = movie.Year,
                    MovieFileId = movie.MovieFileId,
                    RelativePath = movie.MovieFile.RelativePath,
                    FileEdition = fileEdition,
                    SuggestedEdition = preset?.Name,
                    Ambiguous = matches.Count > 1,
                    Kind = preset == null ? EditionAuditKind.Unrecognised
                        : preset.Name == "Theatrical" ? EditionAuditKind.Theatrical
                        : VariantPresets.Contains(preset.Name) ? EditionAuditKind.Variant
                        : EditionAuditKind.Cut
                };

                var siblings = byMetadata[movie.MovieMetadataId].Where(r => r.Id != movie.Id && r.IsEdition).ToList();
                var claimingRow = EditionMatcher.SelectRow(siblings, fileEdition);

                if (claimingRow != null && claimingRow.IsEdition)
                {
                    item.EditionRowId = claimingRow.Id;
                    item.EditionRowHasFile = claimingRow.MovieFileId > 0;
                }

                result.Add(item);
            }

            return result.OrderBy(i => i.Kind).ThenBy(i => i.Title).ToList();
        }

        // A Plex {edition-X} tag in the file name wins over the edition parsed from the release name at import.
        public static string FileEdition(MovieFile movieFile)
        {
            var match = PlexEditionTagRegex.Match(movieFile.RelativePath ?? string.Empty);

            return match.Success ? match.Groups["edition"].Value.Trim() : movieFile.Edition;
        }

        private static Movie PresetRow(EditionPreset preset)
        {
            return new Movie { Edition = preset.Name, EditionAliases = preset.Aliases };
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
