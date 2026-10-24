using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionMatcherFixture : CoreTest
    {
        private Movie _default;
        private Movie _extended;
        private Movie _directorsCut;
        private Movie _ulysses;
        private List<Movie> _rows;

        [SetUp]
        public void Setup()
        {
            _default = Row(1, "");
            _extended = Row(2, "Extended", "extended", "extended cut", "extended edition");
            _directorsCut = Row(3, "Director's Cut", "director's cut", "directors cut", "director's edition", "dc");
            _ulysses = Row(4, "Ulysses Cut", "ulysses cut");
            _ulysses.Monitored = false;

            _rows = new List<Movie> { _default, _extended, _directorsCut, _ulysses };
        }

        private static Movie Row(int id, string edition, params string[] aliases)
        {
            return new Movie
            {
                Id = id,
                MovieMetadataId = 99,
                Monitored = true,
                Edition = edition,
                EditionAliases = aliases.ToList()
            };
        }

        [TestCase("Ulysses Cut")]
        [TestCase("ulysses.cut")]
        [TestCase("ULYSSES-CUT")]
        public void ulysses_cut_should_match_its_row(string parsed)
        {
            EditionMatcher.RowMatchesEdition(_ulysses, parsed).Should().BeTrue();
            EditionMatcher.RowMatchesEdition(_extended, parsed).Should().BeFalse();
        }

        [TestCase("Director's Edition")]
        [TestCase("Directors Cut")]
        [TestCase("Director’s Cut")]
        [TestCase("DC")]
        public void directors_edition_should_match_directors_cut(string parsed)
        {
            EditionMatcher.RowMatchesEdition(_directorsCut, parsed).Should().BeTrue();
        }

        [Test]
        public void multi_word_parsed_edition_should_match_a_contained_alias()
        {
            EditionMatcher.RowMatchesEdition(_extended, "Extended Remastered").Should().BeTrue();
            EditionMatcher.RowMatchesEdition(_extended, "Remastered Extended Cut").Should().BeTrue();
        }

        [Test]
        public void single_word_parsed_edition_should_only_match_exactly()
        {
            EditionMatcher.RowMatchesEdition(_extended, "Remastered").Should().BeFalse();
            EditionMatcher.RowMatchesEdition(_extended, "Extendedx").Should().BeFalse();
        }

        [Test]
        public void containment_should_be_whole_word()
        {
            var master = Row(5, "Master");

            EditionMatcher.RowMatchesEdition(master, "Extended Remastered").Should().BeFalse();
            EditionMatcher.RowMatchesEdition(master, "Extended Master").Should().BeTrue();
        }

        [Test]
        public void row_name_should_match_without_aliases()
        {
            var imax = Row(6, "IMAX");

            EditionMatcher.RowMatchesEdition(imax, "imax").Should().BeTrue();
            EditionMatcher.RowMatchesEdition(imax, "IMAX Edition").Should().BeTrue();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("  ")]
        public void empty_parsed_edition_should_never_match_an_edition_row(string parsed)
        {
            EditionMatcher.RowMatchesEdition(_extended, parsed).Should().BeFalse();
            EditionMatcher.RowMatchesEdition(_ulysses, parsed).Should().BeFalse();
        }

        [Test]
        public void default_row_should_never_match()
        {
            EditionMatcher.RowMatchesEdition(_default, "").Should().BeFalse();
            EditionMatcher.RowMatchesEdition(_default, "Extended").Should().BeFalse();
            EditionMatcher.RowMatchesEdition(null, "Extended").Should().BeFalse();
        }

        [Test]
        public void null_aliases_should_be_tolerated()
        {
            var row = Row(7, "Unrated");
            row.EditionAliases = null;

            row.EditionAliases.Should().NotBeNull();
            EditionMatcher.RowMatchesEdition(row, "Unrated").Should().BeTrue();
        }

        [Test]
        public void select_row_should_pick_the_matching_edition_row()
        {
            EditionMatcher.SelectRow(_rows, "Extended Cut").Should().BeSameAs(_extended);
            EditionMatcher.SelectRow(_rows, "Director's Edition").Should().BeSameAs(_directorsCut);
        }

        [Test]
        public void select_row_should_pick_an_unmonitored_edition_row()
        {
            _ulysses.Monitored.Should().BeFalse();

            EditionMatcher.SelectRow(_rows, "Ulysses Cut").Should().BeSameAs(_ulysses);
        }

        [TestCase(null)]
        [TestCase("")]
        public void select_row_should_fall_back_to_the_default_row_for_untagged(string parsed)
        {
            EditionMatcher.SelectRow(_rows, parsed).Should().BeSameAs(_default);
        }

        [Test]
        public void select_row_should_fall_back_to_the_default_row_for_unknown_editions()
        {
            EditionMatcher.SelectRow(_rows, "Unrated").Should().BeSameAs(_default);
        }

        [Test]
        public void select_row_should_return_null_without_a_default_row_or_match()
        {
            EditionMatcher.SelectRow(new List<Movie> { _extended }, "Unrated").Should().BeNull();
            EditionMatcher.SelectRow(new List<Movie>(), "Extended").Should().BeNull();
            EditionMatcher.SelectRow(null, "Extended").Should().BeNull();
        }

        [Test]
        public void select_row_should_not_depend_on_row_order()
        {
            var reversed = Enumerable.Reverse(_rows).ToList();

            EditionMatcher.SelectRow(reversed, "Extended").Should().BeSameAs(_extended);
            EditionMatcher.SelectRow(reversed, "").Should().BeSameAs(_default);
        }

        [Test]
        public void is_claimed_by_sibling_should_be_true_when_a_sibling_names_the_edition()
        {
            var siblings = new List<Movie> { _extended, _directorsCut, _ulysses };

            EditionMatcher.IsClaimedBySibling(_default, siblings, "Extended Cut").Should().BeTrue();
            EditionMatcher.IsClaimedBySibling(_default, siblings, "ulysses cut").Should().BeTrue();
        }

        [Test]
        public void is_claimed_by_sibling_should_be_false_for_untagged_or_unknown()
        {
            var siblings = new List<Movie> { _extended, _directorsCut, _ulysses };

            EditionMatcher.IsClaimedBySibling(_default, siblings, "").Should().BeFalse();
            EditionMatcher.IsClaimedBySibling(_default, siblings, null).Should().BeFalse();
            EditionMatcher.IsClaimedBySibling(_default, siblings, "Unrated").Should().BeFalse();
            EditionMatcher.IsClaimedBySibling(_default, siblings, "Theatrical").Should().BeFalse();
        }

        [Test]
        public void is_claimed_by_sibling_should_ignore_the_default_row_itself_and_null_input()
        {
            EditionMatcher.IsClaimedBySibling(_default, _rows, "Extended").Should().BeTrue();
            EditionMatcher.IsClaimedBySibling(_default, new List<Movie> { _default }, "Extended").Should().BeFalse();
            EditionMatcher.IsClaimedBySibling(_default, null, "Extended").Should().BeFalse();
            EditionMatcher.IsClaimedBySibling(_default, new List<Movie>(), "Extended").Should().BeFalse();
        }
    }
}
