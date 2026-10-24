// krzw(multi-edition): the accept/reject rule shared by EditionGateSpecification (grabs) and EditionGateImportSpecification (imports), spec section 7
using System;
using System.Collections.Generic;
using System.Linq;
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
        // behaviour otherwise). getSiblings is invoked only for a default row facing a tagged release, the one
        // case that needs the other rows, so untagged releases and edition rows cost no query.
        public static string Evaluate(Movie movie, string parsedEdition, Func<List<Movie>> getSiblings)
        {
            if (movie == null)
            {
                return null;
            }

            var edition = parsedEdition?.Trim() ?? string.Empty;

            if (movie.IsEdition)
            {
                if (edition.IsNullOrWhiteSpace())
                {
                    return movie.EditionAcceptUntagged ? null : string.Format(UntaggedNotAcceptedMessage, movie.Edition);
                }

                return EditionMatcher.RowMatchesEdition(movie, edition) ? null : string.Format(DoesNotMatchMessage, edition, movie.Edition);
            }

            if (edition.IsNullOrWhiteSpace())
            {
                return null;
            }

            var siblings = getSiblings?.Invoke() ?? new List<Movie>();

            if (!EditionMatcher.IsClaimedBySibling(movie, siblings, edition))
            {
                return null;
            }

            var claimant = siblings.First(s => s != null && s.Id != movie.Id && EditionMatcher.RowMatchesEdition(s, edition));

            return string.Format(BelongsToSiblingMessage, edition, claimant.Edition);
        }
    }
}
