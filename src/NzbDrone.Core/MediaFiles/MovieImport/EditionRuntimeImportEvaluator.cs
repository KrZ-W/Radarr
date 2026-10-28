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
    }

    public class EditionRuntimeImportResult
    {
        public double FileMinutes { get; set; }

        // Tagged file whose runtime uniquely names another cut: the rejection message
        public string Conflict { get; set; }

        // Untagged default-row file whose runtime uniquely matches exactly one sibling row's expected runtime
        public Movie RuntimeSibling { get; set; }
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
            var result = new EditionRuntimeImportResult { FileMinutes = minutes.Value };
            var tagCut = TagCut(localMovie.Edition, catalog);

            if (tagCut != null)
            {
                result.Conflict = TagConflict(movie, tagCut, minutes.Value, catalog, tolerance);
            }
            else if (movie.IsDefaultEdition && (localMovie.Edition.IsNullOrWhiteSpace() || IsVariantTag(localMovie.Edition)))
            {
                // Untagged, or only a release variant (IMAX, Remastered...): a raw tag the catalog does not know (fan
                // edit...) says the file is something else, so it is neither re-homed nor rejected
                result.RuntimeSibling = RuntimeSibling(movie, siblings, minutes.Value, catalog, tolerance);
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

        private static string TagConflict(Movie movie, string tagCut, double minutes, List<EditionCatalogEntry> catalog, int tolerance)
        {
            var item = new EditionAuditItem();

            // No theatrical references: an identification doubt is the audit's business, not a reason to reject
            EditionAuditClassifier.Classify(item, tagCut, minutes, catalog, new List<int>(), tolerance);

            if (item.Verdict != EditionAuditVerdict.Conflict || item.RuntimeSuggestedEdition.IsNullOrWhiteSpace())
            {
                return null;
            }

            var expected = catalog.Where(e => e.Confidence >= EditionRuntime.MinimumConfidence && e.RuntimeMinutes > 0 && EditionAuditClassifier.SameCut(e.Name, tagCut))
                                  .OrderByDescending(e => EditionCatalogPriority.Rank(e.Source))
                                  .ThenByDescending(e => e.Confidence)
                                  .Select(e => (int?)e.RuntimeMinutes)
                                  .FirstOrDefault();

            if (!expected.HasValue && movie.IsEdition && movie.ExpectedRuntimeMinutes > 0 && EditionAuditClassifier.SameCut(movie.Edition, tagCut))
            {
                expected = movie.ExpectedRuntimeMinutes;
            }

            return string.Format(ConflictMessage,
                                 tagCut,
                                 expected.HasValue ? $" ({expected.Value} min)" : string.Empty,
                                 Round(minutes),
                                 item.RuntimeSuggestedEdition,
                                 item.RuntimeSuggestedMinutes);
        }

        private static Movie RuntimeSibling(Movie defaultRow, List<Movie> siblings, double minutes, List<EditionCatalogEntry> catalog, int tolerance)
        {
            if (defaultRow.ExpectedRuntimeMinutes > 0 && EditionRuntime.Matches(minutes, defaultRow.ExpectedRuntimeMinutes.Value, tolerance))
            {
                return null;
            }

            var match = EditionRuntime.Match(minutes, catalog, tolerance);
            EditionCatalogEntry entry = null;

            if (match.Kind == RuntimeMatchKind.Unique)
            {
                entry = match.Entry;
            }
            else if (match.Kind == RuntimeMatchKind.Ambiguous && match.Candidates.All(c => EditionAuditClassifier.SameCut(c.Name, match.Candidates[0].Name)))
            {
                // Spellings of one cut ("Extended" from IMDb, "Extended Edition" from a row) are not a real ambiguity
                entry = match.Candidates[0];
            }

            if (entry == null || EditionAuditClassifier.IsTheatrical(entry.Name))
            {
                return null;
            }

            var matching = siblings.Where(s => s.IsEdition &&
                                               s.ExpectedRuntimeMinutes > 0 &&
                                               EditionRuntime.Matches(minutes, s.ExpectedRuntimeMinutes.Value, tolerance))
                                   .ToList();

            if (matching.Count != 1)
            {
                return null;
            }

            var row = matching[0];

            return EditionMatcher.RowMatchesEdition(row, entry.Name) || EditionAuditClassifier.SameCut(row.Edition, entry.Name) ? row : null;
        }

        public static int Round(double minutes)
        {
            return (int)Math.Round(minutes, MidpointRounding.AwayFromZero);
        }
    }
}
