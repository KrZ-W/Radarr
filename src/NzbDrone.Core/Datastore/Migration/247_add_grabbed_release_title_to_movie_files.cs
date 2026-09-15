using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(grabbed-release-title): the title of the release that was grabbed, captured at import
    [Migration(247)]
    public class add_grabbed_release_title_to_movie_files : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieFiles").AddColumn("GrabbedReleaseTitle").AsString().Nullable();
        }
    }
}
