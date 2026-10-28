// krzw(edition-runtime-catalog): TMDb's single stored runtime as a low-weight "Default" entry, marked suspect when IMDb disagrees
using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Movies.Editions.Catalog.Providers
{
    // No network call: MovieMetadata.Runtime is already stored. It reads the IMDb entries already in the catalog, so the
    // refresh runs it after the IMDb provider. A TMDb runtime more than 50 % away from IMDb's theatrical runtime is
    // almost always a wrong match (Seven at 18 min) and gets confidence 0, which keeps it out of every verdict.
    public class TmdbBaselineProvider : IEditionCatalogProvider
    {
        public const string DefaultName = "Default";
        public const int BaselineConfidence = 30;
        public const int SuspectConfidence = 0;
        public const string SuspectAttribute = "suspect";
        public const double SuspectRatio = 0.5;

        private readonly IEditionCatalogRepository _repository;

        public TmdbBaselineProvider(IEditionCatalogRepository repository)
        {
            _repository = repository;
        }

        public EditionCatalogSource Source => EditionCatalogSource.Tmdb;

        public bool Enabled => true;

        public Dictionary<int, List<EditionCatalogEntry>> Fetch(List<MovieMetadata> metadata)
        {
            var titles = (metadata ?? new List<MovieMetadata>()).Where(m => m != null && m.Id > 0).ToList();

            var imdbTheatrical = _repository.GetByMetadataIds(titles.Select(m => m.Id))
                                            .Where(e => e.Source == EditionCatalogSource.Imdb &&
                                                        e.RuntimeMinutes > 0 &&
                                                        EditionNormalizer.Normalize(e.Name) == EditionNormalizer.Normalize(EditionLabelNormalizer.TheatricalName))
                                            .GroupBy(e => e.MovieMetadataId)
                                            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Confidence).First());

            var result = new Dictionary<int, List<EditionCatalogEntry>>();

            foreach (var title in titles)
            {
                imdbTheatrical.TryGetValue(title.Id, out var theatrical);
                result[title.Id] = Build(title, theatrical);
            }

            return result;
        }

        public static List<EditionCatalogEntry> Build(MovieMetadata title, EditionCatalogEntry imdbTheatrical)
        {
            if (title == null || title.Runtime <= 0)
            {
                return new List<EditionCatalogEntry>();
            }

            var suspect = IsSuspect(title.Runtime, imdbTheatrical?.RuntimeMinutes ?? 0);

            return new List<EditionCatalogEntry>
            {
                new EditionCatalogEntry
                {
                    MovieMetadataId = title.Id,
                    Name = DefaultName,
                    RuntimeMinutes = title.Runtime,
                    Source = EditionCatalogSource.Tmdb,
                    Confidence = suspect ? SuspectConfidence : BaselineConfidence,
                    Attributes = suspect ? new List<string> { SuspectAttribute } : new List<string>()
                }
            };
        }

        public static bool IsSuspect(int tmdbMinutes, int imdbTheatricalMinutes)
        {
            if (tmdbMinutes <= 0 || imdbTheatricalMinutes <= 0)
            {
                return false;
            }

            return Math.Abs(tmdbMinutes - imdbTheatricalMinutes) > imdbTheatricalMinutes * SuspectRatio;
        }
    }
}
