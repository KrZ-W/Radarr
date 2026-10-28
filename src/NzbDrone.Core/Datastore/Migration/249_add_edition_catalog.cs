// krzw(edition-runtime-catalog): per-title catalog of known cuts with their runtimes, plus the expected runtime of each movie row
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // MovieEditionCatalog holds what each source (Tmdb, Wikidata, Imdb, Library, User) says about the cuts of a title;
    // one row per (title, name, source). Movies.ExpectedRuntimeMinutes is nullable: null = unknown, no rewrite of rows.
    [Migration(249)]
    public class add_edition_catalog : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("MovieEditionCatalog")
                .WithColumn("MovieMetadataId").AsInt32().NotNullable().Indexed()
                .WithColumn("Name").AsString().NotNullable()
                .WithColumn("RuntimeMinutes").AsInt32().NotNullable()
                .WithColumn("Source").AsInt32().NotNullable()
                .WithColumn("Confidence").AsInt32().NotNullable()
                .WithColumn("Attributes").AsString().Nullable()
                .WithColumn("Updated").AsDateTimeOffset().NotNullable();

            Create.Index("IX_MovieEditionCatalog_MovieMetadataId_Name_Source")
                .OnTable("MovieEditionCatalog")
                .OnColumn("MovieMetadataId").Ascending()
                .OnColumn("Name").Ascending()
                .OnColumn("Source").Ascending()
                .WithOptions().Unique();

            Alter.Table("Movies").AddColumn("ExpectedRuntimeMinutes").AsInt32().Nullable();
        }
    }
}
