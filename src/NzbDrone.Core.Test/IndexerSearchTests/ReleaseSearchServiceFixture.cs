using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.IndexerSearch;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.IndexerSearchTests
{
        public class ReleaseSearchServiceFixture : CoreTest<ReleaseSearchService>
    {
        private Mock<IIndexer> _mockIndexer;
        private Movie _movie;

        [SetUp]
        public void SetUp()
        {
            _mockIndexer = Mocker.GetMock<IIndexer>();
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition { Id = 1 });
            _mockIndexer.SetupGet(s => s.SupportsSearch).Returns(true);

            Mocker.GetMock<IIndexerFactory>()
                  .Setup(s => s.AutomaticSearchEnabled(true))
                  .Returns(new List<IIndexer> { _mockIndexer.Object });

            Mocker.GetMock<IMakeDownloadDecision>()
                .Setup(s => s.GetSearchDecision(It.IsAny<List<Parser.Model.ReleaseInfo>>(), It.IsAny<SearchCriteriaBase>()))
                .Returns(new List<DownloadDecision>());

            _movie = Builder<Movie>.CreateNew()
                .With(v => v.Monitored = true)
                .Build();

            Mocker.GetMock<IMovieService>()
                .Setup(v => v.GetMovie(_movie.Id))
                .Returns(_movie);

            Mocker.GetMock<IMovieTranslationService>()
                .Setup(s => s.GetAllTranslationsForMovieMetadata(It.IsAny<int>()))
                .Returns(new List<MovieTranslation>());
        }

        private List<SearchCriteriaBase> WatchForSearchCriteria()
        {
            var result = new List<SearchCriteriaBase>();

            _mockIndexer.Setup(v => v.Fetch(It.IsAny<MovieSearchCriteria>()))
                .Callback<MovieSearchCriteria>(s => result.Add(s))
                .Returns(Task.FromResult<IList<Parser.Model.ReleaseInfo>>(new List<Parser.Model.ReleaseInfo>()));

            return result;
        }

        [Test]
        public async Task Tags_IndexerTags_MovieNoTags_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 3 }
            });

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }

        [Test]
        public async Task Tags_IndexerNoTags_MovieTags_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1
            });

            _movie = Builder<Movie>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3 })
                .Build();

            Mocker.GetMock<IMovieService>()
                .Setup(v => v.GetMovie(_movie.Id))
                .Returns(_movie);

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndMovieTagsMatch_IndexerIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _movie = Builder<Movie>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 3, 4, 5 })
                .Build();

            Mocker.GetMock<IMovieService>()
                .Setup(v => v.GetMovie(_movie.Id))
                .Returns(_movie);

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
        }

        [Test]
        public async Task Tags_IndexerAndMovieTagsMismatch_IndexerNotIncluded()
        {
            _mockIndexer.SetupGet(s => s.Definition).Returns(new IndexerDefinition
            {
                Id = 1,
                Tags = new HashSet<int> { 1, 2, 3 }
            });

            _movie = Builder<Movie>.CreateNew()
                .With(v => v.Monitored = true)
                .With(v => v.Tags = new HashSet<int> { 4, 5, 6 })
                .Build();

            Mocker.GetMock<IMovieService>()
                .Setup(v => v.GetMovie(_movie.Id))
                .Returns(_movie);

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(0);
        }

        private void GivenFrenchRegionalTranslations(RegionalTranslationSearchMode mode, string variants)
        {
            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.RegionalTranslationSearchMode)
                .Returns(mode);

            Mocker.GetMock<IConfigService>()
                .SetupGet(s => s.RegionalTranslationVariants)
                .Returns(variants);

            Mocker.GetMock<IQualityProfileService>()
                .Setup(s => s.GetAcceptableLanguages(It.IsAny<int>()))
                .Returns(new List<Language> { Language.French });

            Mocker.GetMock<IMovieTranslationService>()
                .Setup(s => s.GetAllTranslationsForMovieMetadata(It.IsAny<int>()))
                .Returns(new List<MovieTranslation>
                {
                    new MovieTranslation { Title = "Titre France", CleanTitle = "titrefrance", Language = Language.French, RegionalLanguage = "fr" },
                    new MovieTranslation { Title = "Titre Quebec", CleanTitle = "titrequebec", Language = Language.French, RegionalLanguage = "fr-ca" },
                    new MovieTranslation { Title = "Titre Belgique", CleanTitle = "titrebelgique", Language = Language.French, RegionalLanguage = "fr-be" }
                });
        }

        [Test]
        public async Task RegionalVariants_OnePerRegion_restricts_to_configured_variants()
        {
            GivenFrenchRegionalTranslations(RegionalTranslationSearchMode.OnePerRegion, "fr-CA");

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
            criteria[0].SceneTitles.Should().Contain("Titre France");
            criteria[0].SceneTitles.Should().Contain("Titre Quebec");
            criteria[0].SceneTitles.Should().NotContain("Titre Belgique");
        }

        [Test]
        public async Task RegionalVariants_empty_list_means_no_restriction()
        {
            GivenFrenchRegionalTranslations(RegionalTranslationSearchMode.OnePerRegion, string.Empty);

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
            criteria[0].SceneTitles.Should().Contain("Titre France");
            criteria[0].SceneTitles.Should().Contain("Titre Quebec");
            criteria[0].SceneTitles.Should().Contain("Titre Belgique");
        }

        [Test]
        public async Task RegionalVariants_standard_mode_is_unaffected()
        {
            GivenFrenchRegionalTranslations(RegionalTranslationSearchMode.Standard, "fr-CA");

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);

            // Standard mode collapses to one title per language; the variants list must not change that.
            var frenchTitles = new[] { "Titre France", "Titre Quebec", "Titre Belgique" };
            criteria[0].SceneTitles.Count(t => frenchTitles.Contains(t)).Should().Be(1);
        }

        [Test]
        public async Task RegionalVariants_allTitles_restricts_regional_titles_but_keeps_bare_language()
        {
            GivenFrenchRegionalTranslations(RegionalTranslationSearchMode.AllTitles, "fr-CA");

            var allCriteria = WatchForSearchCriteria();

            await Subject.MovieSearch(_movie, true, false);

            var criteria = allCriteria.OfType<MovieSearchCriteria>().ToList();

            criteria.Count.Should().Be(1);
            criteria[0].SceneTitles.Should().Contain("Titre France");
            criteria[0].SceneTitles.Should().Contain("Titre Quebec");
            criteria[0].SceneTitles.Should().NotContain("Titre Belgique");
        }
    }
}
