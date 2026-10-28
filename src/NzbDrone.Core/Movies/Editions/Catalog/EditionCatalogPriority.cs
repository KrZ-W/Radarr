// krzw(edition-runtime-catalog): merge priority of catalog sources (User > Library > Imdb > Wikidata > Tmdb)
namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public static class EditionCatalogPriority
    {
        public static int Rank(EditionCatalogSource source)
        {
            switch (source)
            {
                case EditionCatalogSource.User:
                    return 4;
                case EditionCatalogSource.Library:
                    return 3;
                case EditionCatalogSource.Imdb:
                    return 2;
                case EditionCatalogSource.Wikidata:
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
