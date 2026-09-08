using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(user-titles): builds a user translation from standard identifiers. Returns null when
    // the language cannot be resolved (the caller counts it as TitlesUnknownLanguage).
    public static class UserTranslationFactory
    {
        public static MovieTranslation Create(string title, string language, string region)
        {
            var languageInput = RegionalLanguageTag.Normalize(language);

            if (languageInput == null)
            {
                return null;
            }

            var iso = IsoLanguages.Find(languageInput);

            if (iso == null)
            {
                return null;
            }

            // A region carried in the language field ("fr-CA") is honoured when none is given
            // explicitly. The stored tag always uses the canonical two-letter code, not the
            // caller's spelling: "fra" and "fr-CA" both store as "fr-ca".
            var (_, regionFromLanguage) = RegionalLanguageTag.Split(languageInput);
            var effectiveRegion = region.IsNotNullOrWhiteSpace() ? region : regionFromLanguage;

            return new MovieTranslation
            {
                Title = title,
                CleanTitle = title.CleanMovieTitle(),
                Language = iso.Language,
                RegionalLanguage = RegionalLanguageTag.Build(iso.TwoLetterCode, effectiveRegion)
            };
        }
    }
}
