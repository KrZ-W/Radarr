// krzw(edition-runtime-catalog): refreshes the provider entries of the edition catalog (POST /api/v3/command {"name":"RefreshEditionCatalog"})
using System.Collections.Generic;
using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public class RefreshEditionCatalogCommand : Command
    {
        public RefreshEditionCatalogCommand()
        {
            MovieIds = new List<int>();
        }

        // Empty = the whole library
        public List<int> MovieIds { get; set; }

        // Refresh titles whose catalog is younger than the staleness window too
        public bool Force { get; set; }

        public override bool SendUpdatesToClient => true;

        public override bool IsLongRunning => true;

        public override string CompletionMessage => "Completed";
    }
}
