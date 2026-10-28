// krzw(edition-runtime-catalog): import-time runtime rules shared by the re-home in ImportDecisionMaker and EditionRuntimeImportSpecification
using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    public interface IEditionRuntimeImportEvaluator
    {
        // Null when the runtime rules do not apply (no runtime, an existing library file, a row the user picked,
        // or a title with neither sibling rows nor catalog entries)
        EditionRuntimeImportResult Evaluate(LocalMovie localMovie);

        // The sibling edition row an untagged default-row file is re-homed to, or null (setting off, no unique
        // runtime match, or the target row is unmonitored)
        Movie FindRehomeTarget(LocalMovie localMovie);

        // True when this file sits on an edition row that the re-home would have taken it to from the default row
        // (Manual Import reprocess: the listing already shows the re-homed row, the flag is not round-tripped)
        bool IsRehomeTarget(LocalMovie localMovie);
    }

    public class EditionRuntimeImportResult
    {
        public double FileMinutes { get; set; }

        // Tagged file whose runtime uniquely names another cut: the rejection message
        public string Conflict { get; set; }

        // Untagged default-row file whose runtime matches exactly one sibling row's expected runtime (or, for a row
        // without one, the catalog runtime of its name) and not the default row's own
        public Movie RuntimeSibling { get; set; }
    }

    // The evaluation of one file against one row, kept on the LocalMovie so the re-home step and the import
    // specification share the sibling and catalog queries
    public class EditionRuntimeEvaluation
    {
        public Movie Movie { get; set; }

        public EditionRuntimeImportResult Result { get; set; }
    }

    public class EditionRuntimeImportEvaluator : IEditionRuntimeImportEvaluator
    {
        public const string ConflictMessage = "Tagged {0}{1} but runtime {2} min matches {3} ({4} min)";
        public const string SiblingMessage = "Runtime {0} min matches the {1} edition of this movie";

        private static readonly HashSet<string> VariantPresets = new HashSet<string> { "IMAX", "Criterion", "Remastered", "Open Matte", "Restored" };

        private readonly IMovieService _movieService;
        private readonly IEditionCatalogService _catalogService;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public EditionRuntimeImportEvaluator(IMovieService movieService,
                                             IEditionCatalogService catalogService,
                                             IConfigService configService,
                                             Logger logger)
        {
            _movieService = movieService;
            _catalogService = catalogService;
            _configService = configService;
            _logger = logger;
        }

        public EditionRuntimeImportResult Evaluate(LocalMovie localMovie)
        {
            var movie = localMovie?.Movie;

            if (movie == null || localMovie.EditionOverride || localMovie.ExistingFile)
            {
                return null;
            }

            var minutes = FileMinutes(localMovie);

            if (!minutes.HasValue)
            {
                return null;
            }

            if (localMovie.EditionRuntimeEvaluation != null && ReferenceEquals(localMovie.EditionRuntimeEvaluation.Movie, movie))
            {
                return localMovie.EditionRuntimeEvaluation.Result;
            }

            var result = Evaluate(localMovie, movie, minutes.Value);

            localMovie.EditionRuntimeEvaluation = new EditionRuntimeEvaluation { Movie = movie, Result = result };

            return result;
        }

        private EditionRuntimeImportResult Evaluate(LocalMovie localMovie, Movie movie, double minutes)
        {
            var siblings = (_movieService.GetEditionSiblings(movie) ?? new List<Movie>()).Where(s => s != null && s.Id != movie.Id).ToList();
            var raw = movie.MovieMetadataId > 0 ? _catalogService.GetForMetadata(movie.MovieMetadataId) ?? new List<EditionCatalogEntry>() : new List<EditionCatalogEntry>();

            if (siblings.Count == 0 && raw.Count == 0 && !(movie.ExpectedRuntimeMinutes > 0))
            {
                return null;
            }

            var tolerance = Math.Max(0, _configService.EditionRuntimeToleranceMinutes);
            var rows = new List<Movie> { movie };
            rows.AddRange(siblings);

            var catalog = EditionAuditClassifier.PrepareCatalog(raw, rows, tolerance);
            var result = new EditionRuntimeImportResult { FileMinutes = minutes };
            var tagCut = TagCut(localMovie.Edition, catalog);

            if (tagCut != null)
            {
                result.Conflict = TagConflict(movie, tagCut, minutes, catalog, tolerance);
            }
            else if (movie.IsDefaultEdition && (localMovie.Edition.IsNullOrWhiteSpace() || IsVariantTag(localMovie.Edition)))
            {
                // Untagged, or only a release variant (IMAX, Remastered...): a raw tag the catalog does not know (fan
                // edit...) says the file is something else, so it is neither re-homed nor rejected
                result.RuntimeSibling = RuntimeSibling(movie, siblings, minutes, catalog, tolerance);
            }

            return result;
        }

        public Movie FindRehomeTarget(LocalMovie localMovie)
        {
            if (!_configService.EditionRehomeOnImport || localMovie?.Movie == null || !localMovie.Movie.IsDefaultEdition)
            {
                return null;
            }

            var target = Evaluate(localMovie)?.RuntimeSibling;

            if (target == null)
            {
                return null;
            }

            if (!target.Monitored)
            {
                _logger.Debug("Runtime of {0} matches the unmonitored {1} edition row, not re-homing", localMovie.Path, target.Edition);
                return null;
            }

            return target;
        }

        public bool IsRehomeTarget(LocalMovie localMovie)
        {
            var row = localMovie?.Movie;

            if (row == null || !row.IsEdition || localMovie.EditionOverride || localMovie.ExistingFile || !_configService.EditionRehomeOnImport)
            {
                return false;
            }

            if (!FileMinutes(localMovie).HasValue || !(localMovie.Edition.IsNullOrWhiteSpace() || IsVariantTag(localMovie.Edition)))
            {
                return false;
            }

            var defaultRow = row.MovieMetadataId > 0 ? _movieService.GetDefaultEdition(row.MovieMetadataId) : null;

            if (defaultRow == null || defaultRow.Id == row.Id)
            {
                return false;
            }

            localMovie.Movie = defaultRow;

            try
            {
                return FindRehomeTarget(localMovie)?.Id == row.Id;
            }
            finally
            {
                localMovie.Movie = row;
            }
        }

        public static double? FileMinutes(LocalMovie localMovie)
        {
            var minutes = localMovie?.MediaInfo?.RunTime.TotalMinutes;

            return minutes > 0 ? minutes : null;
        }

        // The cut the tag claims: a preset cut or Theatrical, or a raw tag the catalog knows by name. Release variants
        // (IMAX, Remastered...) and tags the catalog does not know count as untagged, as in the audit.
        public static string TagCut(string edition, List<EditionCatalogEntry> catalog)
        {
            if (edition.IsNullOrWhiteSpace())
            {
                return null;
            }

            var preset = EditionAuditClassifier.PresetFor(edition);

            if (preset != null)
            {
                return VariantPresets.Contains(preset.Name) ? null : preset.Name;
            }

            var normalized = EditionNormalizer.Normalize(edition);

            return catalog.Any(e => EditionNormalizer.Normalize(e.Name) == normalized) ? edition.Trim() : null;
        }

        public static bool IsVariantTag(string edition)
        {
            var preset = edition.IsNullOrWhiteSpace() ? null : EditionAuditClassifier.PresetFor(edition);

            return preset != null && VariantPresets.Contains(preset.Name);
        }

        // The tagged row's expected runtime is the authority for its cut: a file that matches it is never a conflict,
        // and the message quotes it. Without one, the catalog's runtime for the tagged cut's name is quoted.
        private static string TagConflict(Movie movie, string tagCut, double minutes, List<EditionCatalogEntry> catalog, int tolerance)
        {
            var rowExpected = TaggedRowExpected(movie, tagCut);

            if (rowExpected.HasValue && EditionRuntime.Matches(minutes, rowExpected.Value, tolerance))
            {
                return null;
            }

            var item = new EditionAuditItem();

            // No theatrical references: an identification doubt is the audit's business, not a reason to reject
            EditionAuditClassifier.Classify(item, tagCut, minutes, catalog, new List<int>(), tolerance);

            if (item.Verdict != EditionAuditVerdict.Conflict || item.RuntimeSuggestedEdition.IsNullOrWhiteSpace())
            {
                return null;
            }

            var expected = rowExpected ??
                           catalog.Where(e => e.Confidence >= EditionRuntime.MinimumConfidence && e.RuntimeMinutes > 0 && EditionAuditClassifier.SameCut(e.Name, tagCut))
                                  .OrderByDescending(e => EditionCatalogPriority.Rank(e.Source))
                                  .ThenByDescending(e => e.Confidence)
                                  .Select(e => (int?)e.RuntimeMinutes)
                                  .FirstOrDefault();

            return string.Format(ConflictMessage,
                                 tagCut,
                                 expected.HasValue ? $" ({expected.Value} min)" : string.Empty,
                                 Round(minutes),
                                 item.RuntimeSuggestedEdition,
                                 item.RuntimeSuggestedMinutes);
        }

        // The expected runtime of the row the tagged file sits on, when that row is the tagged cut's row
        private static int? TaggedRowExpected(Movie movie, string tagCut)
        {
            if (!(movie.ExpectedRuntimeMinutes > 0))
            {
                return null;
            }

            var isTagRow = movie.IsEdition
                ? EditionMatcher.RowMatchesEdition(movie, tagCut) || EditionAuditClassifier.SameCut(movie.Edition, tagCut)
                : EditionAuditClassifier.IsTheatrical(tagCut);

            return isTagRow ? movie.ExpectedRuntimeMinutes : null;
        }

        // Re-home target of an untagged default-row file: the one sibling edition row whose runtime the file matches,
        // provided it does not match the default row's own runtime. Each row's runtime is its expected runtime, the
        // authority; a row without one falls back to the catalog entries of its name and aliases. The default row's
        // own runtime is its expected runtime, else the catalog's Theatrical / TMDb baseline. Catalog names of other
        // cuts play no part: IMDb's "Director's Cut" 176 does not stop a 176-min file going to a "Ulysses Cut" row
        // that expects 176.
        private static Movie RuntimeSibling(Movie defaultRow, List<Movie> siblings, double minutes, List<EditionCatalogEntry> catalog, int tolerance)
        {
            var usable = catalog.Where(e => e.Confidence >= EditionRuntime.MinimumConfidence && e.RuntimeMinutes > 0).ToList();

            var defaultRuntimes = defaultRow.ExpectedRuntimeMinutes > 0
                ? new List<int> { defaultRow.ExpectedRuntimeMinutes.Value }
                : usable.Where(e => EditionAuditClassifier.IsTheatrical(e.Name)).Select(e => e.RuntimeMinutes).ToList();

            if (defaultRuntimes.Any(r => EditionRuntime.Matches(minutes, r, tolerance)))
            {
                return null;
            }

            var matching = siblings.Where(s => s.IsEdition && RowRuntimes(s, usable).Any(r => EditionRuntime.Matches(minutes, r, tolerance)))
                                   .ToList();

            return matching.Count == 1 ? matching[0] : null;
        }

        private static List<int> RowRuntimes(Movie row, List<EditionCatalogEntry> usable)
        {
            if (row.ExpectedRuntimeMinutes > 0)
            {
                return new List<int> { row.ExpectedRuntimeMinutes.Value };
            }

            return usable.Where(e => EditionMatcher.RowMatchesEdition(row, e.Name) || EditionAuditClassifier.SameCut(row.Edition, e.Name))
                         .Select(e => e.RuntimeMinutes)
                         .ToList();
        }

        public static int Round(double minutes)
        {
            return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        }
    }
}
