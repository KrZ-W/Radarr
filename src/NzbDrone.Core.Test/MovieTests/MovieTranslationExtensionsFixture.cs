using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests
{
    [TestFixture]
    public class MovieTranslationExtensionsFixture : CoreTest
    {
        private List<MovieTranslation> GivenFrenchTranslations()
        {
            // Deliberately ordered so that naive FirstOrDefault would pick fr-BE
            return new List<MovieTranslation>
            {
                new MovieTranslation { Title = "Titre Belgique", Language = Language.French, RegionalLanguage = "fr-be" },
                new MovieTranslation { Title = "Titre Quebec", Language = Language.French, RegionalLanguage = "fr-ca" },
                new MovieTranslation { Title = "Titre France", Language = Language.French, RegionalLanguage = "fr" }
            };
        }

        [Test]
        public void should_prefer_configured_variant_first()
        {
            var ordered = GivenFrenchTranslations().OrderByRegionalPreference("fr-CA").ToList();

            ordered[0].Title.Should().Be("Titre Quebec");
            ordered[1].Title.Should().Be("Titre France");
            ordered[2].Title.Should().Be("Titre Belgique");
        }

        [Test]
        public void should_prefer_bare_language_when_no_variants_configured()
        {
            var ordered = GivenFrenchTranslations().OrderByRegionalPreference(string.Empty).ToList();

            ordered[0].Title.Should().Be("Titre France");
            ordered[1].Title.Should().Be("Titre Belgique");
            ordered[2].Title.Should().Be("Titre Quebec");
        }

        [Test]
        public void should_respect_variant_list_order()
        {
            var ordered = GivenFrenchTranslations().OrderByRegionalPreference("fr-BE, fr-CA").ToList();

            ordered[0].Title.Should().Be("Titre Belgique");
            ordered[1].Title.Should().Be("Titre Quebec");
            ordered[2].Title.Should().Be("Titre France");
        }

        [Test]
        public void should_handle_null_csv_and_null_regional_codes()
        {
            var translations = new List<MovieTranslation>
            {
                new MovieTranslation { Title = "No Code", Language = Language.French, RegionalLanguage = null },
                new MovieTranslation { Title = "Titre France", Language = Language.French, RegionalLanguage = "fr" }
            };

            var ordered = translations.OrderByRegionalPreference(null).ToList();

            // Null codes sort like bare-language entries; no exception either way
            ordered.Should().HaveCount(2);
            ordered.Select(t => t.Title).Should().Contain("Titre France");
        }
    }
}
