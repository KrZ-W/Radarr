using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // krzw(multi-edition)
    [TestFixture]
    public class add_movie_editionsFixture : MigrationTest<add_movie_editions>
    {
        private const string InsertMovieSql = "INSERT INTO \"Movies\" (\"Path\", \"Monitored\", \"QualityProfileId\", \"MovieFileId\", \"MinimumAvailability\", \"MovieMetadataId\", \"Edition\") " +
                                              "VALUES (@Path, @Monitored, @QualityProfileId, @MovieFileId, @MinimumAvailability, @MovieMetadataId, @Edition)";

        [Test]
        public void should_turn_existing_rows_into_default_rows_without_rewriting_them()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Movies").Row(new
                {
                    Path = "/movies/Title (2020)",
                    Monitored = true,
                    QualityProfileId = 1,
                    MovieFileId = 0,
                    MinimumAvailability = 3,
                    MovieMetadataId = 1,
                    Added = DateTime.UtcNow
                });
            });

            var rows = db.Query<Movie248>("SELECT \"Id\", \"MovieMetadataId\", \"Edition\", \"EditionAliases\", \"EditionAcceptUntagged\" FROM \"Movies\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().MovieMetadataId.Should().Be(1);
            rows.First().Edition.Should().Be("");
            rows.First().EditionAliases.Should().BeNull();
            rows.First().EditionAcceptUntagged.Should().BeFalse();
        }

        [Test]
        public void should_replace_the_metadata_unique_index_with_a_metadata_edition_one()
        {
            using var conn = WithDapperMigrationTestDb();

            var databaseType = Mocker.Resolve<IMainDatabase>().DatabaseType;

            var sql = databaseType == DatabaseType.PostgreSQL
                ? "SELECT indexname FROM pg_indexes WHERE tablename = 'Movies'"
                : "SELECT name FROM sqlite_master WHERE type = 'index' AND tbl_name = 'Movies'";

            var indexNames = conn.Query<string>(sql).ToList();

            indexNames.Should().Contain("IX_Movies_MovieMetadataId_Edition");
            indexNames.Should().NotContain("IX_Movies_MovieMetadataId");
        }

        [Test]
        public void should_allow_several_editions_per_metadata_but_only_one_row_per_edition()
        {
            using var conn = WithDapperMigrationTestDb();

            conn.Execute(InsertMovieSql, MovieRow(""));
            conn.Execute(InsertMovieSql, MovieRow("Extended"));

            Action duplicateEdition = () => conn.Execute(InsertMovieSql, MovieRow("Extended"));
            duplicateEdition.Should().Throw<Exception>();

            Action duplicateDefault = () => conn.Execute(InsertMovieSql, MovieRow(""));
            duplicateDefault.Should().Throw<Exception>();

            conn.Query<string>("SELECT \"Edition\" FROM \"Movies\" WHERE \"MovieMetadataId\" = 1").Should().BeEquivalentTo(new List<string> { "", "Extended" });
        }

        private static object MovieRow(string edition)
        {
            return new
            {
                Path = "/movies/Title (2020)",
                Monitored = true,
                QualityProfileId = 1,
                MovieFileId = 0,
                MinimumAvailability = 3,
                MovieMetadataId = 1,
                Edition = edition
            };
        }

        private class Movie248
        {
            public int Id { get; set; }
            public int MovieMetadataId { get; set; }
            public string Edition { get; set; }
            public string EditionAliases { get; set; }
            public bool EditionAcceptUntagged { get; set; }
        }
    }
}
