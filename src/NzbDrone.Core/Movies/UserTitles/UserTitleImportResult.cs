using System.Collections.Generic;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(user-titles): outcome of one import request, with skips broken down by reason.
    public class UserTitleImportResult
    {
        public int MoviesProcessed { get; set; }
        public int TitlesAdded { get; set; }

        // Rejected by the cross-movie guard: the title already resolves to a different movie.
        public int TitlesGuarded { get; set; }

        // Translations only: the language code could not be resolved.
        public int TitlesUnknownLanguage { get; set; }

        // Already stored for this movie (any source), equal to the movie's own title, or dropped by
        // the upsert's table-local guard.
        public int TitlesAlreadyPresent { get; set; }

        public int TitlesSkipped => TitlesGuarded + TitlesUnknownLanguage + TitlesAlreadyPresent;

        // "<title> (<year>) [tmdb:<id>]" for rows whose movie is not in the library.
        public List<string> MoviesNotFound { get; } = new List<string>();

        // "<title> (<year>) [tmdb:<id>]: <error>" for rows whose import threw. The rest of the
        // request still completes; nothing is rolled back for the failed movie.
        public List<string> MoviesFailed { get; } = new List<string>();
    }
}
