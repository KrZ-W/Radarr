using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.CustomFormats;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.MovieRepositoryTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class MovieRepositoryEditionFixture : DbTest<MovieRepository, Movie>
    {
        private const int TmdbId = 603;
        private const string ImdbId = "tt0133093";
        private const string MoviePath = "/movies/The Matrix (1999)";

        private QualityProfile _profile;
        private MovieMetadata _metadata;
        private Movie _default;
        private Movie _extended;

        [SetUp]
        public void Setup()
        {
            var profileRepository = Mocker.Resolve<QualityProfileRepository>();
            Mocker.SetConstant<IQualityProfileRepository>(profileRepository);

            Mocker.GetMock<ICustomFormatService>()
                .Setup(x => x.All())
                .Returns(new List<CustomFormat>());

            _profile = new QualityProfile
            {
                Items = Qualities.QualityFixture.GetDefaultQualities(Quality.Bluray1080p, Quality.DVD, Quality.HDTV720p),
                FormatItems = CustomFormatsTestHelpers.GetDefaultFormatItems(),
                MinFormatScore = 0,
                Cutoff = Quality.Bluray1080p.Id,
                Name = "TestProfile"
            };

            profileRepository.Insert(_profile);

            _metadata = Builder<MovieMetadata>.CreateNew()
                                              .With(m => m.TmdbId = TmdbId)
                                              .With(m => m.ImdbId = ImdbId)
                                              .BuildNew();

            Db.Insert(_metadata);

            // The edition row is inserted first so it gets the lower Id: "prefers default" must not rely on insertion order.
            _extended = NewRow("Extended", "extended", "extended cut");
            _extended.Monitored = false;
            Subject.Insert(_extended);

            _default = NewRow("");
            Subject.Insert(_default);
        }

        private Movie NewRow(string edition, params string[] aliases)
        {
            return NewRow(_metadata.Id, edition, aliases);
        }

        private Movie NewRow(int metadataId, string edition, params string[] aliases)
        {
            return Builder<Movie>.CreateNew()
                                 .With(m => m.Id = 0)
                                 .With(m => m.MovieMetadataId = metadataId)
                                 .With(m => m.QualityProfileId = _profile.Id)
                                 .With(m => m.Path = MoviePath)
                                 .With(m => m.Monitored = true)
                                 .With(m => m.MovieFileId = 0)
                                 .With(m => m.Edition = edition)
                                 .With(m => m.EditionAliases = aliases.ToList())
                                 .With(m => m.EditionAcceptUntagged = false)
                                 .BuildNew();
        }

        [Test]
        public void find_all_by_tmdb_id_should_return_every_row_default_first()
        {
            var rows = Subject.FindAllByTmdbId(TmdbId);

            rows.Should().HaveCount(2);
            rows[0].Id.Should().Be(_default.Id);
            rows[0].IsDefaultEdition.Should().BeTrue();
            rows[1].Id.Should().Be(_extended.Id);
            rows[1].Edition.Should().Be("Extended");
            rows.Should().OnlyContain(r => r.MovieMetadata.Value.TmdbId == TmdbId);
        }

        [Test]
        public void find_all_by_tmdb_id_should_be_empty_for_unknown_ids()
        {
            Subject.FindAllByTmdbId(TmdbId + 1).Should().BeEmpty();
        }

        // A second title with a default row only, as the calendar and the bulk editor mix titles in one lookup.
        private Movie GivenOtherTitle(int tmdbId)
        {
            var metadata = Builder<MovieMetadata>.CreateNew()
                                                 .With(m => m.Id = 0)
                                                 .With(m => m.TmdbId = tmdbId)
                                                 .With(m => m.ImdbId = "tt" + tmdbId.ToString("D7"))
                                                 .With(m => m.CleanTitle = "other" + tmdbId)
                                                 .BuildNew();

            Db.Insert(metadata);

            var row = NewRow(metadata.Id, "");
            row.Path = "/movies/Other (" + tmdbId + ")";
            Subject.Insert(row);

            return row;
        }

        [Test]
        public void find_by_tmdb_id_list_should_return_every_row_of_the_given_titles_default_rows_first()
        {
            var other = GivenOtherTitle(TmdbId + 1);

            var rows = Subject.FindByTmdbId(new List<int> { TmdbId, TmdbId + 1 });

            rows.Should().HaveCount(3);
            rows.Select(r => r.Id).Should().BeEquivalentTo(new[] { _default.Id, _extended.Id, other.Id });
            rows.Take(2).Should().OnlyContain(r => r.IsDefaultEdition);
            rows.Take(2).Select(r => r.Id).Should().BeInAscendingOrder();
            rows[2].Id.Should().Be(_extended.Id);
            rows.Should().OnlyContain(r => r.MovieMetadata.Value != null && r.QualityProfile != null);
        }

        [Test]
        public void find_by_tmdb_id_list_should_only_return_rows_of_the_given_titles()
        {
            GivenOtherTitle(TmdbId + 1);

            Subject.FindByTmdbId(new List<int> { TmdbId }).Select(r => r.Id).Should().BeEquivalentTo(new[] { _default.Id, _extended.Id });
            Subject.FindByTmdbId(new List<int> { TmdbId + 2 }).Should().BeEmpty();
        }

        [Test]
        public void find_by_tmdb_id_should_prefer_the_default_row()
        {
            _extended.Id.Should().BeLessThan(_default.Id);

            var movie = Subject.FindByTmdbId(TmdbId);

            movie.Should().NotBeNull();
            movie.Id.Should().Be(_default.Id);
            movie.IsDefaultEdition.Should().BeTrue();
        }

        [Test]
        public void find_by_tmdb_id_should_not_leak_an_edition_row_when_no_default_exists()
        {
            Subject.Delete(_default.Id);

            Subject.FindByTmdbId(TmdbId).Should().BeNull();
            Subject.FindAllByTmdbId(TmdbId).Should().HaveCount(1);
        }

        [Test]
        public void find_by_imdb_id_and_path_should_prefer_the_default_row()
        {
            Subject.FindByImdbId(ImdbId).Id.Should().Be(_default.Id);
            Subject.FindByPath(MoviePath).Id.Should().Be(_default.Id);
        }

        [Test]
        public void get_edition_siblings_should_return_the_other_rows_only()
        {
            var ofDefault = Subject.GetEditionSiblings(_default.Id);
            var ofExtended = Subject.GetEditionSiblings(_extended.Id);

            ofDefault.Select(r => r.Id).Should().BeEquivalentTo(new[] { _extended.Id });
            ofExtended.Select(r => r.Id).Should().BeEquivalentTo(new[] { _default.Id });
        }

        [Test]
        public void get_edition_siblings_should_be_empty_without_siblings_or_for_unknown_ids()
        {
            Subject.Delete(_extended.Id);

            Subject.GetEditionSiblings(_default.Id).Should().BeEmpty();
            Subject.GetEditionSiblings(12345).Should().BeEmpty();
        }

        [Test]
        public void get_default_edition_should_return_the_default_row()
        {
            Subject.GetDefaultEdition(_metadata.Id).Id.Should().Be(_default.Id);
            Subject.GetDefaultEdition(_metadata.Id + 1).Should().BeNull();

            Subject.Delete(_default.Id);
            Subject.GetDefaultEdition(_metadata.Id).Should().BeNull();
        }

        [TestCase("Extended", true)]
        [TestCase("extended", true)]
        [TestCase("EXTENDED", true)]
        [TestCase("", true)]
        [TestCase("Extended Cut", false)]
        [TestCase("Director's Cut", false)]
        public void edition_exists_should_compare_normalized_names(string editionName, bool expected)
        {
            Subject.EditionExists(TmdbId, editionName).Should().Be(expected);
        }

        [Test]
        public void edition_exists_should_be_false_for_unknown_tmdb_ids()
        {
            Subject.EditionExists(TmdbId + 1, "Extended").Should().BeFalse();
            Subject.EditionExists(TmdbId + 1, "").Should().BeFalse();
        }

        [Test]
        public void edition_fields_should_round_trip()
        {
            var stored = Subject.Get(_extended.Id);

            stored.Edition.Should().Be("Extended");
            stored.EditionAliases.Should().Equal("extended", "extended cut");
            stored.EditionAcceptUntagged.Should().BeFalse();
            stored.IsEdition.Should().BeTrue();

            stored.EditionAcceptUntagged = true;
            stored.EditionAliases.Add("extended edition");
            Subject.Update(stored);

            var reloaded = Subject.Get(_extended.Id);
            reloaded.EditionAcceptUntagged.Should().BeTrue();
            reloaded.EditionAliases.Should().Equal("extended", "extended cut", "extended edition");
        }

        [Test]
        public void edition_aliases_should_load_as_empty_list_when_column_is_null()
        {
            using (var conn = Db.OpenConnection())
            {
                conn.Execute("UPDATE \"Movies\" SET \"EditionAliases\" = NULL WHERE \"Id\" = @Id", new { Id = _extended.Id });
            }

            var reloaded = Subject.Get(_extended.Id);

            reloaded.EditionAliases.Should().NotBeNull();
            reloaded.EditionAliases.Should().BeEmpty();

            Subject.All().Should().OnlyContain(m => m.EditionAliases != null);
        }

        [Test]
        public void inserting_a_second_row_with_the_same_edition_should_fail()
        {
            var duplicate = NewRow("Extended");

            Action insert = () => Subject.Insert(duplicate);

            insert.Should().Throw<Exception>().WithMessage("*UNIQUE*");
        }

        [Test]
        public void all_should_still_return_every_row_with_metadata()
        {
            var all = Subject.All().ToList();

            all.Should().HaveCount(2);
            all.Should().OnlyContain(m => m.MovieMetadata.Value.TmdbId == TmdbId);
            all.Should().OnlyContain(m => m.QualityProfile != null);
        }
    }
}
