// krzw(edition-runtime-catalog): audit v2 types and the runtime verdict of a default-row file against the title's edition catalog
using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies.Editions.Catalog;

namespace NzbDrone.Core.Movies.Editions
{
    public enum EditionAuditVerdict
    {
        // Tag and runtime agree, or the untagged file's runtime matches the row's own (theatrical) cut
        Consistent,

        // Tagged, but the runtime only matches the theatrical / TMDb baseline and the catalog knows nothing of the tagged cut
        TagOnly,

        // Untagged (or tagged Theatrical) and the runtime uniquely matches another known cut, or is an unknown longer cut
        RuntimeOnly,

        // Tag names one cut, the runtime uniquely matches another
        Conflict,

        // Same cut as the tag / row, at 25 fps (informational)
        Pal,

        // Runtime matches nothing and is more than 50 % away from the TMDb / IMDb theatrical runtime: likely a wrong TMDb match
        CheckIdentification,

        // Nothing to say (no runtime, no catalog, ambiguous)
        Unknown
    }

    public class EditionAuditApplyItem
    {
        public int MovieId { get; set; }
        public string EditionName { get; set; }
        public int? RuntimeMinutes { get; set; }
        public List<string> Aliases { get; set; }
    }

    public class EditionAuditApplyResult
    {
        public int MovieId { get; set; }
        public int? EditionMovieId { get; set; }
        public int? MovieFileId { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
    }

    public static class EditionAuditClassifier
    {
        public const string TheatricalName = "Theatrical";
        public const double IdentificationMismatchRatio = 0.5;
        public const int ExpectedRuntimeConfidence = 90;
        public const string ExpectedRuntimeAttribute = "expected runtime";

        // The catalog the audit compares with: the TMDb "Default" baseline is named Theatrical (so it collapses into
        // IMDb's theatrical runtime within tolerance), sources are merged, and the expected runtime of each row of the
        // title is added as an entry of its own name unless the catalog already has that name within tolerance.
        public static List<EditionCatalogEntry> PrepareCatalog(IEnumerable<EditionCatalogEntry> raw, IEnumerable<Movie> rows, int tolerance)
        {
            var renamed = (raw ?? Enumerable.Empty<EditionCatalogEntry>())
                          .Where(e => e != null)
                          .Select(e => new EditionCatalogEntry
                          {
                              Id = e.Id,
                              MovieMetadataId = e.MovieMetadataId,
                              Name = IsBaseline(e) ? TheatricalName : e.Name,
                              RuntimeMinutes = e.RuntimeMinutes,
                              Source = e.Source,
                              Confidence = e.Confidence,
                              Attributes = e.Attributes.ToList(),
                              Updated = e.Updated
                          });

            var merged = EditionCatalogService.Merge(renamed, tolerance);

            foreach (var row in (rows ?? Enumerable.Empty<Movie>()).Where(r => r != null && r.ExpectedRuntimeMinutes > 0))
            {
                var name = row.IsEdition ? row.Edition : TheatricalName;
                var expected = row.ExpectedRuntimeMinutes.Value;

                if (merged.Any(e => SameCut(e.Name, name) && Math.Abs(e.RuntimeMinutes - expected) <= tolerance))
                {
                    continue;
                }

                merged.Add(new EditionCatalogEntry
                {
                    MovieMetadataId = row.MovieMetadataId,
                    Name = name,
                    RuntimeMinutes = expected,
                    Source = EditionCatalogSource.Library,
                    Confidence = ExpectedRuntimeConfidence,
                    Attributes = new List<string> { ExpectedRuntimeAttribute },
                    Updated = DateTime.UtcNow
                });
            }

            return merged.OrderBy(e => e.RuntimeMinutes).ToList();
        }

        // Theatrical references for the identification check: TMDb (catalog baseline, even when suspect, else the stored
        // metadata runtime) and IMDb's theatrical runtime.
        public static List<int> TheatricalReferences(IEnumerable<EditionCatalogEntry> raw, int metadataRuntime)
        {
            var entries = (raw ?? Enumerable.Empty<EditionCatalogEntry>()).Where(e => e != null && e.RuntimeMinutes > 0).ToList();
            var references = entries.Where(e => e.Source == EditionCatalogSource.Tmdb ||
                                                (e.Source == EditionCatalogSource.Imdb && IsTheatrical(e.Name)))
                                    .Select(e => e.RuntimeMinutes)
                                    .ToList();

            if (!entries.Any(e => e.Source == EditionCatalogSource.Tmdb) && metadataRuntime > 0)
            {
                references.Add(metadataRuntime);
            }

            return references.Distinct().ToList();
        }

        // tagCut: the cut the file's tag names (preset name, or a raw tag the catalog knows), "Theatrical" for a theatrical
        // tag, null when untagged or tagged with a release variant only.
        public static void Classify(EditionAuditItem item, string tagCut, double? fileMinutes, List<EditionCatalogEntry> catalog, List<int> theatricalReferences, int tolerance)
        {
            item.Verdict = EditionAuditVerdict.Unknown;
            item.RuntimeMatchKind = RuntimeMatchKind.None;

            if (!fileMinutes.HasValue || fileMinutes.Value <= 0)
            {
                return;
            }

            var minutes = fileMinutes.Value;
            var tagged = tagCut.IsNotNullOrWhiteSpace() && !IsTheatrical(tagCut);
            var taggedTheatrical = tagCut.IsNotNullOrWhiteSpace() && !tagged;
            var match = EditionRuntime.Match(minutes, catalog, tolerance);

            item.FileRuntimeMinutes = Math.Round(minutes, 1);
            item.RuntimeMatchKind = match.Kind;

            switch (match.Kind)
            {
                case RuntimeMatchKind.Unique:
                {
                    var entry = match.Entry;

                    Suggest(item, entry);
                    item.IsPal = match.IsPal;

                    if (tagged)
                    {
                        if (SameCut(entry.Name, tagCut))
                        {
                            item.Verdict = match.IsPal ? EditionAuditVerdict.Pal : EditionAuditVerdict.Consistent;
                        }
                        else if (IsTheatrical(entry.Name) && (entry.Source == EditionCatalogSource.Tmdb || !Knows(catalog, tagCut)))
                        {
                            // Fast Five "Extended" 131 = TMDb: TMDb may be the extended cut, or the tag lies
                            item.Verdict = EditionAuditVerdict.TagOnly;
                        }
                        else
                        {
                            item.Verdict = EditionAuditVerdict.Conflict;
                        }
                    }
                    else if (IsTheatrical(entry.Name))
                    {
                        item.Verdict = match.IsPal ? EditionAuditVerdict.Pal : EditionAuditVerdict.Consistent;
                    }
                    else
                    {
                        item.Verdict = taggedTheatrical ? EditionAuditVerdict.Conflict : EditionAuditVerdict.RuntimeOnly;
                    }

                    break;
                }

                case RuntimeMatchKind.Ambiguous:
                {
                    // The tag settles a runtime two cuts share
                    var own = tagged ? match.Candidates.FirstOrDefault(c => SameCut(c.Name, tagCut))
                            : match.Candidates.FirstOrDefault(c => IsTheatrical(c.Name));

                    if (own != null)
                    {
                        var pal = Math.Abs(minutes - own.RuntimeMinutes) > tolerance;

                        Suggest(item, own);
                        item.IsPal = pal;
                        item.Verdict = pal ? EditionAuditVerdict.Pal : EditionAuditVerdict.Consistent;
                    }

                    break;
                }

                case RuntimeMatchKind.UnknownLonger:
                {
                    if (WrongIdentification(minutes, theatricalReferences))
                    {
                        item.Verdict = EditionAuditVerdict.CheckIdentification;
                    }
                    else if (!tagged)
                    {
                        // Troy 196: a longer cut nobody named yet, the user names it
                        item.Verdict = EditionAuditVerdict.RuntimeOnly;
                    }

                    break;
                }

                default:
                {
                    if (WrongIdentification(minutes, theatricalReferences))
                    {
                        item.Verdict = EditionAuditVerdict.CheckIdentification;
                    }

                    break;
                }
            }
        }

        public static bool IsTheatrical(string name)
        {
            return PresetFor(name)?.Name == TheatricalName;
        }

        // Same cut: equal after normalisation, or both resolve to the same preset ("Directors Cut" / "director's cut roadshow")
        public static bool SameCut(string a, string b)
        {
            var na = EditionNormalizer.Normalize(a);
            var nb = EditionNormalizer.Normalize(b);

            if (na.IsNullOrWhiteSpace() || nb.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (na == nb)
            {
                return true;
            }

            var pa = PresetFor(a);

            return pa != null && pa.Name == PresetFor(b)?.Name;
        }

        public static EditionPreset PresetFor(string name)
        {
            if (name.IsNullOrWhiteSpace())
            {
                return null;
            }

            return EditionPresets.All.FirstOrDefault(p => EditionMatcher.RowMatchesEdition(PresetRow(p), name));
        }

        public static Movie PresetRow(EditionPreset preset)
        {
            return new Movie { Edition = preset.Name, EditionAliases = preset.Aliases };
        }

        private static bool IsBaseline(EditionCatalogEntry entry)
        {
            return entry.Source == EditionCatalogSource.Tmdb && EditionNormalizer.Normalize(entry.Name) == "default";
        }

        private static bool Knows(List<EditionCatalogEntry> catalog, string cut)
        {
            return catalog.Any(e => e.Confidence >= EditionRuntime.MinimumConfidence && e.RuntimeMinutes > 0 && SameCut(e.Name, cut));
        }

        private static bool WrongIdentification(double minutes, List<int> references)
        {
            return references != null && references.Count > 0 &&
                   references.All(r => Math.Abs(minutes - r) > IdentificationMismatchRatio * r);
        }

        private static void Suggest(EditionAuditItem item, EditionCatalogEntry entry)
        {
            item.RuntimeSuggestedEdition = entry.Name;
            item.RuntimeSuggestedMinutes = entry.RuntimeMinutes;
            item.CatalogSource = entry.Source;
        }
    }
}
