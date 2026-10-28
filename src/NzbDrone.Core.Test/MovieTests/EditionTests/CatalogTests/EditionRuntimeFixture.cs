// krzw(edition-runtime-catalog): runtime matching (direct, PAL both ways, ambiguity, unknown longer cut, tolerance)
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.Editions.Catalog;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests
{
    [TestFixture]
    public class EditionRuntimeFixture
    {
        private static EditionCatalogEntry Entry(string name, int runtime, int confidence = 70, EditionCatalogSource source = EditionCatalogSource.Imdb)
        {
            return new EditionCatalogEntry { MovieMetadataId = 1, Name = name, RuntimeMinutes = runtime, Confidence = confidence, Source = source };
        }

        // Fellowship of the Ring as IMDb lists it
        private static List<EditionCatalogEntry> Fellowship()
        {
            return new List<EditionCatalogEntry>
            {
                Entry("Theatrical", 178, 80),
                Entry("Extended", 208, 60),
                Entry("DVD Widescreen Edition", 171, 20)
            };
        }

        [TestCase(178.0, 178, 3, true)]
        [TestCase(181.0, 178, 3, true)]
        [TestCase(175.0, 178, 3, true)]
        [TestCase(181.5, 178, 3, false)]
        [TestCase(174.4, 178, 3, false)]
        [TestCase(178.0, 178, 0, true)]
        [TestCase(178.5, 178, 0, false)]
        public void matches_direct_within_inclusive_tolerance(double file, int catalog, int tolerance, bool expected)
        {
            EditionRuntime.Matches(file, catalog, tolerance).Should().Be(expected);
        }

        [Test]
        public void matches_a_pal_file_against_a_film_runtime()
        {
            // 178 min at 24 fps runs 170.9 min at 25 fps
            EditionRuntime.Matches(170.9, 178, 1).Should().BeTrue();
        }

        [Test]
        public void matches_a_film_file_against_a_pal_runtime()
        {
            // a catalog runtime measured on a PAL release (170.9 -> 171), film-speed file 178
            EditionRuntime.Matches(178, 171, 1).Should().BeTrue();
        }

        [Test]
        public void does_not_match_zero_or_negative_runtimes()
        {
            EditionRuntime.Matches(0, 120, 3).Should().BeFalse();
            EditionRuntime.Matches(120, 0, 3).Should().BeFalse();
        }

        [Test]
        public void unique_direct_match()
        {
            var match = EditionRuntime.Match(207.5, Fellowship(), 3);

            match.Kind.Should().Be(RuntimeMatchKind.Unique);
            match.Entry.Name.Should().Be("Extended");
            match.IsPal.Should().BeFalse();
            match.MatchedMinutes.Should().Be(207.5);
            match.FileMinutes.Should().Be(207.5);
        }

        [Test]
        public void unique_pal_match_reports_pal_and_the_de_paled_runtime()
        {
            // 208 min extended at 25 fps = 199.7 min
            var match = EditionRuntime.Match(199.7, Fellowship(), 3);

            match.Kind.Should().Be(RuntimeMatchKind.Unique);
            match.Entry.Name.Should().Be("Extended");
            match.IsPal.Should().BeTrue();
            match.MatchedMinutes.Should().BeApproximately(208.02, 0.01);
        }

        [Test]
        public void unique_reverse_pal_match_reports_pal()
        {
            var entries = new List<EditionCatalogEntry> { Entry("Theatrical", 96, 80) };

            var match = EditionRuntime.Match(100, entries, 0);

            match.Kind.Should().Be(RuntimeMatchKind.Unique);
            match.IsPal.Should().BeTrue();
            match.MatchedMinutes.Should().BeApproximately(96, 0.001);
        }

        [Test]
        public void two_cuts_within_tolerance_are_ambiguous()
        {
            // Star Trek II: 113 theatrical, 116 director's cut
            var entries = new List<EditionCatalogEntry> { Entry("Theatrical", 113, 80), Entry("Director's Cut", 116) };

            var match = EditionRuntime.Match(114.5, entries, 3);

            match.Kind.Should().Be(RuntimeMatchKind.Ambiguous);
            match.Entry.Should().BeNull();
            match.Candidates.Should().HaveCount(2);
            match.Candidates[0].Name.Should().Be("Theatrical");
        }

        [Test]
        public void same_name_from_two_sources_is_one_candidate_and_the_stronger_source_wins()
        {
            var entries = new List<EditionCatalogEntry>
            {
                Entry("Extended", 208, 60, EditionCatalogSource.Imdb),
                Entry("extended", 209, 90, EditionCatalogSource.Library)
            };

            var match = EditionRuntime.Match(208, entries, 3);

            match.Kind.Should().Be(RuntimeMatchKind.Unique);
            match.Entry.Source.Should().Be(EditionCatalogSource.Library);
        }

        [Test]
        public void a_file_more_than_eight_minutes_longer_than_every_cut_is_an_unknown_longer_cut()
        {
            var match = EditionRuntime.Match(196, new List<EditionCatalogEntry> { Entry("Theatrical", 163, 80) }, 3);

            match.Kind.Should().Be(RuntimeMatchKind.UnknownLonger);
            match.Entry.Should().BeNull();
            match.Candidates.Should().ContainSingle();
        }

        [Test]
        public void exactly_eight_minutes_longer_is_not_an_unknown_longer_cut()
        {
            EditionRuntime.Match(171, new List<EditionCatalogEntry> { Entry("Theatrical", 163, 80) }, 0).Kind.Should().Be(RuntimeMatchKind.None);
        }

        [Test]
        public void shorter_than_every_cut_without_a_match_is_none()
        {
            EditionRuntime.Match(90, Fellowship(), 3).Kind.Should().Be(RuntimeMatchKind.None);
        }

        [Test]
        public void low_confidence_entries_never_match()
        {
            // 171 is the "DVD Widescreen Edition" transfer label (confidence 20)
            var match = EditionRuntime.Match(171, new List<EditionCatalogEntry> { Entry("DVD Widescreen Edition", 171, 20) }, 3);

            match.Kind.Should().Be(RuntimeMatchKind.None);
        }

        [Test]
        public void tolerance_boundary_is_inclusive_for_match()
        {
            // tolerance 1 keeps the direct band (119-121) apart from the PAL bands (114.2-116.2, 124-126)
            var entries = new List<EditionCatalogEntry> { Entry("Theatrical", 120, 80) };

            EditionRuntime.Match(121, entries, 1).Kind.Should().Be(RuntimeMatchKind.Unique);
            EditionRuntime.Match(119, entries, 1).Kind.Should().Be(RuntimeMatchKind.Unique);
            EditionRuntime.Match(121.01, entries, 1).Kind.Should().Be(RuntimeMatchKind.None);
            EditionRuntime.Match(118.99, entries, 1).Kind.Should().Be(RuntimeMatchKind.None);
        }

        [Test]
        public void no_entries_or_no_runtime_is_none()
        {
            EditionRuntime.Match(120, new List<EditionCatalogEntry>(), 3).Kind.Should().Be(RuntimeMatchKind.None);
            EditionRuntime.Match(120, null, 3).Kind.Should().Be(RuntimeMatchKind.None);
            EditionRuntime.Match(0, Fellowship(), 3).Kind.Should().Be(RuntimeMatchKind.None);
        }
    }
}
