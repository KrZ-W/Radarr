using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionGateImportSpecificationFixture : CoreTest<EditionGateImportSpecification>
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

        // Edition is what AggregateEdition resolved (Plex tag, download client title, folder or file name);
        // FileMovieInfo deliberately carries no edition so a test only passes when the gate reads LocalMovie.Edition.
        private static LocalMovie GivenFile(Movie movie, string edition)
        {
            return new LocalMovie
            {
                Path = @"C:\Test\Downloads\Movie.Title.2010.1080p.BluRay.x264-GROUP\Movie.Title.2010.1080p.BluRay.x264-GROUP.mkv".AsOsAgnostic(),
                Movie = movie,
                Edition = edition,
                FileMovieInfo = new ParsedMovieInfo { Edition = "" }
            };
        }

        private void VerifySiblingQueries(Times times)
        {
            Mocker.GetMock<IMovieService>().Verify(s => s.GetEditionSiblings(It.IsAny<int>()), times);
        }

        // skips

        [Test]
        public void should_skip_gate_when_user_picked_the_row()
        {
            var mismatchingEdition = GivenFile(_extended, "Director's Cut");
            mismatchingEdition.EditionOverride = true;

            var claimedBySibling = GivenFile(_default, "Extended");
            claimedBySibling.EditionOverride = true;

            var untaggedForEdition = GivenFile(_extended, "");
            untaggedForEdition.EditionOverride = true;

            Subject.IsSatisfiedBy(mismatchingEdition, null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(claimedBySibling, null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(untaggedForEdition, null).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        [Test]
        public void should_skip_gate_for_existing_files()
        {
            var claimedBySibling = GivenFile(_default, "Extended");
            claimedBySibling.ExistingFile = true;

            var untaggedForEdition = GivenFile(_extended, "");
            untaggedForEdition.ExistingFile = true;

            Subject.IsSatisfiedBy(claimedBySibling, null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(untaggedForEdition, null).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        // default row

        [Test]
        public void should_accept_untagged_file_for_default_row_without_querying_siblings()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, ""), null).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        [Test]
        public void should_treat_null_edition_as_untagged_for_default_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, null), null).Accepted.Should().BeTrue();

            VerifySiblingQueries(Times.Never());
        }

        [Test]
        public void should_accept_tagged_file_for_default_row_without_siblings()
        {
            GivenSiblings(_default);

            Subject.IsSatisfiedBy(GivenFile(_default, "Extended"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_tagged_file_for_default_row_when_no_sibling_claims_it()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, "Remastered"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_theatrical_file_for_default_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, "Theatrical Cut"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_file_for_default_row_when_sibling_claims_edition()
        {
            var decision = Subject.IsSatisfiedBy(GivenFile(_default, "Extended"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release edition 'Extended' belongs to the Extended edition of this movie");
        }

        [Test]
        public void should_reject_file_for_default_row_when_sibling_alias_claims_edition()
        {
            var decision = Subject.IsSatisfiedBy(GivenFile(_default, "Director's Edition"), null);

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().Be("Release edition 'Director's Edition' belongs to the Director's Cut edition of this movie");
        }

        [Test]
        public void should_reject_file_for_default_row_even_when_claiming_sibling_is_unmonitored()
        {
            _directorsCut.Monitored.Should().BeFalse();

            Subject.IsSatisfiedBy(GivenFile(_default, "DC"), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_query_siblings_once_for_tagged_file_on_default_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, "Extended"), null);

            Mocker.GetMock<IMovieService>().Verify(s => s.GetEditionSiblings(_default.Id), Times.Once());
            VerifySiblingQueries(Times.Once());
        }

        // edition rows

        [Test]
        public void should_accept_file_naming_the_edition_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_file_naming_an_alias_of_the_edition_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended Edition"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_matching_file_for_unmonitored_edition_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_directorsCut, "Directors Cut"), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_file_naming_another_edition_for_edition_row()
        {
            var decision = Subject.IsSatisfiedBy(GivenFile(_extended, "Director's Cut"), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release edition 'Director's Cut' does not match the Extended edition");
        }

        [Test]
        public void should_reject_untagged_file_for_edition_row_that_does_not_accept_untagged()
        {
            var decision = Subject.IsSatisfiedBy(GivenFile(_extended, ""), null);

            decision.Accepted.Should().BeFalse();
            decision.Reason.Should().Be(ImportRejectionReason.EditionMismatch);
            decision.Message.Should().Be("Release has no edition tag and the Extended edition does not accept untagged releases");
        }

        [Test]
        public void should_treat_null_edition_as_untagged_for_edition_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, null), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_untagged_file_for_edition_row_that_accepts_untagged()
        {
            _extended.EditionAcceptUntagged = true;

            Subject.IsSatisfiedBy(GivenFile(_extended, ""), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_still_reject_mismatching_edition_when_edition_row_accepts_untagged()
        {
            _extended.EditionAcceptUntagged = true;

            Subject.IsSatisfiedBy(GivenFile(_extended, "Director's Cut"), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_judge_the_aggregated_edition_not_the_file_name()
        {
            var taggedByDownloadClient = GivenFile(_extended, "Extended");
            taggedByDownloadClient.FileMovieInfo.Edition = "";

            var taggedOnlyInFileName = GivenFile(_extended, "");
            taggedOnlyInFileName.FileMovieInfo.Edition = "Extended";

            Subject.IsSatisfiedBy(taggedByDownloadClient, null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(taggedOnlyInFileName, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_not_query_siblings_for_edition_rows()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended"), null);
            Subject.IsSatisfiedBy(GivenFile(_extended, "Director's Cut"), null);
            Subject.IsSatisfiedBy(GivenFile(_extended, ""), null);

            VerifySiblingQueries(Times.Never());
        }
    }
}
