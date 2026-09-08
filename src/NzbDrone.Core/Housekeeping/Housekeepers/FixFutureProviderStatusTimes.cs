using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.ThingiProvider.Status;

namespace NzbDrone.Core.Housekeeping.Housekeepers
{
    public abstract class FixFutureProviderStatusTimes<TModel>
        where TModel : ProviderStatusBase, new()
    {
        private readonly IProviderStatusRepository<TModel> _repo;

        protected FixFutureProviderStatusTimes(IProviderStatusRepository<TModel> repo)
        {
            _repo = repo;
        }

        public void Clean()
        {
            var now = DateTime.UtcNow;
            var statuses = _repo.All().ToList();
            var toUpdate = new List<TModel>();

            foreach (var status in statuses)
            {
                var updated = false;

                // krzw(indexer-cooldown): a custom cooldown schedule (IndexerCooldownPeriods) can have more levels than the
                // default table, so a persisted EscalationLevel may exceed the last index - clamp it,
                // matching CalculateBackOffPeriod.
                var escalationLevel = Math.Min(status.EscalationLevel, EscalationBackOff.Periods.Length - 1);
                var escalationDelay = EscalationBackOff.Periods[escalationLevel];
                var disabledTill = now.AddMinutes(escalationDelay);

                if (status.DisabledTill > disabledTill)
                {
                    status.DisabledTill = disabledTill;
                    updated = true;
                }

                if (status.InitialFailure > now)
                {
                    status.InitialFailure = now;
                    updated = true;
                }

                if (status.MostRecentFailure > now)
                {
                    status.MostRecentFailure = now;
                    updated = true;
                }

                if (updated)
                {
                    toUpdate.Add(status);
                }
            }

            _repo.UpdateMany(toUpdate);
        }
    }
}
