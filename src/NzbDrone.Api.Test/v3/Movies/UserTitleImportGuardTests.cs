using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using Radarr.Api.V3.Movies;

namespace NzbDrone.Api.Test.v3.Movies;

[Parallelizable(ParallelScope.All)]
public class UserTitleImportGuardTests
{
    private static Mock<IMovieService> GivenCandidates(params Movie[] candidates)
    {
        var mock = new Mock<IMovieService>();
        var otherTitles = new List<string>();

        mock.Setup(s => s.FindByTitleCandidates(It.IsAny<List<string>>(), out otherTitles))
            .Returns(new List<Movie>(candidates));

        return mock;
    }

    [Test]
    public void Safe_when_no_movie_owns_the_title()
    {
        var movie = new Movie { TmdbId = 194 };

        UserTitleImportGuard.IsSafeForMovie(GivenCandidates().Object, "Un Titre", movie).Should().BeTrue();
    }

    [Test]
    public void Safe_when_only_the_same_movie_owns_the_title()
    {
        var movie = new Movie { TmdbId = 194 };

        UserTitleImportGuard.IsSafeForMovie(GivenCandidates(new Movie { TmdbId = 194 }).Object, "Un Titre", movie).Should().BeTrue();
    }

    [Test]
    public void Unsafe_when_another_movie_owns_the_title()
    {
        var movie = new Movie { TmdbId = 194 };

        UserTitleImportGuard.IsSafeForMovie(GivenCandidates(new Movie { TmdbId = 999 }).Object, "Un Titre", movie).Should().BeFalse();
    }

    [Test]
    public void Unsafe_when_any_candidate_is_another_movie()
    {
        var movie = new Movie { TmdbId = 194 };
        var service = GivenCandidates(new Movie { TmdbId = 194 }, new Movie { TmdbId = 999 }).Object;

        UserTitleImportGuard.IsSafeForMovie(service, "Un Titre", movie).Should().BeFalse();
    }
}
