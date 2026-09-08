using System.Collections.Generic;

namespace Radarr.Api.V3.Movies
{
    public class UserAlternativeTitleImportResource
    {
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string MovieTitle { get; set; }
        public int Year { get; set; }
        public List<UserAlternativeTitleImportEntryResource> MissingFrenchTitles { get; set; }

        // Neutral alias for MissingFrenchTitles (the curated dataset's field name); either may be sent.
        public List<UserAlternativeTitleImportEntryResource> Titles
        {
            get => MissingFrenchTitles;
            set => MissingFrenchTitles = value;
        }
    }

    public class UserAlternativeTitleImportEntryResource
    {
        public string Title { get; set; }

        // ISO 3166-1 alpha-2 region code, optional.
        public string Region { get; set; }

        // ISO 639-1 language code, optional. The translation importer defaults it to
        // "fr" for compatibility with the curated FR/QC dataset; the alt-titles
        // importer ignores it (alternative titles carry no language).
        public string Language { get; set; }
    }

    public class UserAlternativeTitleImportSummaryResource
    {
        public int MoviesProcessed { get; set; }
        public int TitlesAdded { get; set; }
        public int TitlesSkipped { get; set; }

        // Breakdown of TitlesSkipped.
        public int TitlesGuarded { get; set; }
        public int TitlesUnknownLanguage { get; set; }
        public int TitlesAlreadyPresent { get; set; }

        public List<string> MoviesNotFound { get; set; } = new List<string>();

        // Rows whose import threw; the rest of the request still completed.
        public List<string> MoviesFailed { get; set; } = new List<string>();
    }
}
