using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.AlternativeTitles;
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.ImdbTitles;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Movies.UserTitles;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.ImdbTitleTests
{
    [TestFixture]
    public class ImdbTitleSyncServiceFixture : CoreTest<ImdbTitleSyncService>
    {
        private Movie _movie;
        private List<ImdbAkasRow> _rows;
        private List<AlternativeTitle> _alternativeTitles;
        private List<MovieTranslation> _translations;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                TmdbId = 194,
                MovieMetadata = new MovieMetadata { Id = 10, TmdbId = 194, ImdbId = "tt0211915", Title = "Amélie", OriginalTitle = "Le Fabuleux Destin d'Amélie Poulain", Year = 2001 }
            };

            _rows = new List<ImdbAkasRow>();
            _alternativeTitles = new List<AlternativeTitle>();
            _translations = new List<MovieTranslation>();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderRegions).Returns("CA,FR");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderLanguages).Returns("fr");
            Mocker.GetMock<IConfigService>().SetupGet(c => c.RegionalTranslationVariants).Returns("fr-CA,en-CA");

            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(true);
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetTitles("tt0211915")).Returns(() => _rows);

            Mocker.GetMock<IAlternativeTitleService>().Setup(s => s.GetAllTitlesForMovieMetadata(10)).Returns(() => _alternativeTitles);
            Mocker.GetMock<IMovieTranslationService>().Setup(s => s.GetAllTranslationsForMovieMetadata(10)).Returns(() => _translations);

            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(() => new List<Movie> { _movie });

            Mocker.GetMock<IUserTitleImportService>()
                  .Setup(s => s.ImportAlternativeTitles(It.IsAny<List<UserTitleImportRequest>>()))
                  .Returns((List<UserTitleImportRequest> r) => new UserTitleImportResult { MoviesProcessed = r.Count, TitlesAdded = r.Sum(x => x.Titles.Count) });

            Mocker.GetMock<IUserTitleImportService>()
                  .Setup(s => s.ImportTranslations(It.IsAny<List<UserTitleImportRequest>>()))
                  .Returns((List<UserTitleImportRequest> r) => new UserTitleImportResult { MoviesProcessed = r.Count, TitlesAdded = r.Sum(x => x.Titles.Count) });
        }

        private static ImdbAkasRow Row(string title, string region = null, string language = null)
        {
            return new ImdbAkasRow { Tconst = "tt0211915", Title = title, Region = region, Language = language };
        }

        private List<UserTitleImportRequest> AlternativeRequests()
        {
            var captured = new List<UserTitleImportRequest>();
            Mocker.GetMock<IUserTitleImportService>().Invocations
                  .Where(i => i.Method.Name == nameof(IUserTitleImportService.ImportAlternativeTitles))
                  .ToList()
                  .ForEach(i => captured.AddRange((List<UserTitleImportRequest>)i.Arguments[0]));
            return captured;
        }

        private List<UserTitleImportRequest> TranslationRequests()
        {
            var captured = new List<UserTitleImportRequest>();
            Mocker.GetMock<IUserTitleImportService>().Invocations
                  .Where(i => i.Method.Name == nameof(IUserTitleImportService.ImportTranslations))
                  .ToList()
                  .ForEach(i => captured.AddRange((List<UserTitleImportRequest>)i.Arguments[0]));
            return captured;
        }

        [Test]
        public void should_keep_region_only_when_its_tag_is_a_configured_variant()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));
            _rows.Add(Row("Amélie Poulain", "FR"));
            _rows.Add(Row("Le destin fabuleux", "BE", "fr"));

            var candidates = Subject.BuildCandidates(_movie);

            candidates.Translations.Should().HaveCount(3);
            candidates.Translations[0].Region.Should().Be("CA");
            candidates.Translations[0].Language.Should().Be("fr");
            candidates.Translations[1].Region.Should().BeNull("fr-FR is not in Regional Translation Variants");
            candidates.Translations[2].Region.Should().BeNull();
        }

        [Test]
        public void should_use_row_language_when_present_and_first_configured_language_otherwise()
        {
            _rows.Add(Row("Amelie in Canada", "CA", "en"));
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            var candidates = Subject.BuildCandidates(_movie);

            candidates.Translations[0].Language.Should().Be("en");
            candidates.Translations[0].Region.Should().Be("CA", "en-CA is a configured variant");
            candidates.Translations[1].Language.Should().Be("fr");
        }

        [Test]
        public void should_skip_titles_the_movie_already_has_using_loose_normalisation()
        {
            _alternativeTitles.Add(new AlternativeTitle("Amelie de Montmartre", SourceType.Tmdb));
            _translations.Add(new MovieTranslation { Title = "AMÉLIE DE MONTMARTRE!", Language = Language.French });

            _rows.Add(Row("Amélie de Montmartre", "CA"));
            _rows.Add(Row("Amelie", "US"));
            _rows.Add(Row("Le fabuleux destin d'Amelie Poulain", "FR"));
            _rows.Add(Row("Amélie Poulain", "FR"));

            var candidates = Subject.BuildCandidates(_movie);

            candidates.AlternativeTitles.Select(t => t.Title).Should().Equal("Amélie Poulain");
            candidates.Translations.Select(t => t.Title).Should().Equal("Amélie Poulain");
        }

        [Test]
        public void should_keep_alt_title_and_translation_lists_independent()
        {
            _alternativeTitles.Add(new AlternativeTitle("Amélie de Montmartre", SourceType.User));

            _rows.Add(Row("Amélie de Montmartre", "CA"));

            var candidates = Subject.BuildCandidates(_movie);

            candidates.AlternativeTitles.Should().BeEmpty();
            candidates.Translations.Select(t => t.Title).Should().Equal("Amélie de Montmartre");
        }

        [Test]
        public void should_dedupe_dataset_rows_by_normalised_title_first_wins()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));
            _rows.Add(Row("Amelie de Montmartre", "FR"));

            var candidates = Subject.BuildCandidates(_movie);

            candidates.Translations.Should().HaveCount(1);
            candidates.Translations[0].Title.Should().Be("Amélie de Montmartre");
            candidates.Translations[0].Region.Should().Be("CA");
        }

        [Test]
        public void should_import_into_both_pipelines_as_one_request_per_movie()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));
            _rows.Add(Row("Amélie Poulain", "FR"));

            var summary = Subject.SyncMovie(_movie);

            summary.MoviesChecked.Should().Be(1);
            summary.MoviesWithCandidates.Should().Be(1);
            summary.AlternativeTitles.TitlesAdded.Should().Be(2);
            summary.Translations.TitlesAdded.Should().Be(2);

            var request = AlternativeRequests().Single();
            request.TmdbId.Should().Be(194);
            request.ImdbId.Should().Be("tt0211915");
            request.MovieTitle.Should().Be("Amélie");
            request.Year.Should().Be(2001);
            request.Titles.Select(t => t.Title).Should().Equal("Amélie de Montmartre", "Amélie Poulain");

            TranslationRequests().Single().Titles.Select(t => t.Region).Should().Equal("CA", null);
        }

        [Test]
        public void should_not_call_importers_when_nothing_is_missing()
        {
            _rows.Add(Row("Amélie", "CA"));

            Subject.SyncMovie(_movie);

            Mocker.GetMock<IUserTitleImportService>().Verify(s => s.ImportAlternativeTitles(It.IsAny<List<UserTitleImportRequest>>()), Times.Never());
            Mocker.GetMock<IUserTitleImportService>().Verify(s => s.ImportTranslations(It.IsAny<List<UserTitleImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImdbTitleProviderEnabled).Returns(false);
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            Subject.SyncMovie(_movie);
            Subject.SyncAll();

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.GetTitles(It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IUserTitleImportService>().Verify(s => s.ImportTranslations(It.IsAny<List<UserTitleImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_do_nothing_when_index_is_missing()
        {
            Mocker.GetMock<IImdbAkasDatabase>().SetupGet(d => d.Exists).Returns(false);
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            Subject.SyncMovie(_movie);

            Mocker.GetMock<IUserTitleImportService>().Verify(s => s.ImportTranslations(It.IsAny<List<UserTitleImportRequest>>()), Times.Never());
        }

        [Test]
        public void should_skip_movies_without_imdb_id()
        {
            _movie.ImdbId = null;

            Subject.SyncMovie(_movie);
            Subject.SyncAll().MoviesChecked.Should().Be(0);

            Mocker.GetMock<IImdbAkasDatabase>().Verify(d => d.GetTitles(It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_sync_all_library_movies_with_an_imdb_id()
        {
            var other = new Movie
            {
                Id = 2,
                TmdbId = 238,
                MovieMetadata = new MovieMetadata { Id = 20, TmdbId = 238, ImdbId = "tt0068646", Title = "The Godfather", Year = 1972 }
            };

            Mocker.GetMock<IMovieService>().Setup(s => s.GetAllMovies()).Returns(new List<Movie> { _movie, other });
            Mocker.GetMock<IImdbAkasDatabase>().Setup(d => d.GetTitles("tt0068646")).Returns(new List<ImdbAkasRow> { new ImdbAkasRow { Tconst = "tt0068646", Title = "Le Parrain", Region = "CA" } });
            Mocker.GetMock<IAlternativeTitleService>().Setup(s => s.GetAllTitlesForMovieMetadata(20)).Returns(new List<AlternativeTitle>());
            Mocker.GetMock<IMovieTranslationService>().Setup(s => s.GetAllTranslationsForMovieMetadata(20)).Returns(new List<MovieTranslation>());

            _rows.Add(Row("Amélie", "CA"));

            var summary = Subject.SyncAll();

            summary.MoviesChecked.Should().Be(2);
            summary.MoviesWithCandidates.Should().Be(1);

            AlternativeRequests().Single().ImdbId.Should().Be("tt0068646");
            TranslationRequests().Single().Titles.Single().Region.Should().Be("CA");
        }

        [Test]
        public void should_sync_on_movie_added_event()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            Subject.HandleAsync(new MovieAddedEvent(_movie));

            AlternativeRequests().Should().HaveCount(1);
            TranslationRequests().Should().HaveCount(1);
        }

        [Test]
        public void should_sync_on_movie_updated_event()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            Subject.HandleAsync(new MovieUpdatedEvent(_movie));

            TranslationRequests().Should().HaveCount(1);
        }

        [Test]
        public void should_swallow_errors_raised_while_handling_events()
        {
            _rows.Add(Row("Amélie de Montmartre", "CA"));

            Mocker.GetMock<IUserTitleImportService>()
                  .Setup(s => s.ImportAlternativeTitles(It.IsAny<List<UserTitleImportRequest>>()))
                  .Throws(new InvalidOperationException("boom"));

            Subject.HandleAsync(new MovieAddedEvent(_movie));

            ExceptionVerification.ExpectedErrors(1);
        }

        [TestCase("fr", "CA", "fr-CA,en-CA", true)]
        [TestCase("fr", "ca", "fr-CA", true)]
        [TestCase("fr", "FR", "fr-CA", false)]
        [TestCase("fr", null, "fr-CA", false)]
        [TestCase(null, "CA", "fr-CA", false)]
        [TestCase("fr", "CA", "", false)]
        public void should_apply_region_rule(string language, string region, string variants, bool expected)
        {
            ImdbTitleSyncService.KeepRegion(language, region, ImdbTitleSyncService.ParseVariants(variants)).Should().Be(expected);
        }
    }
}
