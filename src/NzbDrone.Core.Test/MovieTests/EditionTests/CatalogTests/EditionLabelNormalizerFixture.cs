// krzw(edition-runtime-catalog): IMDb / Wikidata label table (plan section 3.1, every label of the section 2 distribution)
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Editions.Catalog;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionLabelNormalizerFixture
    {
        // Section 2 distribution: cut names
        [TestCase("extended", "Extended", 70)]
        [TestCase("director's cut", "Director's Cut", 70)]
        [TestCase("unrated", "Unrated", 70)]
        [TestCase("special edition", "Special Edition", 70)]
        [TestCase("extended edition", "Extended", 70)]
        [TestCase("extended cut", "Extended", 70)]
        [TestCase("unrated director's cut", "Director's Cut", 70)]
        [TestCase("workprint", "Workprint", 70)]
        [TestCase("uncut", "Uncut", 70)]

        // Other cut spellings of the table
        [TestCase("extended version", "Extended", 70)]
        [TestCase("Director's Cut", "Director's Cut", 70)]
        [TestCase("directors cut", "Director's Cut", 70)]
        [TestCase("final cut", "Final Cut", 70)]
        [TestCase("theatrical", "Theatrical", 80)]
        [TestCase("theatrical version", "Theatrical", 80)]
        [TestCase("original", "Theatrical", 60)]
        [TestCase("original cut", "Theatrical", 60)]

        // Embellished labels keep the preset, one notch lower (the full text is the provider's attribute)
        [TestCase("director's cut roadshow", "Director's Cut", 60)]
        [TestCase("Special DVD Extended Edition", "Extended", 60)]
        [TestCase("Blu Ray Extended Edition", "Extended", 60)]
        [TestCase("Workprint Version", "Workprint", 70)]
        [TestCase("theatrical version (USA)", "Theatrical", 60)]
        public void maps_cut_labels_to_presets(string label, string preset, int confidence)
        {
            var (presetName, mappedConfidence) = EditionLabelNormalizer.Map(label);

            presetName.Should().Be(preset);
            mappedConfidence.Should().Be(confidence);
        }

        // Section 2 distribution: transfer and market labels, plus the others of the table
        [TestCase("cut")]
        [TestCase("tv")]
        [TestCase("dvd")]
        [TestCase("approximate")]
        [TestCase("international")]
        [TestCase("québec")]
        [TestCase("video")]
        [TestCase("blu-ray")]
        [TestCase("edited")]
        [TestCase("heavily cut")]
        [TestCase("re-release")]
        [TestCase("Germany")]
        [TestCase("UK")]
        [TestCase("DVD Widescreen Edition")]
        public void keeps_transfer_and_market_labels_raw_with_a_confidence_that_never_drives_a_verdict(string label)
        {
            var (presetName, confidence) = EditionLabelNormalizer.Map(label);

            presetName.Should().Be(label);
            confidence.Should().Be(EditionLabelNormalizer.TransferConfidence);
            confidence.Should().BeLessThan(EditionRuntime.MinimumConfidence);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void unlabelled_runtime_is_theatrical(string label)
        {
            EditionLabelNormalizer.Map(label).Should().Be(("Theatrical", 80));
        }

        [Test]
        public void trims_raw_labels()
        {
            EditionLabelNormalizer.Map("  tv ").PresetName.Should().Be("tv");
        }

        [Test]
        public void cut_presets_are_strong_enough_to_drive_a_verdict()
        {
            EditionLabelNormalizer.Map("director's cut roadshow").Confidence.Should().BeGreaterOrEqualTo(EditionRuntime.MinimumConfidence);
            EditionLabelNormalizer.Map("original").Confidence.Should().BeGreaterOrEqualTo(EditionRuntime.MinimumConfidence);
        }
    }
}
