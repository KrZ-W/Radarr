using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Movies.UserTitles
{
    // krzw(user-titles): the cross-movie invariant behind both importers. The parser resolves a
    // release to a movie by clean title across movie titles, alternative titles AND translations,
    // so a curated title that already belongs to a different movie would make that movie's
    // releases resolve to this one. FindByTitleCandidates sweeps all three tables and
    // roman-numeral variants, which a table-local check cannot do.
    public interface IUserTitleGuard
    {
        // Returns the titles that are safe to attach to <paramref name="movie"/>; titles owned by
        // another movie are returned in <paramref name="rejected"/>.
        List<string> FilterSafe(Movie movie, List<string> titles, out List<string> rejected);
    }

    public class UserTitleGuard : IUserTitleGuard
    {
        private readonly IMovieService _movieService;

        public UserTitleGuard(IMovieService movieService)
        {
            _movieService = movieService;
        }

        public List<string> FilterSafe(Movie movie, List<string> titles, out List<string> rejected)
        {
            rejected = new List<string>();

            if (titles == null || titles.Count == 0)
            {
                return new List<string>();
            }

            // Fast path: one sweep for the whole batch. If no other movie owns any of these
            // titles, all of them are safe and no per-title queries are needed.
            var candidates = _movieService.FindByTitleCandidates(titles, out _);

            if (IsOwnedOnlyBy(candidates, movie))
            {
                return titles.ToList();
            }

            // Slow path: attribute per title so one colliding title does not reject its siblings.
            var safe = new List<string>();

            foreach (var title in titles)
            {
                var owners = _movieService.FindByTitleCandidates(new List<string> { title }, out _);

                if (IsOwnedOnlyBy(owners, movie))
                {
                    safe.Add(title);
                }
                else
                {
                    rejected.Add(title);
                }
            }

            return safe;
        }

        private static bool IsOwnedOnlyBy(List<Movie> candidates, Movie movie)
        {
            return candidates == null || candidates.All(c => c.TmdbId == movie.TmdbId);
        }
    }
}
