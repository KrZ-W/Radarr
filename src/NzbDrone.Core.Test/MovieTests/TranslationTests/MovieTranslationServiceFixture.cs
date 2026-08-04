using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.TranslationTests
{
    [TestFixture]
    public class MovieTranslationServiceFixture : CoreTest<MovieTranslationService>
    {
        private MovieMetadata _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<MovieMetadata>.CreateNew()
                .With(m => m.CleanTitle = "myothertitle")
                .With(m => m.Id = 1)
                .Build();

            Mocker.GetMock<IMovieTranslationRepository>()
                .Setup(r => r.FindByCleanTitles(It.IsAny<List<string>>()))
                .Returns(new List<MovieTranslation>());
        }

        private void GivenExistingTranslations(params MovieTranslation[] translations)
        {
            Mocker.GetMock<IMovieTranslationRepository>()
                .Setup(r => r.FindByMovieMetadataId(_movie.Id))
                .Returns(translations.ToList());
        }

        private static MovieTranslation TmdbRow(string title, string regional)
        {
            return new MovieTranslation
            {
                Title = title,
                CleanTitle = title.ToLowerInvariant().Replace(" ", string.Empty),
                Language = Language.French,
                RegionalLanguage = regional,
                SourceType = SourceType.Tmdb
            };
        }

        private static MovieTranslation UserRow(string title, string regional)
        {
            var row = TmdbRow(title, regional);
            row.SourceType = SourceType.User;
            row.MovieMetadataId = 1;
            return row;
        }

        [Test]
        public void should_preserve_user_translations_on_update()
        {
            var tmdbRow = TmdbRow("Titre France", "fr");
            tmdbRow.MovieMetadataId = _movie.Id;
            var userRow = UserRow("Titre Quebec", "fr-ca");

            GivenExistingTranslations(tmdbRow, userRow);

            var result = Subject.UpdateTranslations(new List<MovieTranslation>(), _movie);

            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.DeleteMany(new List<MovieTranslation> { tmdbRow }), Times.Once());
            result.Should().Contain(userRow);
        }

        [Test]
        public void should_drop_incoming_tmdb_duplicate_of_user_translation()
        {
            var userRow = UserRow("Titre Quebec", "fr-ca");

            GivenExistingTranslations(userRow);

            var incoming = TmdbRow("Titre Quebec", "fr-ca");

            var result = Subject.UpdateTranslations(new List<MovieTranslation> { incoming }, _movie);

            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.InsertMany(new List<MovieTranslation>()), Times.Once());
            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.DeleteMany(new List<MovieTranslation>()), Times.Once());
            result.Should().Contain(userRow);
            result.Should().NotContain(incoming);
        }

        [Test]
        public void should_upsert_user_translations_and_set_fields()
        {
            GivenExistingTranslations();

            var translation = new MovieTranslation
            {
                Title = "Le Titre Québécois",
                CleanTitle = "letitrequebecois",
                Language = Language.French,
                RegionalLanguage = "fr-CA"
            };

            var result = Subject.UpsertUserTranslations(new List<MovieTranslation> { translation }, _movie);

            result.Should().HaveCount(1);
            translation.SourceType.Should().Be(SourceType.User);
            translation.MovieMetadataId.Should().Be(_movie.Id);

            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.InsertMany(new List<MovieTranslation> { translation }), Times.Once());
        }

        [Test]
        public void should_not_upsert_main_title()
        {
            GivenExistingTranslations();

            var translation = new MovieTranslation { Title = "My Other Title", CleanTitle = "myothertitle", Language = Language.French, RegionalLanguage = "fr" };

            var result = Subject.UpsertUserTranslations(new List<MovieTranslation> { translation }, _movie);

            result.Should().BeEmpty();
            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.InsertMany(new List<MovieTranslation>()), Times.Once());
        }

        [Test]
        public void should_not_upsert_title_already_present_for_movie_any_source()
        {
            var tmdbRow = TmdbRow("Titre Quebec", "fr-ca");
            tmdbRow.MovieMetadataId = _movie.Id;

            GivenExistingTranslations(tmdbRow);

            var translation = new MovieTranslation { Title = "Titre Quebec", CleanTitle = "titrequebec", Language = Language.French, RegionalLanguage = "fr-CA" };

            var result = Subject.UpsertUserTranslations(new List<MovieTranslation> { translation }, _movie);

            result.Should().BeEmpty();
            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.InsertMany(new List<MovieTranslation>()), Times.Once());
        }

        [Test]
        public void should_not_upsert_title_owned_by_another_movie()
        {
            GivenExistingTranslations();

            var otherMovieTranslation = new MovieTranslation
            {
                Title = "Titre Quebec",
                CleanTitle = "titrequebec",
                MovieMetadataId = 999,
                Language = Language.French,
                RegionalLanguage = "fr-ca"
            };

            Mocker.GetMock<IMovieTranslationRepository>()
                .Setup(r => r.FindByCleanTitles(It.IsAny<List<string>>()))
                .Returns(new List<MovieTranslation> { otherMovieTranslation });

            var translation = new MovieTranslation { Title = "Titre Quebec", CleanTitle = "titrequebec", Language = Language.French, RegionalLanguage = "fr-CA" };

            var result = Subject.UpsertUserTranslations(new List<MovieTranslation> { translation }, _movie);

            result.Should().BeEmpty();
            Mocker.GetMock<IMovieTranslationRepository>().Verify(r => r.InsertMany(new List<MovieTranslation>()), Times.Once());
        }

        [Test]
        public void should_dedupe_user_translations_within_batch()
        {
            GivenExistingTranslations();

            var first = new MovieTranslation { Title = "Titre Quebec", CleanTitle = "titrequebec", Language = Language.French, RegionalLanguage = "fr-CA" };
            var second = new MovieTranslation { Title = "Titre Quebec", CleanTitle = "titrequebec", Language = Language.French, RegionalLanguage = "fr" };

            var result = Subject.UpsertUserTranslations(new List<MovieTranslation> { first, second }, _movie);

            result.Should().HaveCount(1);
        }
    }
}
