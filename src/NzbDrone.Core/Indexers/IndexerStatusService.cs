using System;
using System.Collections.Generic;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Indexers
{
    public interface IIndexerStatusService : IProviderStatusServiceBase<IndexerStatus>
    {
        ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId);
        IDictionary<string, string> GetIndexerCookies(int indexerId);
        DateTime GetIndexerCookiesExpirationDate(int indexerId);

        void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo);
        void UpdateCookies(int indexerId, IDictionary<string, string> cookies, DateTime? expiration);
    }

    public class IndexerStatusService : ProviderStatusServiceBase<IIndexer, IndexerStatus>, IIndexerStatusService
    {
        private readonly IConfigService _configService;

        public IndexerStatusService(IIndexerStatusRepository providerStatusRepository, IEventAggregator eventAggregator, IRuntimeInfo runtimeInfo, IConfigService configService, Logger logger)
            : base(providerStatusRepository, eventAggregator, runtimeInfo, logger)
        {
            _configService = configService;
        }

        protected override int[] GetEscalationPeriods()
        {
            return IndexerCooldownPeriods.ToSecondsTable(_configService.IndexerCooldownPeriods);
        }

        public ReleaseInfo GetLastRssSyncReleaseInfo(int indexerId)
        {
            return GetProviderStatus(indexerId).LastRssSyncReleaseInfo;
        }

        public IDictionary<string, string> GetIndexerCookies(int indexerId)
        {
            return GetProviderStatus(indexerId).Cookies;
        }

        public DateTime GetIndexerCookiesExpirationDate(int indexerId)
        {
            return GetProviderStatus(indexerId).CookiesExpirationDate ?? DateTime.Now + TimeSpan.FromDays(12);
        }

        public void UpdateRssSyncStatus(int indexerId, ReleaseInfo releaseInfo)
        {
            lock (_syncRoot)
            {
                var status = GetProviderStatus(indexerId);

                status.LastRssSyncReleaseInfo = releaseInfo;

                _providerStatusRepository.Upsert(status);
            }
        }

        public void UpdateCookies(int indexerId, IDictionary<string, string> cookies, DateTime? expiration)
        {
            lock (_syncRoot)
            {
                var status = GetProviderStatus(indexerId);
                status.Cookies = cookies;
                status.CookiesExpirationDate = expiration;
                _providerStatusRepository.Upsert(status);
            }
        }
    }
}
