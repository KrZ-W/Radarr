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
    }

    public class UserAlternativeTitleImportEntryResource
    {
        public string Title { get; set; }
        public string Region { get; set; }
    }

    public class UserAlternativeTitleImportSummaryResource
    {
        public int MoviesProcessed { get; set; }
        public int TitlesAdded { get; set; }
        public int TitlesSkipped { get; set; }
        public List<string> MoviesNotFound { get; set; } = new List<string>();
    }
}
