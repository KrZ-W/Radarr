using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(regional-translations): the single definition of the RegionalLanguage storage shape.
    // A tag is "xx" or "xx-yy", lowercase. TMDB rows (SkyHookProxy) and user rows
    // (UserTranslationFactory) both go through here, so the Regional Translation Variants
    // filter and the OnePerRegion dedupe treat them alike.
    public static class RegionalLanguageTag
    {
        public static string Normalize(string code)
        {
            return code.IsNullOrWhiteSpace() ? null : code.Trim().ToLowerInvariant();
        }

        public static string Build(string twoLetterLanguage, string region)
        {
            var language = Normalize(twoLetterLanguage);
            var normalizedRegion = Normalize(region);

            if (language == null)
            {
                return null;
            }

            return normalizedRegion == null ? language : $"{language}-{normalizedRegion}";
        }

        // "fr-CA" -> ("fr", "ca"); "fr" -> ("fr", null); null/blank -> (null, null).
        public static (string Language, string Region) Split(string tag)
        {
            var normalized = Normalize(tag);

            if (normalized == null)
            {
                return (null, null);
            }

            var parts = normalized.Split('-', 2);

            return (parts[0], parts.Length > 1 && parts[1].IsNotNullOrWhiteSpace() ? parts[1] : null);
        }
    }
}
