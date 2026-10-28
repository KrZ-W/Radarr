// krzw(edition-runtime-catalog): drops catalog entries of titles whose MovieMetadata row is gone
using Dapper;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public class CleanupOrphanedEditionCatalog : IHousekeepingTask
    {
        private readonly IMainDatabase _database;

        public CleanupOrphanedEditionCatalog(IMainDatabase database)
        {
            _database = database;
        }

        public void Clean()
        {
            using var mapper = _database.OpenConnection();
            mapper.Execute(@"DELETE FROM ""MovieEditionCatalog""
                             WHERE ""Id"" IN (
                             SELECT ""MovieEditionCatalog"".""Id"" FROM ""MovieEditionCatalog""
                             LEFT OUTER JOIN ""MovieMetadata""
                             ON ""MovieEditionCatalog"".""MovieMetadataId"" = ""MovieMetadata"".""Id""
                             WHERE ""MovieMetadata"".""Id"" IS NULL)");
        }
    }
}
