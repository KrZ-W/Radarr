using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionPresetsFixture : CoreTest
    {
        [Test]
        public void should_ship_the_spec_presets()
        {
            EditionPresets.All.Select(p => p.Name).Should().BeEquivalentTo(new[]
            {
                "Theatrical",
                "Extended",
                "Director's Cut",
                "Unrated",
                "Final Cut",
                "Ulysses Cut",
                "IMAX",
                "Criterion",
                "Remastered",
                "Special Edition",
                "Ultimate Edition",
                "Ultimate Cut",
                "Open Matte",
                "Uncut",
                "Anniversary Edition"
            });
        }

        [TestCase("Director's Cut", "director's edition")]
        [TestCase("Director's Cut", "directors cut")]
        [TestCase("Director's Cut", "dc")]
        [TestCase("Extended", "extended cut")]
        [TestCase("Extended", "extended edition")]
        [TestCase("Theatrical", "theatrical cut")]
        [TestCase("Criterion", "criterion collection")]
        [TestCase("Ulysses Cut", "ulysses cut")]
        public void should_carry_alias(string presetName, string alias)
        {
            EditionPresets.All.Single(p => p.Name == presetName).Aliases.Should().Contain(alias);
        }

        [Test]
        public void names_should_be_unique_after_normalization_and_fit_the_plex_limit()
        {
            var normalized = EditionPresets.All.Select(p => EditionNormalizer.Normalize(p.Name)).ToList();

            normalized.Should().OnlyHaveUniqueItems();
            normalized.Should().NotContain("");
            EditionPresets.All.Should().OnlyContain(p => p.Name.Length <= 32);
        }

        [Test]
        public void every_preset_should_match_its_own_name_through_its_aliases()
        {
            foreach (var preset in EditionPresets.All)
            {
                preset.Aliases.Should().NotBeEmpty(preset.Name);
                preset.Aliases.Select(EditionNormalizer.Normalize).Should().Contain(EditionNormalizer.Normalize(preset.Name), preset.Name);
            }
        }
    }
}
