using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // krzw(multi-edition): editions are extra Movies rows sharing one MovieMetadata; Edition '' = default row.
    // Adds the three edition columns with defaults (no row rewrites: every existing row becomes the default row)
    // and widens the uniqueness from MovieMetadataId to (MovieMetadataId, Edition).
    [Migration(248)]
    public class add_movie_editions : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            // Migration 207 made MovieMetadataId unique through FluentMigrator's default index name
            // IX_Movies_MovieMetadataId on both engines. On SQLite the table recreate that 207 triggered
            // (newer runners) also baked an inline "UNIQUE" into the column, which only another recreate removes:
            // AlterColumn without Unique() runs through NzbDroneSQLiteProcessor and rebuilds the table
            // with the same data and the remaining named indexes.
            Execute.Sql("DROP INDEX IF EXISTS \"IX_Movies_MovieMetadataId\"");
            IfDatabase("sqlite").Alter.Table("Movies").AlterColumn("MovieMetadataId").AsInt32().NotNullable();
            IfDatabase("postgres").Execute.Sql("ALTER TABLE \"Movies\" DROP CONSTRAINT IF EXISTS \"Movies_MovieMetadataId_key\"");

            Alter.Table("Movies").AddColumn("Edition").AsString().NotNullable().WithDefaultValue("");
            Alter.Table("Movies").AddColumn("EditionAliases").AsString().Nullable();
            Alter.Table("Movies").AddColumn("EditionAcceptUntagged").AsBoolean().NotNullable().WithDefaultValue(false);

            Create.Index("IX_Movies_MovieMetadataId_Edition")
                .OnTable("Movies")
                .OnColumn("MovieMetadataId").Ascending()
                .OnColumn("Edition").Ascending()
                .WithOptions().Unique();
        }
    }
}
