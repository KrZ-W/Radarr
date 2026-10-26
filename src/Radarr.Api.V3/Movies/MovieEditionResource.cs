// krzw(multi-edition): edition fields of MovieResource (edition object on edition rows, editions summary on default rows)
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Movies;

namespace Radarr.Api.V3.Movies
{
    public class MovieEditionResource
    {
        public string Name { get; set; }
        public List<string> Aliases { get; set; }
        public bool AcceptUntagged { get; set; }
    }

    public class MovieEditionSummaryResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Monitored { get; set; }
        public bool HasFile { get; set; }
        public int QualityProfileId { get; set; }
        public int MovieFileId { get; set; }
    }

    public static class MovieEditionResourceMapper
    {
        // null for the default row, so clients that do not know editions see no new object on their movies.
        public static MovieEditionResource ToEditionResource(this Movie model)
        {
            if (model == null || model.IsDefaultEdition)
            {
                return null;
            }

            return new MovieEditionResource
            {
                Name = model.Edition,
                Aliases = model.EditionAliases.ToList(),
                AcceptUntagged = model.EditionAcceptUntagged
            };
        }

        public static MovieEditionSummaryResource ToEditionSummary(this Movie model)
        {
            return new MovieEditionSummaryResource
            {
                Id = model.Id,
                Name = model.Edition,
                Monitored = model.Monitored,
                HasFile = model.HasFile,
                QualityProfileId = model.QualityProfileId,
                MovieFileId = model.MovieFileId
            };
        }

        // Fills EditionParentId (edition rows: the default row's id) and Editions (default rows: their edition rows)
        // from the rows of the movie's title. titleRows may include the movie itself and rows of other titles; only
        // rows sharing its MovieMetadataId count. No query is made here, callers batch the rows.
        public static MovieResource LinkEditions(this MovieResource resource, Movie movie, IEnumerable<Movie> titleRows)
        {
            if (resource == null || movie == null)
            {
                return resource;
            }

            var siblings = (titleRows ?? Enumerable.Empty<Movie>())
                           .Where(r => r != null && r.Id != movie.Id && r.MovieMetadataId == movie.MovieMetadataId)
                           .ToList();

            if (movie.IsEdition)
            {
                resource.EditionParentId = siblings.FirstOrDefault(r => r.IsDefaultEdition)?.Id;
                resource.Editions = new List<MovieEditionSummaryResource>();

                return resource;
            }

            resource.EditionParentId = null;
            resource.Editions = siblings.Where(r => r.IsEdition)
                                        .OrderBy(r => r.Id)
                                        .Select(ToEditionSummary)
                                        .ToList();

            return resource;
        }
    }
}
