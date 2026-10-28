// krzw(edition-runtime-catalog): migration 249 (MovieEditionCatalog table, Movies.ExpectedRuntimeMinutes)
using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_edition_catalogFixture : MigrationTest<add_edition_catalog>
    {
        private const string InsertEntrySql = "INSERT INTO \"MovieEditionCatalog\" (\"MovieMetadataId\", \"Name\", \"RuntimeMinutes\", \"Source\", \"Confidence\", \"Attributes\", \"Updated\") " +
                                              "VALUES (@MovieMetadataId, @Name, @RuntimeMinutes, @Source, @Confidence, @Attributes, @Updated)";

        [Test]
        public void should_leave_existing_movies_with_an_unknown_expected_runtime()
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
                    Edition = "",
                    Added = DateTime.UtcNow
                });
            });

            var rows = db.Query<Movie249>("SELECT \"Id\", \"ExpectedRuntimeMinutes\" FROM \"Movies\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().ExpectedRuntimeMinutes.Should().BeNull();
        }

        [Test]
        public void should_create_the_catalog_table_unique_per_title_name_and_source()
        {
            using var conn = WithDapperMigrationTestDb();

            conn.Execute(InsertEntrySql, Entry("Extended", 2, 208));
            conn.Execute(InsertEntrySql, Entry("Extended", 3, 228));
            conn.Execute(InsertEntrySql, Entry("Theatrical", 2, 178));

            Action duplicate = () => conn.Execute(InsertEntrySql, Entry("Extended", 2, 210));
            duplicate.Should().Throw<Exception>();

            conn.Query<int>("SELECT \"RuntimeMinutes\" FROM \"MovieEditionCatalog\" WHERE \"MovieMetadataId\" = 1 ORDER BY \"RuntimeMinutes\"")
                .Should().Equal(178, 208, 228);
        }

        [Test]
        public void should_allow_a_null_attributes_column()
        {
            using var conn = WithDapperMigrationTestDb();

            var entry = new
            {
                MovieMetadataId = 1,
                Name = "Theatrical",
                RuntimeMinutes = 113,
                Source = 2,
                Confidence = 80,
                Attributes = (string)null,
                Updated = DateTime.UtcNow
            };

            conn.Execute(InsertEntrySql, entry);

            conn.Query<string>("SELECT \"Attributes\" FROM \"MovieEditionCatalog\"").Single().Should().BeNull();
        }

        private static object Entry(string name, int source, int runtime)
        {
            return new
            {
                MovieMetadataId = 1,
                Name = name,
                RuntimeMinutes = runtime,
                Source = source,
                Confidence = 70,
                Attributes = "[\"Blu Ray Extended Edition\"]",
                Updated = DateTime.UtcNow
            };
        }

        private class Movie249
        {
            public int Id { get; set; }
            public int? ExpectedRuntimeMinutes { get; set; }
        }
    }
}
