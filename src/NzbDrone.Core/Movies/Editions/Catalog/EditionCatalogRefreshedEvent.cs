// krzw(edition-runtime-catalog): published at the end of a catalog refresh (triggers the IMDb health check)
using NzbDrone.Common.Messaging;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public class EditionCatalogRefreshedEvent : IEvent
    {
        public EditionCatalogRefreshedEvent(int titles, int incomplete)
        {
            Titles = titles;
            Incomplete = incomplete;
        }

        // Titles refreshed (stale or forced)
        public int Titles { get; }

        // Titles for which an enabled online provider could not answer; they stay stale and are retried next time
        public int Incomplete { get; }
    }
}
