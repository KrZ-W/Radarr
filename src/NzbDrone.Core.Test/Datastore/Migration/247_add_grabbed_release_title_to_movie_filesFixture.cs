using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class add_grabbed_release_title_to_movie_filesFixture : MigrationTest<add_grabbed_release_title_to_movie_files>
    {
        [Test]
        public void should_add_nullable_column_and_keep_existing_rows()
        {
            var db = WithMigrationTestDb(c =>
            {
                c.Insert.IntoTable("MovieFiles").Row(new
                {
                    MovieId = 1,
                    Quality = new { Quality = 6 }.ToJson(),
                    Size = 997478103,
                    DateAdded = DateTime.Now,
                    Languages = "[2]",
                    IndexerFlags = 0,
                    RelativePath = "Movie (2020).mkv"
                });
            });

            var rows = db.Query<MovieFile247>("SELECT \"Id\", \"GrabbedReleaseTitle\" FROM \"MovieFiles\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().GrabbedReleaseTitle.Should().BeNull();
        }

        private class MovieFile247
        {
            public int Id { get; set; }
            public string GrabbedReleaseTitle { get; set; }
        }
    }
}
