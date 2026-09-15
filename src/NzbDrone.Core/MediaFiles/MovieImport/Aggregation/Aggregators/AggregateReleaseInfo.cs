using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles.GrabbedReleaseTitles;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators
{
    public class AggregateReleaseInfo : IAggregateLocalMovie
    {
        // krzw(grabbed-release-title): Order 0, ahead of every other aggregator. AggregateLanguage (Order 1)
        // runs AugmentLanguageFromAudioProbe, whose rejection predictor scores through the grabbed release
        // title this aggregator sets; at an equal Order the two only tied and the title could still be null.
        // This aggregator reads nothing but the download client item and grab history, so it has no
        // dependency on anything another Order-1 aggregator produces.
        public int Order => 0;

        private readonly IHistoryService _historyService;

        public AggregateReleaseInfo(IHistoryService historyService)
        {
            _historyService = historyService;
        }

        public LocalMovie Aggregate(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (downloadClientItem == null)
            {
                return localMovie;
            }

            var grabbedHistories = _historyService.FindByDownloadId(downloadClientItem.DownloadId)
                .Where(h => h.EventType == MovieHistoryEventType.Grabbed)
                .ToList();

            if (grabbedHistories.Empty())
            {
                return localMovie;
            }

            localMovie.Release = new GrabbedReleaseInfo(grabbedHistories);

            // krzw(grabbed-release-title): the grabbed title the import decision scores against
            localMovie.GrabbedReleaseTitle = GrabbedReleaseTitleSanitizer.Sanitize(localMovie.Release.Title);

            return localMovie;
        }
    }
}
