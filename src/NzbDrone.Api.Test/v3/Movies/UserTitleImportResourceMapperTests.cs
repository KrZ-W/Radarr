using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies.UserTitles;
using Radarr.Api.V3.Movies;
using Radarr.Http.REST;

namespace NzbDrone.Api.Test.v3.Movies;

[Parallelizable(ParallelScope.All)]
public class UserTitleImportResourceMapperTests
{
    private static UserAlternativeTitleImportResource Row(int tmdbId, params UserAlternativeTitleImportEntryResource[] titles)
    {
        return new UserAlternativeTitleImportResource
        {
            TmdbId = tmdbId,
            ImdbId = "tt0211915",
            MovieTitle = "Amélie",
            Year = 2001,
            MissingFrenchTitles = titles.ToList()
        };
    }

    [Test]
    public void Null_body_maps_to_empty_request_list()
    {
        ((List<UserAlternativeTitleImportResource>)null).ToImportRequests("fr").Should().BeEmpty();
    }

    [Test]
    public void Maps_identity_fields_and_titles()
    {
        var requests = new List<UserAlternativeTitleImportResource>
        {
            Row(194, new UserAlternativeTitleImportEntryResource { Title = "Amélie de Montmartre", Region = "CA" })
        }.ToImportRequests(null);

        var request = requests.Should().ContainSingle().Subject;
        request.TmdbId.Should().Be(194);
        request.ImdbId.Should().Be("tt0211915");
        request.MovieTitle.Should().Be("Amélie");
        request.Year.Should().Be(2001);
        request.Titles.Should().ContainSingle().Which.Should().BeEquivalentTo(new UserTitleImportEntry { Title = "Amélie de Montmartre", Region = "CA", Language = null });
    }

    [Test]
    public void Default_language_applies_only_when_missing()
    {
        var requests = new List<UserAlternativeTitleImportResource>
        {
            Row(194,
                new UserAlternativeTitleImportEntryResource { Title = "A" },
                new UserAlternativeTitleImportEntryResource { Title = "B", Language = "de" })
        }.ToImportRequests("fr");

        requests[0].Titles.Select(t => t.Language).Should().Equal("fr", "de");
    }

    [Test]
    public void Titles_alias_feeds_the_same_list()
    {
        var resource = new UserAlternativeTitleImportResource { TmdbId = 194, Titles = new List<UserAlternativeTitleImportEntryResource> { new() { Title = "A" } } };

        resource.MissingFrenchTitles.Should().HaveCount(1);
        new List<UserAlternativeTitleImportResource> { resource }.ToImportRequests(null)[0].Titles.Should().HaveCount(1);
    }

    [Test]
    public void Rejects_too_many_movies()
    {
        var rows = Enumerable.Range(1, UserTitleImportResourceMapper.MaxMoviesPerRequest + 1).Select(i => Row(i)).ToList();

        var act = () => rows.ToImportRequests(null);

        act.Should().Throw<BadRequestException>();
    }

    [Test]
    public void Rejects_too_many_titles_for_one_movie()
    {
        var titles = Enumerable.Range(1, UserTitleImportResourceMapper.MaxTitlesPerMovie + 1).Select(i => new UserAlternativeTitleImportEntryResource { Title = $"T{i}" }).ToArray();

        var act = () => new List<UserAlternativeTitleImportResource> { Row(194, titles) }.ToImportRequests(null);

        act.Should().Throw<BadRequestException>();
    }

    [Test]
    public void Rejects_over_long_title()
    {
        var act = () => new List<UserAlternativeTitleImportResource> { Row(194, new UserAlternativeTitleImportEntryResource { Title = new string('x', UserTitleImportResourceMapper.MaxTitleLength + 1) }) }.ToImportRequests(null);

        act.Should().Throw<BadRequestException>();
    }

    [TestCase("CANADA")]
    [TestCase("C")]
    [TestCase("C4")]
    public void Rejects_region_that_is_not_a_two_letter_code(string region)
    {
        var resources = new List<UserAlternativeTitleImportResource>
        {
            Row(194, new UserAlternativeTitleImportEntryResource { Title = "Amélie de Montmartre", Region = region })
        };

        var act = () => resources.ToImportRequests("fr");

        act.Should().Throw<BadRequestException>().WithMessage("*Region*tmdb:194*");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("CA")]
    [TestCase("ca")]
    public void Accepts_blank_or_two_letter_region(string region)
    {
        var requests = new List<UserAlternativeTitleImportResource>
        {
            Row(194, new UserAlternativeTitleImportEntryResource { Title = "Amélie de Montmartre", Region = region })
        }.ToImportRequests("fr");

        requests.Single().Titles.Single().Region.Should().Be(region);
    }

    [Test]
    public void Result_maps_counts_and_lists()
    {
        var result = new UserTitleImportResult { MoviesProcessed = 2, TitlesAdded = 3, TitlesGuarded = 1, TitlesUnknownLanguage = 1, TitlesAlreadyPresent = 2 };
        result.MoviesNotFound.Add("nf");
        result.MoviesFailed.Add("f");

        var resource = result.ToResource();

        resource.MoviesProcessed.Should().Be(2);
        resource.TitlesAdded.Should().Be(3);
        resource.TitlesSkipped.Should().Be(4);
        resource.TitlesGuarded.Should().Be(1);
        resource.TitlesUnknownLanguage.Should().Be(1);
        resource.TitlesAlreadyPresent.Should().Be(2);
        resource.MoviesNotFound.Should().Equal("nf");
        resource.MoviesFailed.Should().Equal("f");
    }
}
