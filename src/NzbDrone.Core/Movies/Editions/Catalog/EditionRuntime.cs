// krzw(edition-runtime-catalog): matches a file runtime against catalog runtimes (direct and PAL 25/24 both ways)
using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public enum RuntimeMatchKind
    {
        None,
        Unique,
        Ambiguous,
        UnknownLonger
    }

    public class RuntimeMatch
    {
        public RuntimeMatch()
        {
            Kind = RuntimeMatchKind.None;
            Candidates = new List<EditionCatalogEntry>();
        }

        public RuntimeMatchKind Kind { get; set; }

        // Set when Kind is Unique
        public EditionCatalogEntry Entry { get; set; }

        // Every entry that matched (one per edition name); for UnknownLonger, the entries the file was compared with
        public List<EditionCatalogEntry> Candidates { get; set; }

        // Unique match reached only through the 25/24 or 24/25 speed factor
        public bool IsPal { get; set; }

        public double FileMinutes { get; set; }

        // The file runtime in the time base of the matched entry (de-PAL'd when IsPal); the file runtime otherwise
        public double MatchedMinutes { get; set; }
    }

    public static class EditionRuntime
    {
        // Entries below this confidence (transfer/market labels, suspect TMDb runtimes) never take part in a match
        public const int MinimumConfidence = 30;

        // A file this much longer than every known cut is reported as an unknown longer cut
        public const int UnknownLongerMarginMinutes = 8;

        private const double PalToFilm = 25.0 / 24.0;
        private const double FilmToPal = 24.0 / 25.0;
        private const double Epsilon = 1e-6;

        public static bool Matches(double fileMinutes, int catalogMinutes, int toleranceMinutes)
        {
            return MatchedMinutes(fileMinutes, catalogMinutes, toleranceMinutes).HasValue;
        }

        public static RuntimeMatch Match(double fileMinutes, IEnumerable<EditionCatalogEntry> entries, int tolerance)
        {
            var result = new RuntimeMatch
            {
                FileMinutes = fileMinutes,
                MatchedMinutes = fileMinutes
            };

            if (fileMinutes <= 0 || entries == null)
            {
                return result;
            }

            var usable = entries.Where(e => e != null && e.RuntimeMinutes > 0 && e.Confidence >= MinimumConfidence)
                                .ToList();

            if (usable.Count == 0)
            {
                return result;
            }

            // One candidate per edition name: the strongest entry (source priority, then confidence, then closest)
            var matched = usable.Select(e => new { Entry = e, Minutes = MatchedMinutes(fileMinutes, e.RuntimeMinutes, tolerance) })
                                .Where(m => m.Minutes.HasValue)
                                .GroupBy(m => EditionNormalizer.Normalize(m.Entry.Name))
                                .Select(g => g.OrderByDescending(m => EditionCatalogPriority.Rank(m.Entry.Source))
                                              .ThenByDescending(m => m.Entry.Confidence)
                                              .ThenBy(m => Math.Abs(m.Minutes.Value - m.Entry.RuntimeMinutes))
                                              .First())
                                .ToList();

            if (matched.Count == 1)
            {
                var match = matched[0];

                result.Kind = RuntimeMatchKind.Unique;
                result.Entry = match.Entry;
                result.Candidates = new List<EditionCatalogEntry> { match.Entry };
                result.MatchedMinutes = match.Minutes.Value;
                result.IsPal = Math.Abs(match.Minutes.Value - fileMinutes) > Epsilon;

                return result;
            }

            if (matched.Count > 1)
            {
                result.Kind = RuntimeMatchKind.Ambiguous;
                result.Candidates = matched.Select(m => m.Entry).OrderBy(e => e.RuntimeMinutes).ToList();

                return result;
            }

            if (usable.All(e => fileMinutes - e.RuntimeMinutes > UnknownLongerMarginMinutes))
            {
                result.Kind = RuntimeMatchKind.UnknownLonger;
                result.Candidates = usable.OrderBy(e => e.RuntimeMinutes).ToList();
            }

            return result;
        }

        // The file runtime expressed in the catalog runtime's time base when it matches (direct first, then PAL), else null
        private static double? MatchedMinutes(double fileMinutes, int catalogMinutes, int toleranceMinutes)
        {
            if (fileMinutes <= 0 || catalogMinutes <= 0)
            {
                return null;
            }

            var tolerance = Math.Max(0, toleranceMinutes) + Epsilon;

            foreach (var candidate in new[] { fileMinutes, fileMinutes * PalToFilm, fileMinutes * FilmToPal })
            {
                if (Math.Abs(candidate - catalogMinutes) <= tolerance)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
