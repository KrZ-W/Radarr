// krzw(multi-edition): maps a parsed release/file edition onto the Movie rows (default + editions) of one title
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions
{
    public static class EditionMatcher
    {
        // True iff row is an edition row and the parsed edition names it: exact normalized match against the
        // row's name or any alias, or, for a multi-word parsed edition, a whole-word containment of one of them
        // ("Extended Remastered" matches the Extended row). An empty parsed edition never matches an edition row.
        public static bool RowMatchesEdition(Movie row, string parsedEdition)
        {
            if (row == null || !row.IsEdition)
            {
                return false;
            }

            var parsed = EditionNormalizer.Normalize(parsedEdition);

            if (parsed.IsNullOrWhiteSpace())
            {
                return false;
            }

            return Candidates(row).Any(candidate => Matches(parsed, candidate));
        }

        // rows share one MovieMetadataId. Returns the first edition row the parsed edition names (monitored or
        // not, so it can be rejected there instead of leaking to the default row), else the default row, else null.
        public static Movie SelectRow(IEnumerable<Movie> rows, string parsedEdition)
        {
            var candidates = rows?.Where(r => r != null).ToList() ?? new List<Movie>();

            if (candidates.Count == 0)
            {
                return null;
            }

            var editionRow = candidates.FirstOrDefault(r => RowMatchesEdition(r, parsedEdition));

            if (editionRow != null)
            {
                return editionRow;
            }

            return candidates.FirstOrDefault(r => r.IsDefaultEdition);
        }

        // True iff a sibling edition row of defaultRow names the parsed edition (the default row must then reject it).
        public static bool IsClaimedBySibling(Movie defaultRow, IEnumerable<Movie> siblings, string parsedEdition)
        {
            if (siblings == null)
            {
                return false;
            }

            return siblings
                .Where(s => s != null && (defaultRow == null || s.Id != defaultRow.Id))
                .Any(s => RowMatchesEdition(s, parsedEdition));
        }

        private static IEnumerable<string> Candidates(Movie row)
        {
            var names = new List<string> { EditionNormalizer.Normalize(row.Edition) };

            names.AddRange(row.EditionAliases.Select(EditionNormalizer.Normalize));

            return names.Where(n => n.IsNotNullOrWhiteSpace()).Distinct();
        }

        private static bool Matches(string parsed, string candidate)
        {
            if (parsed == candidate)
            {
                return true;
            }

            if (!parsed.Contains(' '))
            {
                return false;
            }

            return (" " + parsed + " ").Contains(" " + candidate + " ");
        }
    }
}
