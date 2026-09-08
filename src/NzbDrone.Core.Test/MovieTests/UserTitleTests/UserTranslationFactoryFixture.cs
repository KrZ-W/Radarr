using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies.UserTitles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.UserTitleTests
{
    [TestFixture]
    public class UserTranslationFactoryFixture : CoreTest
    {
        [TestCase("fr", null, "fr")]
        [TestCase("fr", "CA", "fr-ca")]
        [TestCase("fr", "FR", "fr-fr")]
        [TestCase("fr", "BE", "fr-be")]
        [TestCase("de", "AT", "de-at")]
        [TestCase("es", null, "es")]
        [TestCase("fra", "CA", "fr-ca")]
        [TestCase("deu", null, "de")]
        [TestCase("fr-CA", null, "fr-ca")]
        [TestCase("fr-CA", "CA", "fr-ca")]
        [TestCase("FR-ca", "BE", "fr-be")]
        public void should_build_lowercase_tag_from_language_and_region(string language, string region, string expectedTag)
        {
            var result = UserTranslationFactory.Create("Un Titre", language, region);

            result.Should().NotBeNull();
            result.RegionalLanguage.Should().Be(expectedTag);
        }

        [Test]
        public void should_resolve_language()
        {
            UserTranslationFactory.Create("Ein Titel", "de", "AT").Language.Should().Be(Language.German);
            UserTranslationFactory.Create("Un Titre", "fra", null).Language.Should().Be(Language.French);
        }

        [TestCase("zz")]
        [TestCase(null)]
        [TestCase("")]
        public void should_return_null_for_unknown_or_missing_language(string language)
        {
            UserTranslationFactory.Create("Titre", language, "CA").Should().BeNull();
        }

        [Test]
        public void should_set_clean_title()
        {
            UserTranslationFactory.Create("Amélie de Montmartre", "fr", "CA").CleanTitle.Should().Be("ameliedemontmartre");
        }
    }
}
