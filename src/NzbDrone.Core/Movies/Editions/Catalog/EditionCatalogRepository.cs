// krzw(edition-runtime-catalog): storage of the MovieEditionCatalog table
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public interface IEditionCatalogRepository : IBasicRepository<EditionCatalogEntry>
    {
        List<EditionCatalogEntry> GetByMetadata(int metadataId);
        List<EditionCatalogEntry> GetByMetadataIds(IEnumerable<int> metadataIds);
        void DeleteBySource(int metadataId, EditionCatalogSource source);
        List<EditionCatalogEntry> GetBySources(IEnumerable<EditionCatalogSource> sources);
    }

    public class EditionCatalogRepository : BasicRepository<EditionCatalogEntry>, IEditionCatalogRepository
    {
        public EditionCatalogRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<EditionCatalogEntry> GetByMetadata(int metadataId)
        {
            return Query(x => x.MovieMetadataId == metadataId);
        }

        public List<EditionCatalogEntry> GetByMetadataIds(IEnumerable<int> metadataIds)
        {
            var ids = (metadataIds ?? Enumerable.Empty<int>()).Distinct().ToList();

            if (ids.Count == 0)
            {
                return new List<EditionCatalogEntry>();
            }

            // Large libraries: stay well under SQLite's bound-parameter limit
            return ids.Chunk(500)
                      .SelectMany(chunk =>
                      {
                          var list = chunk.ToList();
                          return Query(x => list.Contains(x.MovieMetadataId));
                      })
                      .ToList();
        }

        public void DeleteBySource(int metadataId, EditionCatalogSource source)
        {
            Delete(x => x.MovieMetadataId == metadataId && x.Source == source);
        }

        // Catalog export: every entry of the given sources, across the library
        public List<EditionCatalogEntry> GetBySources(IEnumerable<EditionCatalogSource> sources)
        {
            var list = (sources ?? Enumerable.Empty<EditionCatalogSource>()).Distinct().ToList();

            if (list.Count == 0)
            {
                return new List<EditionCatalogEntry>();
            }

            // One equality query per source: an enum list in Contains is sent to Postgres as an unmapped array parameter
            return list.SelectMany(source => Query(x => x.Source == source)).ToList();
        }
    }
}
