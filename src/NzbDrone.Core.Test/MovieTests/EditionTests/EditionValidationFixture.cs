using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.EditionTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionValidationFixture : CoreTest
    {
        private List<Movie> _rows;

        [SetUp]
        public void Setup()
        {
            _rows = new List<Movie>
            {
                new Movie { Id = 1, MovieMetadataId = 7, Edition = "" },
                new Movie { Id = 2, MovieMetadataId = 7, Edition = "Extended", EditionAliases = new List<string> { "extended", "extended cut" } },
                new Movie { Id = 3, MovieMetadataId = 7, Edition = "Director's Cut", EditionAliases = new List<string> { "directors cut", "dc" } }
            };
        }

        [TestCase(null, EditionValidation.NameRequiredMessage)]
        [TestCase("", EditionValidation.NameRequiredMessage)]
        [TestCase("  ", EditionValidation.NameRequiredMessage)]
        [TestCase("123456789012345678901234567890123", "Edition name must be at most 32 characters")]
        public void should_report_name_problems(string name, string expected)
        {
            EditionValidation.NameProblem(name).Should().Be(expected);
        }

        [TestCase("Extended")]
        [TestCase("12345678901234567890123456789012")]
        [TestCase("  12345678901234567890123456789012  ")]
        public void should_accept_valid_names(string name)
        {
            EditionValidation.NameProblem(name).Should().BeNull();
        }

        [TestCase("Extended")]
        [TestCase("extended")]
        [TestCase("Extended.Cut")]
        [TestCase("DC")]
        [TestCase("Directors Cut")]
        public void should_detect_a_collision_with_an_edition_rows_name_or_alias(string name)
        {
            EditionValidation.NameCollides(_rows, name).Should().BeTrue();
        }

        [TestCase("Theatrical")]
        [TestCase("Extended Remastered")]
        [TestCase("")]
        [TestCase(null)]
        public void should_not_report_a_collision_for_new_names(string name)
        {
            EditionValidation.NameCollides(_rows, name).Should().BeFalse();
        }

        [Test]
        public void should_ignore_the_row_being_renamed()
        {
            EditionValidation.NameCollides(_rows, "Extended Cut", excludeMovieId: 2).Should().BeFalse();
            EditionValidation.NameCollides(_rows, "Extended Cut", excludeMovieId: 3).Should().BeTrue();
        }

        [Test]
        public void should_handle_null_rows()
        {
            EditionValidation.NameCollides(null, "Extended").Should().BeFalse();
        }

        [Test]
        public void should_build_normalized_distinct_aliases_with_the_name_first()
        {
            EditionValidation.BuildAliases("Director's Cut", new[] { "DC", "directors.cut", " ", null, "Director's Edition", "dc" })
                             .Should().Equal("directors cut", "dc", "directors edition");
        }

        [Test]
        public void should_build_aliases_from_the_name_alone_when_none_are_given()
        {
            EditionValidation.BuildAliases("IMAX", null).Should().Equal("imax");
        }
    }
}
