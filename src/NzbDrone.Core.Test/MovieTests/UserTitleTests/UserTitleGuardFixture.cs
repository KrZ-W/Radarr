using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.UserTitles;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MovieTests.UserTitleTests
{
    [TestFixture]
    public class UserTitleGuardFixture : CoreTest<UserTitleGuard>
    {
        private Movie _movie;
        private List<string> _otherTitles;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { TmdbId = 194 };
            _otherTitles = new List<string>();
        }

        private void GivenCandidates(int titleCount, params Movie[] candidates)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitleCandidates(It.Is<List<string>>(l => l.Count == titleCount), out _otherTitles))
                  .Returns(new List<Movie>(candidates));
        }

        private void GivenCandidatesForTitle(string title, params Movie[] candidates)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitleCandidates(It.Is<List<string>>(l => l.Count == 1 && l[0] == title), out _otherTitles))
                  .Returns(new List<Movie>(candidates));
        }

        [Test]
        public void should_return_empty_for_no_titles()
        {
            Subject.FilterSafe(_movie, new List<string>(), out var rejected).Should().BeEmpty();
            rejected.Should().BeEmpty();

            Mocker.GetMock<IMovieService>().Verify(s => s.FindByTitleCandidates(It.IsAny<List<string>>(), out _otherTitles), Times.Never());
        }

        [Test]
        public void should_accept_all_when_no_movie_owns_any_title()
        {
            GivenCandidates(2);

            Subject.FilterSafe(_movie, new List<string> { "Un Titre", "Autre Titre" }, out var rejected).Should().Equal("Un Titre", "Autre Titre");
            rejected.Should().BeEmpty();
        }

        [Test]
        public void should_accept_all_when_only_the_same_movie_owns_them()
        {
            GivenCandidates(2, new Movie { TmdbId = 194 });

            Subject.FilterSafe(_movie, new List<string> { "Un Titre", "Autre Titre" }, out var rejected).Should().HaveCount(2);
            rejected.Should().BeEmpty();
        }

        [Test]
        public void should_query_once_on_the_fast_path()
        {
            GivenCandidates(3);

            Subject.FilterSafe(_movie, new List<string> { "A", "B", "C" }, out _);

            Mocker.GetMock<IMovieService>().Verify(s => s.FindByTitleCandidates(It.IsAny<List<string>>(), out _otherTitles), Times.Once());
        }

        [Test]
        public void should_reject_only_the_title_owned_by_another_movie()
        {
            GivenCandidates(3, new Movie { TmdbId = 194 }, new Movie { TmdbId = 999 });
            GivenCandidatesForTitle("A");
            GivenCandidatesForTitle("B", new Movie { TmdbId = 999 });
            GivenCandidatesForTitle("C", new Movie { TmdbId = 194 });

            var safe = Subject.FilterSafe(_movie, new List<string> { "A", "B", "C" }, out var rejected);

            safe.Should().Equal("A", "C");
            rejected.Should().Equal("B");
        }

        [Test]
        public void should_reject_a_title_when_any_candidate_is_another_movie()
        {
            GivenCandidates(1, new Movie { TmdbId = 194 }, new Movie { TmdbId = 999 });
            GivenCandidatesForTitle("A", new Movie { TmdbId = 194 }, new Movie { TmdbId = 999 });

            Subject.FilterSafe(_movie, new List<string> { "A" }, out var rejected).Should().BeEmpty();
            rejected.Should().Equal("A");
        }
    }
}
