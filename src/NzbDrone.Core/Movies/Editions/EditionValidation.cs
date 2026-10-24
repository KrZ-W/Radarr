// krzw(multi-edition): name, uniqueness and alias rules shared by the add-edition service and the movie API
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions
{
    public static class EditionValidation
    {
        // Plex truncates edition tags beyond this length, so canonical names stay within it.
        public const int MaxNameLength = 32;

        public const string NameRequiredMessage = "Edition name is required";
        public const string NameTooLongMessage = "Edition name must be at most {0} characters";
        public const string AlreadyAddedMessage = "This movie edition '{0}' has already been added";

        // null when the name is acceptable, else the problem to report.
        public static string NameProblem(string name)
        {
            var trimmed = name?.Trim() ?? "";

            if (trimmed.IsNullOrWhiteSpace())
            {
                return NameRequiredMessage;
            }

            if (trimmed.Length > MaxNameLength)
            {
                return string.Format(NameTooLongMessage, MaxNameLength);
            }

            return null;
        }

        // True iff another edition row of the title (rows share one TMDb id) already carries the name, as its
        // canonical name or as an alias, after normalization. Exact equality only: "Extended Remastered" may live
        // next to "Extended" (the matcher's containment rule then routes "Extended Remastered" releases to whichever
        // row comes first; that is the user's call). excludeMovieId leaves the row being renamed out.
        public static bool NameCollides(IEnumerable<Movie> rows, string name, int excludeMovieId = 0)
        {
            var normalized = EditionNormalizer.Normalize(name);

            if (normalized.IsNullOrWhiteSpace())
            {
                return false;
            }

            return (rows ?? Enumerable.Empty<Movie>())
                   .Where(row => row != null && row.IsEdition && row.Id != excludeMovieId)
                   .Any(row => EditionNormalizer.Normalize(row.Edition) == normalized ||
                               row.EditionAliases.Any(alias => EditionNormalizer.Normalize(alias) == normalized));
        }

        // The alias list as stored: every alias normalized, blanks dropped, duplicates removed, the normalized name first.
        public static List<string> BuildAliases(string name, IEnumerable<string> aliases)
        {
            var result = new List<string>();
            var candidates = new List<string> { name };

            candidates.AddRange(aliases ?? Enumerable.Empty<string>());

            foreach (var candidate in candidates.Select(EditionNormalizer.Normalize))
            {
                if (candidate.IsNotNullOrWhiteSpace() && !result.Contains(candidate))
                {
                    result.Add(candidate);
                }
            }

            return result;
        }
    }
}
