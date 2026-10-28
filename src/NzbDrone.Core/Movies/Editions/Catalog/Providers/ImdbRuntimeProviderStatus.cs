// krzw(edition-runtime-catalog): in-memory record of the IMDb runtime batches, read by the health check
using System;
using System.Threading;

namespace NzbDrone.Core.Movies.Editions.Catalog.Providers
{
    public interface IImdbRuntimeProviderStatus
    {
        int ConsecutiveFailedBatches { get; }
        DateTime? LastSuccess { get; }
        void RecordBatch(bool success);
    }

    public class ImdbRuntimeProviderStatus : IImdbRuntimeProviderStatus
    {
        private int _consecutiveFailedBatches;
        private long _lastSuccessTicks;

        public int ConsecutiveFailedBatches => Volatile.Read(ref _consecutiveFailedBatches);

        public DateTime? LastSuccess
        {
            get
            {
                var ticks = Interlocked.Read(ref _lastSuccessTicks);
                return ticks == 0 ? null : new DateTime(ticks, DateTimeKind.Utc);
            }
        }

        public void RecordBatch(bool success)
        {
            if (success)
            {
                Interlocked.Exchange(ref _consecutiveFailedBatches, 0);
                Interlocked.Exchange(ref _lastSuccessTicks, DateTime.UtcNow.Ticks);
            }
            else
            {
                Interlocked.Increment(ref _consecutiveFailedBatches);
            }
        }
    }
}
