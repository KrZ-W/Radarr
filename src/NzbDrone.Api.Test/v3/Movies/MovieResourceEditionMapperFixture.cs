using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Movies;
using Radarr.Api.V3.Movies;

namespace NzbDrone.Api.Test.v3.Movies;

// krzw(multi-edition)
[Parallelizable(ParallelScope.All)]
public class MovieResourceEditionMapperFixture
{
    private static Movie Row(int id, string edition, int movieFileId = 0, bool monitored = true, params string[] aliases)
    {
        return new Movie
        {
            Id = id,
            MovieMetadataId = 7,
            MovieMetadata = new MovieMetadata { Id = 7, TmdbId = 603, Title = "The Matrix", Year = 1999, Images = new List<NzbDrone.Core.MediaCover.MediaCover>(), Genres = new List<string>(), Keywords = new List<string>() },
            Edition = edition,
            EditionAliases = new List<string>(aliases),
            EditionAcceptUntagged = edition == "Extended",
            Monitored = monitored,
            QualityProfileId = 3,
            MovieFileId = movieFileId,
            Path = "/movies/The Matrix (1999)"
        };
    }

    [Test]
    public void Default_row_maps_to_a_null_edition_and_an_empty_editions_list()
    {
        var resource = Row(1, "").ToResource(0);

        resource.Edition.Should().BeNull();
        resource.EditionParentId.Should().BeNull();
        resource.Editions.Should().NotBeNull().And.BeEmpty();
    }

    [Test]
    public void Edition_row_maps_name_aliases_and_accept_untagged()
    {
        var resource = Row(2, "Extended", 0, true, "extended", "extended cut").ToResource(0);

        resource.Edition.Should().NotBeNull();
        resource.Edition.Name.Should().Be("Extended");
        resource.Edition.Aliases.Should().Equal("extended", "extended cut");
        resource.Edition.AcceptUntagged.Should().BeTrue();
        resource.EditionParentId.Should().BeNull("the parent is only known once the title's rows are linked");
    }

    [Test]
    public void Linking_fills_the_parent_on_edition_rows_and_the_summaries_on_the_default_row()
    {
        var rows = new List<Movie>
        {
            Row(1, "", 10),
            Row(2, "Extended", 0, false, "extended"),
            Row(3, "Director's Cut", 12, true, "directors cut"),
            new Movie { Id = 4, MovieMetadataId = 8, Edition = "Extended" }
        };

        var defaultResource = rows[0].ToResource(0).LinkEditions(rows[0], rows);
        var extendedResource = rows[1].ToResource(0).LinkEditions(rows[1], rows);

        defaultResource.EditionParentId.Should().BeNull();
        defaultResource.Editions.Should().HaveCount(2);
        defaultResource.Editions[0].Id.Should().Be(2);
        defaultResource.Editions[0].Name.Should().Be("Extended");
        defaultResource.Editions[0].Monitored.Should().BeFalse();
        defaultResource.Editions[0].HasFile.Should().BeFalse();
        defaultResource.Editions[0].MovieFileId.Should().Be(0);
        defaultResource.Editions[0].QualityProfileId.Should().Be(3);
        defaultResource.Editions[1].Id.Should().Be(3);
        defaultResource.Editions[1].HasFile.Should().BeTrue();
        defaultResource.Editions[1].MovieFileId.Should().Be(12);

        extendedResource.EditionParentId.Should().Be(1);
        extendedResource.Editions.Should().BeEmpty();
    }

    [Test]
    public void Linking_without_the_default_row_leaves_the_parent_null()
    {
        var extended = Row(2, "Extended");

        extended.ToResource(0).LinkEditions(extended, new List<Movie> { extended }).EditionParentId.Should().BeNull();
    }

    [Test]
    public void Model_takes_the_edition_fields_from_the_resource()
    {
        var model = new MovieResource
        {
            TmdbId = 603,
            Edition = new MovieEditionResource { Name = "Extended", Aliases = new List<string> { "extended cut" }, AcceptUntagged = true }
        }.ToModel();

        model.Edition.Should().Be("Extended");
        model.EditionAliases.Should().Equal("extended cut");
        model.EditionAcceptUntagged.Should().BeTrue();
        model.IsEdition.Should().BeTrue();
    }

    [Test]
    public void Model_is_a_default_row_when_the_resource_carries_no_edition()
    {
        var model = new MovieResource { TmdbId = 603 }.ToModel();

        model.IsDefaultEdition.Should().BeTrue();
        model.Edition.Should().BeEmpty();
        model.EditionAliases.Should().NotBeNull().And.BeEmpty();
        model.EditionAcceptUntagged.Should().BeFalse();
    }

    [Test]
    public void Model_survives_an_edition_object_without_aliases()
    {
        var model = new MovieResource { TmdbId = 603, Edition = new MovieEditionResource { Name = "IMAX" } }.ToModel();

        model.Edition.Should().Be("IMAX");
        model.EditionAliases.Should().NotBeNull().And.BeEmpty();
    }
}
