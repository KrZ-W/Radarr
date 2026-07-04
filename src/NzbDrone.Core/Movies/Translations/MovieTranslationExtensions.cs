using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Movies.Translations
{
    public static class MovieTranslationExtensions
    {
        // A language can have several translation rows (e.g. fr and fr-CA after migration 243), and
        // consumers that pick one by FirstOrDefault would otherwise depend on DB row order. Order
        // deterministically: variants from the configured RegionalTranslationVariants list first
        // (in list order), then the bare-language row, then remaining variants alphabetically.
        public static IEnumerable<MovieTranslation> OrderByRegionalPreference(this IEnumerable<MovieTranslation> translations, string variantsCsv)
        {
            var variants = (variantsCsv ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => v.ToLowerInvariant())
                .ToList();

            return translations
                .OrderBy(t =>
                {
                    var code = t.RegionalLanguage?.ToLowerInvariant();
                    var index = code == null ? -1 : variants.IndexOf(code);
                    return index >= 0 ? index : int.MaxValue;
                })
                .ThenBy(t => t.RegionalLanguage != null && t.RegionalLanguage.Contains('-'))
                .ThenBy(t => t.RegionalLanguage, StringComparer.OrdinalIgnoreCase);
        }
    }
}
