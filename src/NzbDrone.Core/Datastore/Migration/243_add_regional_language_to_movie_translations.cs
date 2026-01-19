using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(243)]
    public class add_regional_language_to_movie_translations : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieTranslations").AddColumn("RegionalLanguage").AsString().Nullable();
        }
    }
}
