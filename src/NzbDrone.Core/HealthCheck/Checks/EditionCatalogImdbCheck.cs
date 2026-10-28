// krzw(edition-runtime-catalog): warns when the IMDb runtime provider is on and its last batches all failed
using System.Collections.Generic;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;

namespace NzbDrone.Core.HealthCheck.Checks
{
    [CheckOn(typeof(ConfigSavedEvent))]
    [CheckOn(typeof(EditionCatalogRefreshedEvent))]
    public class EditionCatalogImdbCheck : HealthCheckBase
    {
        public const int FailedBatchesThreshold = 3;

        private readonly IConfigService _configService;
        private readonly IImdbRuntimeProviderStatus _status;

        public EditionCatalogImdbCheck(IConfigService configService, IImdbRuntimeProviderStatus status, ILocalizationService localizationService)
            : base(localizationService)
        {
            _configService = configService;
            _status = status;
        }

        public override HealthCheck Check()
        {
            if (!_configService.EditionCatalogImdbEnabled)
            {
                return new HealthCheck(GetType());
            }

            var failed = _status.ConsecutiveFailedBatches;

            if (failed >= FailedBatchesThreshold)
            {
                return new HealthCheck(GetType(),
                    HealthCheckResult.Warning,
                    HealthCheckReason.EditionCatalogImdbFailing,
                    _localizationService.GetLocalizedString("EditionCatalogImdbFailingHealthCheckMessage", new Dictionary<string, object>
                    {
                        { "count", failed }
                    }),
                    "#edition-catalog-imdb-failing");
            }

            return new HealthCheck(GetType());
        }
    }
}
