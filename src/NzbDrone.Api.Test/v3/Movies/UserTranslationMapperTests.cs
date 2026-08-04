using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using Radarr.Api.V3.Movies;

namespace NzbDrone.Api.Test.v3.Movies;

[Parallelizable(ParallelScope.All)]
public class UserTranslationMapperTests
{
    [TestCase(null, null, "fr")]
    [TestCase(null, "CA", "fr-ca")]
    [TestCase(null, "FR", "fr-fr")]
    [TestCase("fr", "BE", "fr-be")]
    [TestCase("de", "AT", "de-at")]
    [TestCase("es", null, "es")]
    public void Map_builds_lowercase_tag_from_language_and_region(string language, string region, string expectedTag)
    {
        var result = UserTranslationMapper.Map(new UserAlternativeTitleImportEntryResource
        {
            Title = "Un Titre",
            Language = language,
            Region = region
        });

        result.Should().NotBeNull();
        result.RegionalLanguage.Should().Be(expectedTag);
    }

    [Test]
    public void Map_defaults_language_to_french()
    {
        var result = UserTranslationMapper.Map(new UserAlternativeTitleImportEntryResource { Title = "Un Titre", Region = "CA" });

        result.Language.Should().Be(Language.French);
    }

    [Test]
    public void Map_resolves_explicit_language()
    {
        var result = UserTranslationMapper.Map(new UserAlternativeTitleImportEntryResource { Title = "Ein Titel", Language = "de", Region = "AT" });

        result.Language.Should().Be(Language.German);
    }

    [Test]
    public void Map_returns_null_for_unknown_language_code()
    {
        var result = UserTranslationMapper.Map(new UserAlternativeTitleImportEntryResource { Title = "Titre", Language = "zz" });

        result.Should().BeNull();
    }

    [Test]
    public void Map_sets_clean_title()
    {
        var result = UserTranslationMapper.Map(new UserAlternativeTitleImportEntryResource { Title = "Amélie de Montmartre", Region = "CA" });

        result.CleanTitle.Should().Be("ameliedemontmartre");
    }
}
