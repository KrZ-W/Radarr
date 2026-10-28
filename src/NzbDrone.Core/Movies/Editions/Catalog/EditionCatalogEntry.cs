// krzw(edition-runtime-catalog): one known cut of a title (name + runtime) as reported by one source
using System;
using System.Collections.Generic;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public class EditionCatalogEntry : ModelBase
    {
        private List<string> _attributes = new List<string>();

        public int MovieMetadataId { get; set; }

        // Preset or free-text edition name, same rules as edition rows (compared through EditionNormalizer)
        public string Name { get; set; }
        public int RuntimeMinutes { get; set; }
        public EditionCatalogSource Source { get; set; }

        // 0-100. Entries below EditionRuntime.MinimumConfidence are shown but never drive a verdict.
        public int Confidence { get; set; }

        // Raw labels from the source ("director's cut roadshow", "Blu Ray Extended Edition"), kept for display and aliases
        public List<string> Attributes
        {
            get => _attributes;
            set => _attributes = value ?? new List<string>();
        }

        public DateTime Updated { get; set; }

        public override string ToString()
        {
            return $"{Name} {RuntimeMinutes} min ({Source}, {Confidence})";
        }
    }
}
