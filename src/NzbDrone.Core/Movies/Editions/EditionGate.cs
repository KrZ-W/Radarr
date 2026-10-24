// krzw(multi-edition): the accept/reject rule shared by EditionGateSpecification (grabs) and EditionGateImportSpecification (imports), spec section 7
using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions
{
    public static class EditionGate
    {
        public const string BelongsToSiblingMessage = "Release edition '{0}' belongs to the {1} edition of this movie";
        public const string UntaggedNotAcceptedMessage = "Release has no edition tag and the {0} edition does not accept untagged releases";
        public const string DoesNotMatchMessage = "Release edition '{0}' does not match the {1} edition";

        // Returns null when the row may take a release or file carrying parsedEdition, else the rejection message.
        // Edition rows accept iff the edition names the row (name or alias), or it is empty and the row accepts
        // untagged releases. Default rows accept unless a sibling edition row claims the edition (legacy
        // behaviour otherwise). A row the download was grabbed for (grabbedForRow: the grab history names it) is
        // judged like a default row: the download is its whatever the client, folder or file names say, unless the
        // edition names a sibling row outright (spec section 7, "imports never cross rows"). getSiblings is invoked
        // only for a tagged release on a default or grabbed-for row that the row itself does not match, so untagged
        // releases and edition rows cost no query.
        public static string Evaluate(Movie movie, string parsedEdition, Func<List<Movie>> getSiblings, bool grabbedForRow = false)
        {
            if (movie == null)
            {
                return null;
            }

            var edition = parsedEdition?.Trim() ?? string.Empty;

            if (movie.IsEdition && !grabbedForRow)
            {
                if (edition.IsNullOrWhiteSpace())
                {
                    return movie.EditionAcceptUntagged ? null : string.Format(UntaggedNotAcceptedMessage, movie.Edition);
                }

                return EditionMatcher.RowMatchesEdition(movie, edition) ? null : string.Format(DoesNotMatchMessage, edition, movie.Edition);
            }

            if (edition.IsNullOrWhiteSpace() || EditionMatcher.RowMatchesEdition(movie, edition))
            {
                return null;
            }

            var claimant = EditionMatcher.FindClaimingSibling(movie, getSiblings?.Invoke(), edition);

            if (claimant == null)
            {
                return null;
            }

            return string.Format(BelongsToSiblingMessage, edition, claimant.Edition);
        }
    }
}
