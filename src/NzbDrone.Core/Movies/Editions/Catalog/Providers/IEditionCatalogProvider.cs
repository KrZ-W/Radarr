// krzw(edition-runtime-catalog): a source of per-title edition runtimes (IMDb, Wikidata, TMDb baseline)
using System.Collections.Generic;

namespace NzbDrone.Core.Movies.Editions.Catalog.Providers
{
    public interface IEditionCatalogProvider
    {
        EditionCatalogSource Source { get; }
        bool Enabled { get; }

        // Keyed by MovieMetadata id. A title present in the result (even with an empty list) was answered and its entries
        // for this source are replaced; a title missing from the result could not be fetched and keeps its cached entries.
        Dictionary<int, List<EditionCatalogEntry>> Fetch(List<MovieMetadata> metadata);
    }
}
