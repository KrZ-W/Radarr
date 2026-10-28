// krzw(edition-runtime-catalog): edition catalog resources
using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Movies.Editions.Catalog;

namespace Radarr.Api.V3.Movies
{
    public class EditionCatalogResource
    {
        public int MovieId { get; set; }
        public int MovieMetadataId { get; set; }
        public int TmdbId { get; set; }
        public int ToleranceMinutes { get; set; }
        public List<EditionCatalogEntryResource> Merged { get; set; }
        public List<EditionCatalogEntryResource> Entries { get; set; }
    }

    public class EditionCatalogEntryResource
    {
        public int Id { get; set; }
        public int MovieMetadataId { get; set; }
        public string Name { get; set; }
        public int RuntimeMinutes { get; set; }
        public EditionCatalogSource Source { get; set; }
        public int Confidence { get; set; }
        public List<string> Attributes { get; set; }
        public DateTime Updated { get; set; }

        // Below EditionRuntime.MinimumConfidence: shown, never drives a verdict (transfer labels, suspect TMDb runtime)
        public bool Informational { get; set; }
    }

    public class EditionCatalogUserEntryResource
    {
        public int MovieId { get; set; }
        public string Name { get; set; }
        public int RuntimeMinutes { get; set; }
    }

    public static class EditionCatalogResourceMapper
    {
        public static EditionCatalogEntryResource ToResource(this EditionCatalogEntry model)
        {
            if (model == null)
            {
                return null;
            }

            return new EditionCatalogEntryResource
            {
                Id = model.Id,
                MovieMetadataId = model.MovieMetadataId,
                Name = model.Name,
                RuntimeMinutes = model.RuntimeMinutes,
                Source = model.Source,
                Confidence = model.Confidence,
                Attributes = model.Attributes.ToList(),
                Updated = model.Updated,
                Informational = model.Confidence < EditionRuntime.MinimumConfidence
            };
        }

        public static List<EditionCatalogEntryResource> ToResource(this IEnumerable<EditionCatalogEntry> models)
        {
            return models.Select(ToResource).ToList();
        }
    }
}
