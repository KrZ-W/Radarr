using System.Collections.Generic;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(user-titles): one curated-dataset row - a movie identity plus the titles to attach to it.
    // Neutral shape shared by the alternative-title and translation importers; the API layer maps
    // its dataset-specific resource (missingFrenchTitles, "fr" default) onto this.
    public class UserTitleImportRequest
    {
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string MovieTitle { get; set; }
        public int Year { get; set; }
        public List<UserTitleImportEntry> Titles { get; set; } = new List<UserTitleImportEntry>();
    }

    public class UserTitleImportEntry
    {
        public string Title { get; set; }

        // ISO 639-1 language code. ISO 639-2 ("fra") and full tags ("fr-CA") are accepted and
        // canonicalised. Translations only; alternative titles carry no language.
        public string Language { get; set; }

        // ISO 3166-1 alpha-2 region code, optional. Translations only.
        public string Region { get; set; }
    }
}
