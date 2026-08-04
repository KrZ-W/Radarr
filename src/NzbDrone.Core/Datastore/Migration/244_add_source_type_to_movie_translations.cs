using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(244)]
    public class add_source_type_to_movie_translations : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("MovieTranslations").AddColumn("SourceType").AsInt32().NotNullable().WithDefaultValue(0);
        }
    }
}
