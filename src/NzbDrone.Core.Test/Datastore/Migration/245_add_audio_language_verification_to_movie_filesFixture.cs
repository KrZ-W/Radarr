using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Datastore.Migration
{
    [TestFixture]
    public class add_audio_language_verification_to_movie_filesFixture : MigrationTest<add_audio_language_verification_to_movie_files>
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

            var rows = db.Query<MovieFile245>("SELECT \"Id\", \"AudioLanguageVerification\" FROM \"MovieFiles\"").ToList();

            rows.Should().HaveCount(1);
            rows.First().AudioLanguageVerification.Should().BeNull();
        }

        private class MovieFile245
        {
            public int Id { get; set; }
            public string AudioLanguageVerification { get; set; }
        }
    }
}
