using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Movies.UserTitles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.UserTitleTests
{
    [TestFixture]
    public class UserTitleImportServiceFixture : CoreTest<UserTitleImportService>
    {
        private delegate List<string> FilterSafeDelegate(Movie movie, List<string> titles, out List<string> rejected);

        private Movie _movie;
        private List<string> _rejected;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                TmdbId = 194,
                Title = "Amélie",
                MovieMetadata = new MovieMetadata { Id = 10, TmdbId = 194, Title = "Amélie", CleanTitle = "amelie" }
            };

            _rejected = new List<string>();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTmdbId(194))
                  .Returns(_movie);

            // Guard passes everything through unless a test says otherwise.
            Mocker.GetMock<IUserTitleGuard>()
                  .Setup(g => g.FilterSafe(It.IsAny<Movie>(), It.IsAny<List<string>>(), out _rejected))
                  .Returns(new FilterSafeDelegate((Movie m, List<string> titles, out List<string> r) =>
                  {
                      r = new List<string>();
                      return titles;
                  }));

            // Upserts report every candidate as added unless a test says otherwise.
            Mocker.GetMock<IAlternativeTitleService>()
                  .Setup(s => s.UpsertUserTitles(It.IsAny<List<AlternativeTitle>>(), It.IsAny<MovieMetadata>()))
                  .Returns((List<AlternativeTitle> t, MovieMetadata m) => t);

            Mocker.GetMock<IMovieTranslationService>()
                  .Setup(s => s.UpsertUserTranslations(It.IsAny<List<MovieTranslation>>(), It.IsAny<MovieMetadata>()))
                  .Returns((List<MovieTranslation> t, MovieMetadata m) => t);
        }

        private static UserTitleImportRequest Request(int tmdbId, params string[] titles)
        {
            return new UserTitleImportRequest
            {
                TmdbId = tmdbId,
                MovieTitle = "Amélie",
                Year = 2001,
                Titles = titles.Select(t => new UserTitleImportEntry { Title = t, Language = "fr", Region = "CA" }).ToList()
            };
        }

        [Test]
        public void should_return_empty_result_for_null_or_empty_input()
        {
            Subject.ImportAlternativeTitles(null).MoviesProcessed.Should().Be(0);
            Subject.ImportTranslations(new List<UserTitleImportRequest>()).MoviesProcessed.Should().Be(0);
        }

        [Test]
        public void should_report_movie_not_found_when_neither_id_resolves()
        {
            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(999, "Titre") });

            result.MoviesProcessed.Should().Be(0);
            result.MoviesNotFound.Should().ContainSingle().Which.Should().Be("Amélie (2001) [tmdb:999]");
        }

        [Test]
        public void should_fall_back_to_imdb_id()
        {
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByImdbId("tt0211915")).Returns(_movie);

            var request = Request(999, "Titre");
            request.ImdbId = "tt0211915";

            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { request });

            result.MoviesProcessed.Should().Be(1);
            result.TitlesAdded.Should().Be(1);
        }

        [Test]
        public void should_not_look_up_tmdb_id_zero()
        {
            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(0, "Titre") });

            result.MoviesNotFound.Should().HaveCount(1);
            Mocker.GetMock<IMovieService>().Verify(s => s.FindByTmdbId(0), Times.Never());
        }

        [Test]
        public void should_drop_blank_titles_before_guarding()
        {
            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "Titre", "", "   ", null) });

            result.TitlesAdded.Should().Be(1);
            result.TitlesSkipped.Should().Be(0);
            Mocker.GetMock<IUserTitleGuard>().Verify(g => g.FilterSafe(_movie, It.Is<List<string>>(l => l.Count == 1), out _rejected), Times.Once());
        }

        [Test]
        public void should_count_guarded_titles_and_not_upsert_them()
        {
            var rejected = new List<string> { "Autre" };
            Mocker.GetMock<IUserTitleGuard>()
                  .Setup(g => g.FilterSafe(It.IsAny<Movie>(), It.IsAny<List<string>>(), out rejected))
                  .Returns(new List<string> { "Titre" });

            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "Titre", "Autre") });

            result.TitlesAdded.Should().Be(1);
            result.TitlesGuarded.Should().Be(1);
            result.TitlesSkipped.Should().Be(1);
            Mocker.GetMock<IAlternativeTitleService>().Verify(s => s.UpsertUserTitles(It.Is<List<AlternativeTitle>>(l => l.Count == 1 && l[0].Title == "Titre"), _movie.MovieMetadata.Value), Times.Once());
        }

        [Test]
        public void should_count_titles_the_upsert_did_not_add_as_already_present()
        {
            Mocker.GetMock<IAlternativeTitleService>()
                  .Setup(s => s.UpsertUserTitles(It.IsAny<List<AlternativeTitle>>(), It.IsAny<MovieMetadata>()))
                  .Returns((List<AlternativeTitle> t, MovieMetadata m) => t.Take(1).ToList());

            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "A", "B", "C") });

            result.TitlesAdded.Should().Be(1);
            result.TitlesAlreadyPresent.Should().Be(2);
            result.TitlesSkipped.Should().Be(2);
        }

        [Test]
        public void should_mark_alternative_titles_as_user_sourced()
        {
            Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "Titre") });

            Mocker.GetMock<IAlternativeTitleService>().Verify(s => s.UpsertUserTitles(It.Is<List<AlternativeTitle>>(l => l.All(t => t.SourceType == SourceType.User)), It.IsAny<MovieMetadata>()), Times.Once());
        }

        [Test]
        public void translations_should_count_unknown_language_and_map_the_rest()
        {
            var request = Request(194, "Bon", "Mauvais");
            request.Titles[1].Language = "zz";

            var result = Subject.ImportTranslations(new List<UserTitleImportRequest> { request });

            result.TitlesAdded.Should().Be(1);
            result.TitlesUnknownLanguage.Should().Be(1);
            result.TitlesAlreadyPresent.Should().Be(0);
            result.TitlesSkipped.Should().Be(1);
            Mocker.GetMock<IMovieTranslationService>().Verify(s => s.UpsertUserTranslations(It.Is<List<MovieTranslation>>(l => l.Count == 1 && l[0].RegionalLanguage == "fr-ca"), _movie.MovieMetadata.Value), Times.Once());
        }

        [Test]
        public void should_report_a_failed_movie_and_continue_with_the_next()
        {
            var other = new Movie { Id = 2, TmdbId = 550, Title = "Fight Club", MovieMetadata = new MovieMetadata { Id = 20, TmdbId = 550, Title = "Fight Club", CleanTitle = "fightclub" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(550)).Returns(other);

            Mocker.GetMock<IAlternativeTitleService>()
                  .Setup(s => s.UpsertUserTitles(It.IsAny<List<AlternativeTitle>>(), _movie.MovieMetadata.Value))
                  .Throws(new InvalidOperationException("boom"));

            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "Titre"), Request(550, "Club de combat") });

            result.MoviesFailed.Should().ContainSingle().Which.Should().Be("Amélie (2001) [tmdb:194]: boom");
            result.MoviesProcessed.Should().Be(1);
            result.TitlesAdded.Should().Be(1);

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_aggregate_counts_across_movies()
        {
            var other = new Movie { Id = 2, TmdbId = 550, Title = "Fight Club", MovieMetadata = new MovieMetadata { Id = 20, TmdbId = 550, Title = "Fight Club", CleanTitle = "fightclub" } };
            Mocker.GetMock<IMovieService>().Setup(s => s.FindByTmdbId(550)).Returns(other);

            var result = Subject.ImportAlternativeTitles(new List<UserTitleImportRequest> { Request(194, "A", "B"), Request(550, "C"), Request(999, "D") });

            result.MoviesProcessed.Should().Be(2);
            result.TitlesAdded.Should().Be(3);
            result.MoviesNotFound.Should().HaveCount(1);
            result.MoviesFailed.Should().BeEmpty();
        }
    }
}
