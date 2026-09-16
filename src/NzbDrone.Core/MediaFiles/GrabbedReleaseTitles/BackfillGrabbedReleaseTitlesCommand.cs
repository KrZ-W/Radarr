using NzbDrone.Core.Messaging.Commands;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    /// <summary>
    /// krzw(grabbed-release-title)
    /// Manual one-shot task: POST /api/v3/command {"name":"BackfillGrabbedReleaseTitles"}.
    /// Fills MovieFiles.GrabbedReleaseTitle for files imported before the feature existed, from the
    /// grab history rows that are still in the database. Idempotent and safe to re-run.
    /// </summary>
    public class BackfillGrabbedReleaseTitlesCommand : Command
    {
        public override bool SendUpdatesToClient => true;

        public override bool IsLongRunning => true;
    }
}
