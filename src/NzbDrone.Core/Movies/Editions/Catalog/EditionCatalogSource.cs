// krzw(edition-runtime-catalog): where a catalog entry comes from; stored as int, values are part of the API and must not change
namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public enum EditionCatalogSource
    {
        Tmdb = 0,
        Wikidata = 1,
        Imdb = 2,
        Library = 3,
        User = 4
    }
}
