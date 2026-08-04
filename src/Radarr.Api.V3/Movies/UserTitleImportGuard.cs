using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Movies;

namespace Radarr.Api.V3.Movies
{
    public static class UserTitleImportGuard
    {
        /// <summary>
        /// True when <paramref name="title"/> is safe to attach to <paramref name="movie"/>.
        /// </summary>
        /// <remarks>
        /// The parser resolves a release to a movie by clean title across movie titles,
        /// alternative titles, AND translations, so a curated title that already belongs to a
        /// different movie would make that movie's releases resolve to this one. Both user
        /// importers share this check: a table-local guard (e.g. alternative titles only) lets
        /// a collision with another movie's translation through, and vice versa.
        /// FindByTitleCandidates sweeps all three tables and roman-numeral variants.
        /// </remarks>
        public static bool IsSafeForMovie(IMovieService movieService, string title, Movie movie)
        {
            var candidates = movieService.FindByTitleCandidates(new List<string> { title }, out _);

            return candidates == null || candidates.All(c => c.TmdbId == movie.TmdbId);
        }
    }
}
