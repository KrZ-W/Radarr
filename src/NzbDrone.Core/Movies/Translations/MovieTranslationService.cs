using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.AlternativeTitles;  // krzw(user-titles)
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.Movies.Translations
{
    public interface IMovieTranslationService
    {
        List<MovieTranslation> GetAllTranslationsForMovieMetadata(int movieMetadataId);
        List<MovieTranslation> GetAllTranslationsForLanguage(Language language);
        List<MovieTranslation> UpdateTranslations(List<MovieTranslation> titles, MovieMetadata movie);
        List<MovieTranslation> UpsertUserTranslations(List<MovieTranslation> translations, MovieMetadata movie);  // krzw(user-titles)
    }

    public class MovieTranslationService : IMovieTranslationService, IHandleAsync<MoviesDeletedEvent>
    {
        private readonly IMovieTranslationRepository _translationRepo;
        private readonly Logger _logger;

        public MovieTranslationService(IMovieTranslationRepository translationRepo,
                             Logger logger)
        {
            _translationRepo = translationRepo;
            _logger = logger;
        }

        public List<MovieTranslation> GetAllTranslationsForMovieMetadata(int movieMetadataId)
        {
            return _translationRepo.FindByMovieMetadataId(movieMetadataId).ToList();
        }

        public List<MovieTranslation> GetAllTranslationsForLanguage(Language language)
        {
            return _translationRepo.FindByLanguage(language).ToList();
        }

        public void RemoveTitle(MovieTranslation title)
        {
            _translationRepo.Delete(title);
        }

        public List<MovieTranslation> UpdateTranslations(List<MovieTranslation> translations, MovieMetadata movieMetadata)
        {
            var movieMetadataId = movieMetadata.Id;

            // First update the movie ids so we can correlate them later
            translations.ForEach(t => t.MovieMetadataId = movieMetadataId);

            // Then throw out any we don't have languages for
            translations = translations.Where(t => t.Language != null).ToList();

            // krzw(regional-translations): distinct key includes region
            // Make sure translations are distinct by (Language, RegionalLanguage)
            // This allows multiple translations for the same language if they have different regions
            translations = translations.DistinctBy(t => new { t.Language, t.RegionalLanguage }).ToList();

            // Now find translations to delete, update and insert
            var existingTranslations = _translationRepo.FindByMovieMetadataId(movieMetadataId);

            // krzw(user-titles): refresh preservation
            // Rows not sourced from TMDB (user imports) are managed outside the metadata
            // refresh and must survive it; the refresh only reconciles TMDB-sourced rows.
            var preservedTranslations = existingTranslations.Where(t => t.SourceType != SourceType.Tmdb).ToList();
            existingTranslations = existingTranslations.Where(t => t.SourceType == SourceType.Tmdb).ToList();

            // An incoming TMDB title duplicating a preserved row is dropped: the user row wins.
            translations = translations.Where(t => !preservedTranslations.Any(p => p.CleanTitle == t.CleanTitle)).ToList();

            var updateList = new List<MovieTranslation>();
            var addList = new List<MovieTranslation>();
            var upToDateCount = 0;

            foreach (var translation in translations)
            {
                // krzw(regional-translations): match key includes region
                var existingTranslation = existingTranslations.FirstOrDefault(x => x.Language == translation.Language && x.RegionalLanguage == translation.RegionalLanguage);

                if (existingTranslation != null)
                {
                    existingTranslations.Remove(existingTranslation);

                    translation.UseDbFieldsFrom(existingTranslation);

                    if (!translation.Equals(existingTranslation))
                    {
                        updateList.Add(translation);
                    }
                    else
                    {
                        upToDateCount++;
                    }
                }
                else
                {
                    addList.Add(translation);
                }
            }

            _translationRepo.DeleteMany(existingTranslations);
            _translationRepo.UpdateMany(updateList);
            _translationRepo.InsertMany(addList);

            // krzw(user-titles): preserved rows are returned with the TMDB set
            _logger.Debug("[{0}] {1} translations up to date; Updating {2}, Adding {3}, Deleting {4}, Preserving {5} non-TMDB entries.", movieMetadata.Title, upToDateCount, updateList.Count, addList.Count, existingTranslations.Count, preservedTranslations.Count);

            return translations.Concat(preservedTranslations).ToList();
        }

        // krzw(user-titles): POST /api/v3/translation/user/import
        public List<MovieTranslation> UpsertUserTranslations(List<MovieTranslation> translations, MovieMetadata movieMetadata)
        {
            var movieMetadataId = movieMetadata.Id;

            translations.ForEach(t =>
            {
                t.MovieMetadataId = movieMetadataId;
                t.SourceType = SourceType.User;
            });

            translations = translations.Where(t => t.CleanTitle != movieMetadata.CleanTitle).ToList();
            translations = translations.DistinctBy(t => t.CleanTitle).ToList();

            var allTranslationsByCleanTitles = _translationRepo.FindByCleanTitles(translations.Select(t => t.CleanTitle).ToList());
            translations = translations.Where(t => !allTranslationsByCleanTitles.Any(e => e.CleanTitle == t.CleanTitle && e.MovieMetadataId != t.MovieMetadataId)).ToList();

            var existingTranslations = _translationRepo.FindByMovieMetadataId(movieMetadataId);
            var addList = translations.Where(t => !existingTranslations.Any(e => e.CleanTitle == t.CleanTitle)).ToList();

            _translationRepo.InsertMany(addList);

            _logger.Debug("[{0}] Upserted user translations; Adding {1}, Skipping {2} already present.", movieMetadata.Title, addList.Count, translations.Count - addList.Count);

            return addList;
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            // TODO handle metadata delete instead of movie delete
            _translationRepo.DeleteForMovies(message.Movies.Select(m => m.MovieMetadataId).ToList());
        }
    }
}
