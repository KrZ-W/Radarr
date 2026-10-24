using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.DecisionEngine;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionGateSpecificationFixture : CoreTest<EditionGateSpecification>
    {
        private const int MetadataId = 42;

        private Movie _default;
        private Movie _extended;
        private Movie _directorsCut;

        [SetUp]
        public void Setup()
        {
            _default = new Movie
            {
                Id = 1,
                MovieMetadataId = MetadataId,
                Monitored = true,
                Edition = "",
                EditionAliases = new List<string> { "Theatrical", "Theatrical Cut" }
            };

            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = MetadataId,
                Monitored = true,
                Edition = "Extended",
                EditionAliases = new List<string> { "Extended", "Extended Cut", "Extended Edition" }
            };

            _directorsCut = new Movie
            {
                Id = 3,
                MovieMetadataId = MetadataId,
                Monitored = false,
                Edition = "Director's Cut",
                EditionAliases = new List<string> { "Director's Cut", "Directors Cut", "Director's Edition", "DC" }
            };

            GivenSiblings(_default, _extended, _directorsCut);
            GivenSiblings(_extended, _default, _directorsCut);
            GivenSiblings(_directorsCut, _default, _extended);
        }

        private void GivenSiblings(Movie row, params Movie[] siblings)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(row.Id))
                  .Returns(siblings.ToList());
        }

        private static RemoteMovie GivenRelease(Movie movie, string edition)
        {
            return new RemoteMovie
            {
                Movie = movie,
                ParsedMovieInfo = new ParsedMovieInfo { Edition = edition }
            };
        }

        private void VerifySiblingQueries(Times times)
        {
            Mocker.GetMock<IMovieService>().Verify(s => s.GetEditionSiblings(It.IsAny<int>()), times);
        }

        [Test]
        public void should_be_a_permanent_rejection()
        {
            Subject.Type.Should().Be(RejectionType.Permanent);
        }

        // default row

        [Test]
        public void should_accept_untagged_release_for_default_row_without_querying_siblings()
        {
            Subject.IsSatisfiedBy(GivenRelease(_default, ""), new()).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        [Test]
        public void should_treat_null_edition_as_untagged_for_default_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_default, null), new()).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        [Test]
        public void should_accept_tagged_release_for_default_row_without_siblings()
        {
            GivenSiblings(_default);

            Subject.IsSatisfiedBy(GivenRelease(_default, "Extended"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_tagged_release_for_default_row_when_no_sibling_claims_it()
        {
            Subject.IsSatisfiedBy(GivenRelease(_default, "Remastered"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_theatrical_release_for_default_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_default, "Theatrical"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_release_for_default_row_when_sibling_claims_edition()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(_default, "Extended"), new());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release edition 'Extended' belongs to the Extended edition of this movie");
        }

        [Test]
        public void should_reject_release_for_default_row_when_sibling_alias_claims_edition()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(_default, "Director's Edition"), new());

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().Be("Release edition 'Director's Edition' belongs to the Director's Cut edition of this movie");
        }

        [Test]
        public void should_reject_release_for_default_row_when_multi_word_edition_names_sibling()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(_default, "Extended Remastered"), new());

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().Be("Release edition 'Extended Remastered' belongs to the Extended edition of this movie");
        }

        [Test]
        public void should_reject_release_for_default_row_even_when_claiming_sibling_is_unmonitored()
        {
            _directorsCut.Monitored.Should().BeFalse();

            Subject.IsSatisfiedBy(GivenRelease(_default, "DC"), new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_query_siblings_once_for_tagged_release_on_default_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_default, "Extended"), new());

            Mocker.GetMock<IMovieService>().Verify(s => s.GetEditionSiblings(_default.Id), Times.Once());
            VerifySiblingQueries(Times.Once());
        }

        // edition rows

        [Test]
        public void should_accept_release_naming_the_edition_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Extended"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_release_naming_an_alias_of_the_edition_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Extended Cut"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_release_whose_multi_word_edition_contains_the_edition_name()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Extended Remastered"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_match_edition_ignoring_case_punctuation_and_apostrophes()
        {
            Subject.IsSatisfiedBy(GivenRelease(_directorsCut, "DIRECTORS.CUT"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_matching_release_for_unmonitored_edition_row()
        {
            // monitoring is MonitoredMovieSpecification's call; the gate only judges the edition
            Subject.IsSatisfiedBy(GivenRelease(_directorsCut, "Director's Cut"), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_release_naming_another_edition_for_edition_row()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(_extended, "Director's Cut"), new());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release edition 'Director's Cut' does not match the Extended edition");
        }

        [Test]
        public void should_reject_release_with_unknown_edition_for_edition_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Remastered"), new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_untagged_release_for_edition_row_that_does_not_accept_untagged()
        {
            var decision = Subject.IsSatisfiedBy(GivenRelease(_extended, ""), new());

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(DownloadRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release has no edition tag and the Extended edition does not accept untagged releases");
        }

        [Test]
        public void should_treat_null_edition_as_untagged_for_edition_row()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, null), new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_untagged_release_for_edition_row_that_accepts_untagged()
        {
            _extended.EditionAcceptUntagged = true;

            Subject.IsSatisfiedBy(GivenRelease(_extended, ""), new()).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_mismatching_edition_when_edition_row_accepts_untagged()
        {
            _extended.EditionAcceptUntagged = true;

            Subject.IsSatisfiedBy(GivenRelease(_extended, "Director's Cut"), new()).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_not_query_siblings_for_edition_rows()
        {
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Extended"), new());
            Subject.IsSatisfiedBy(GivenRelease(_extended, "Director's Cut"), new());
            Subject.IsSatisfiedBy(GivenRelease(_extended, ""), new());

            VerifySiblingQueries(Times.Never());
        }
    }
}
