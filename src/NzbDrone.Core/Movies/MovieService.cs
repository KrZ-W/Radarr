using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;  // krzw(multi-edition)
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.AutoTagging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Exceptions;  // krzw(multi-edition)
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies.Editions;  // krzw(multi-edition)
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.RomanNumerals;

namespace NzbDrone.Core.Movies
{
    public interface IMovieService
    {
        Movie GetMovie(int movieId);
        List<Movie> GetMovies(IEnumerable<int> movieIds);
        PagingSpec<Movie> Paged(PagingSpec<Movie> pagingSpec);
        Movie AddMovie(Movie newMovie);
        List<Movie> AddMovies(List<Movie> newMovies);
        Movie FindByImdbId(string imdbid);
        Movie FindByTmdbId(int tmdbid);
        Movie FindByTitle(string title);
        Movie FindByTitle(string title, int year);
        Movie FindByTitle(List<string> titles, int? year, List<string> otherTitles, List<Movie> candidates);
        Movie FindByTitle(string title, int year, string edition);  // krzw(multi-edition)
        Movie FindByTitle(List<string> titles, int? year, List<string> otherTitles, List<Movie> candidates, string edition);  // krzw(multi-edition)
        Movie FindByImdbId(string imdbid, string edition);  // krzw(multi-edition)
        List<Movie> FindByTitleCandidates(List<string> titles, out List<string> otherTitles);
        Movie FindByPath(string path);
        Dictionary<int, string> AllMoviePaths();
        List<int> AllMovieTmdbIds();
        bool MovieExists(Movie movie);
        List<Movie> GetMoviesByFileId(int fileId);
        List<Movie> GetMoviesByCollectionTmdbId(int collectionId);
        List<Movie> GetMoviesBetweenDates(DateTime start, DateTime end, bool includeUnmonitored);
        PagingSpec<Movie> MoviesWithoutFiles(PagingSpec<Movie> pagingSpec);
        void DeleteMovie(int movieId, bool deleteFiles, bool addImportListExclusion = false);
        void DeleteMovies(List<int> movieIds, bool deleteFiles, bool addImportListExclusion = false);
        List<Movie> GetAllMovies();
        Dictionary<int, List<int>> AllMovieTags();
        Movie UpdateMovie(Movie movie);
        List<Movie> UpdateMovie(List<Movie> movies, bool useExistingRelativeFolder);
        void UpdateLastSearchTime(Movie movie);
        List<int> GetRecommendedTmdbIds();
        bool MoviePathExists(string folder);
        void RemoveAddOptions(Movie movie);
        bool UpdateTags(Movie movie);
        bool ExistsByMetadataId(int metadataId);
        HashSet<int> AllMovieWithCollectionsTmdbIds();
        List<Movie> FindAllByTmdbId(int tmdbId);  // krzw(multi-edition)
        List<Movie> FindAllByTmdbIds(List<int> tmdbIds);  // krzw(multi-edition)
        List<Movie> GetEditionSiblings(int movieId);  // krzw(multi-edition)
        List<Movie> GetEditionSiblings(Movie movie);  // krzw(multi-edition): one query when the caller holds the row
        Movie GetDefaultEdition(int movieMetadataId);  // krzw(multi-edition)
        bool EditionExists(int tmdbId, string editionName);  // krzw(multi-edition)
    }

    public class MovieService : IMovieService, IHandle<MovieFileAddedEvent>,
                                               IHandle<MovieFileDeletedEvent>
    {
        private readonly IMovieRepository _movieRepository;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly IBuildMoviePaths _moviePathBuilder;
        private readonly IAutoTaggingService _autoTaggingService;
        private readonly Logger _logger;

        public MovieService(IMovieRepository movieRepository,
                            IEventAggregator eventAggregator,
                            IConfigService configService,
                            IBuildMoviePaths moviePathBuilder,
                            IAutoTaggingService autoTaggingService,
                            Logger logger)
        {
            _movieRepository = movieRepository;
            _eventAggregator = eventAggregator;
            _configService = configService;
            _moviePathBuilder = moviePathBuilder;
            _autoTaggingService = autoTaggingService;
            _logger = logger;
        }

        public Movie GetMovie(int movieId)
        {
            return _movieRepository.Get(movieId);
        }

        public List<Movie> GetMovies(IEnumerable<int> movieIds)
        {
            return _movieRepository.Get(movieIds).ToList();
        }

        public PagingSpec<Movie> Paged(PagingSpec<Movie> pagingSpec)
        {
            return _movieRepository.GetPaged(pagingSpec);
        }

        public Movie AddMovie(Movie newMovie)
        {
            var movie = _movieRepository.Insert(newMovie);

            _eventAggregator.PublishEvent(new MovieAddedEvent(GetMovie(movie.Id)));

            return movie;
        }

        public List<Movie> AddMovies(List<Movie> newMovies)
        {
            _movieRepository.InsertMany(newMovies);

            _eventAggregator.PublishEvent(new MoviesImportedEvent(newMovies));

            return newMovies;
        }

        public Movie FindByTitle(string title)
        {
            var candidates = FindByTitleCandidates(new List<string> { title }, out var otherTitles);

            return FindByTitle(new List<string> { title }, null, otherTitles, candidates);
        }

        public Movie FindByTitle(string title, int year)
        {
            var candidates = FindByTitleCandidates(new List<string> { title }, out var otherTitles);

            return FindByTitle(new List<string> { title }, year, otherTitles, candidates);
        }

        // krzw(multi-edition) begin
        public Movie FindByTitle(string title, int year, string edition)
        {
            var candidates = FindByTitleCandidates(new List<string> { title }, out var otherTitles);

            return FindByTitle(new List<string> { title }, year, otherTitles, candidates, edition);
        }

        // krzw(multi-edition) end

        public Movie FindByTitle(List<string> titles, int? year, List<string> otherTitles, List<Movie> candidates)
        {
            return FindByTitle(titles, year, otherTitles, candidates, null);  // krzw(multi-edition): legacy callers resolve to the default row
        }

        // krzw(multi-edition): the parsed edition picks the row when the title has default + edition rows
        public Movie FindByTitle(List<string> titles, int? year, List<string> otherTitles, List<Movie> candidates, string edition)
        {
            var cleanTitles = titles.Select(t => t.CleanMovieTitle().ToLowerInvariant());

            var result = candidates.Where(x => cleanTitles.Contains(x.MovieMetadata.Value.CleanTitle) || cleanTitles.Contains(x.MovieMetadata.Value.CleanOriginalTitle))
                .AllWithYear(year)
                .ToList();

            if (result == null || result.Count == 0)
            {
                result =
                    candidates.Where(movie => otherTitles.Contains(movie.MovieMetadata.Value.CleanTitle)).AllWithYear(year).ToList();
            }

            if (result == null || result.Count == 0)
            {
                result = candidates
                    .Where(m => m.MovieMetadata.Value.AlternativeTitles.Any(t => cleanTitles.Contains(t.CleanTitle) ||
                                                        otherTitles.Contains(t.CleanTitle)))
                    .AllWithYear(year).ToList();
            }

            if (result == null || result.Count == 0)
            {
                result = candidates
                    .Where(m => m.MovieMetadata.Value.Translations.Any(t => cleanTitles.Contains(t.CleanTitle) ||
                                                        otherTitles.Contains(t.CleanTitle)))
                    .AllWithYear(year).ToList();
            }

            return ReturnSingleMovieOrThrow(result.ToList(), edition);  // krzw(multi-edition)
        }

        public List<Movie> FindByTitleCandidates(List<string> titles, out List<string> otherTitles)
        {
            var lookupTitles = new List<string>();
            otherTitles = new List<string>();

            foreach (var title in titles)
            {
                var cleanTitle = title.CleanMovieTitle().ToLowerInvariant();
                var romanTitle = cleanTitle;
                var arabicTitle = cleanTitle;

                foreach (var arabicRomanNumeral in RomanNumeralParser.GetArabicRomanNumeralsMapping())
                {
                    var arabicNumber = arabicRomanNumeral.ArabicNumeralAsString;
                    var romanNumber = arabicRomanNumeral.RomanNumeral;

                    romanTitle = romanTitle.Replace(arabicNumber, romanNumber);
                    arabicTitle = arabicTitle.Replace(romanNumber, arabicNumber);
                }

                romanTitle = romanTitle.ToLowerInvariant();

                otherTitles.AddRange(new List<string> { arabicTitle, romanTitle });
                lookupTitles.AddRange(new List<string> { cleanTitle, arabicTitle, romanTitle });
            }

            return _movieRepository.FindByTitles(lookupTitles);
        }

        public Movie FindByImdbId(string imdbid)
        {
            return _movieRepository.FindByImdbId(imdbid);
        }

        // krzw(multi-edition) begin
        // The row of the title the parsed edition names: the default row (what FindByImdbId returns) when the
        // edition is blank or names no edition row of that title.
        public Movie FindByImdbId(string imdbid, string edition)
        {
            var defaultRow = _movieRepository.FindByImdbId(imdbid);

            if (defaultRow == null || edition.IsNullOrWhiteSpace())
            {
                return defaultRow;
            }

            var rows = new List<Movie> { defaultRow };

            rows.AddRange(_movieRepository.GetEditionSiblings(defaultRow.Id));

            return EditionMatcher.SelectRow(rows, edition) ?? defaultRow;
        }

        // krzw(multi-edition) end

        public Movie FindByTmdbId(int tmdbid)
        {
            return _movieRepository.FindByTmdbId(tmdbid);
        }

        // krzw(multi-edition) begin
        public List<Movie> FindAllByTmdbId(int tmdbId)
        {
            return _movieRepository.FindAllByTmdbId(tmdbId);
        }

        // every row (default and edition) of the given titles, for callers that link several resources at once
        public List<Movie> FindAllByTmdbIds(List<int> tmdbIds)
        {
            if (tmdbIds == null || tmdbIds.Count == 0)
            {
                return new List<Movie>();
            }

            return _movieRepository.FindByTmdbId(tmdbIds);
        }

        public List<Movie> GetEditionSiblings(int movieId)
        {
            return _movieRepository.GetEditionSiblings(movieId);
        }

        public List<Movie> GetEditionSiblings(Movie movie)
        {
            return _movieRepository.GetEditionSiblings(movie);
        }

        public Movie GetDefaultEdition(int movieMetadataId)
        {
            return _movieRepository.GetDefaultEdition(movieMetadataId);
        }

        public bool EditionExists(int tmdbId, string editionName)
        {
            return _movieRepository.EditionExists(tmdbId, editionName);
        }

        // krzw(multi-edition) end

        public Movie FindByPath(string path)
        {
            return _movieRepository.FindByPath(path);
        }

        public Dictionary<int, string> AllMoviePaths()
        {
            return _movieRepository.AllMoviePaths();
        }

        public List<int> AllMovieTmdbIds()
        {
            return _movieRepository.AllMovieTmdbIds();
        }

        public void DeleteMovie(int movieId, bool deleteFiles, bool addImportListExclusion = false)
        {
            var movie = _movieRepository.Get(movieId);

            EnsureEditionsGoFirst(movie, new HashSet<int> { movieId });  // krzw(multi-edition)

            _movieRepository.Delete(movieId);
            _eventAggregator.PublishEvent(new MoviesDeletedEvent(new List<Movie> { movie }, deleteFiles, addImportListExclusion));
            _logger.Info("Deleted movie {0}", movie);
        }

        public void DeleteMovies(List<int> movieIds, bool deleteFiles, bool addImportListExclusion = false)
        {
            var moviesToDelete = _movieRepository.Get(movieIds).ToList();

            // krzw(multi-edition) begin
            var batch = movieIds.ToHashSet();

            foreach (var movie in moviesToDelete)
            {
                EnsureEditionsGoFirst(movie, batch);
            }

            // krzw(multi-edition) end
            _movieRepository.DeleteMany(movieIds);

            _eventAggregator.PublishEvent(new MoviesDeletedEvent(moviesToDelete, deleteFiles, addImportListExclusion));

            foreach (var movie in moviesToDelete)
            {
                _logger.Info("Deleted movie {0}", movie);
            }
        }

        public List<Movie> GetAllMovies()
        {
            return _movieRepository.All().ToList();
        }

        public Dictionary<int, List<int>> AllMovieTags()
        {
            return _movieRepository.AllMovieTags();
        }

        public Movie UpdateMovie(Movie movie)
        {
            var storedMovie = GetMovie(movie.Id);

            // krzw(multi-edition) begin: the rows of one title share the folder and the default row owns the path
            var pathChanged = storedMovie != null && PathDiffers(storedMovie.Path, movie.Path);

            if (pathChanged && storedMovie.IsEdition)
            {
                throw new NzbDroneClientException(HttpStatusCode.BadRequest, "The path of the {0} edition of {1} follows its default edition and cannot be changed on its own", storedMovie.Edition, storedMovie.Title);
            }

            // krzw(multi-edition) end
            UpdateTags(movie);

            var updatedMovie = _movieRepository.Update(movie);
            _eventAggregator.PublishEvent(new MovieEditedEvent(updatedMovie, storedMovie));

            // krzw(multi-edition) begin
            if (pathChanged)
            {
                PropagatePathToEditions(updatedMovie, new HashSet<int> { updatedMovie.Id });
            }

            // krzw(multi-edition) end
            return updatedMovie;
        }

        public List<Movie> UpdateMovie(List<Movie> movies, bool useExistingRelativeFolder)
        {
            _logger.Debug("Updating {0} movies", movies.Count);

            var movedDefaultRows = new List<Movie>();  // krzw(multi-edition)

            foreach (var m in movies)
            {
                _logger.Trace("Updating: {0}", m.Title);

                if (!m.RootFolderPath.IsNullOrWhiteSpace() && m.IsDefaultEdition)
                {
                    // krzw(multi-edition): the condition above skips edition rows, they follow their default row (AlignEditionPaths)
                    var previousPath = m.Path;  // krzw(multi-edition)

                    m.Path = _moviePathBuilder.BuildPath(m, useExistingRelativeFolder);

                    _logger.Trace("Changing path for {0} to {1}", m.Title, m.Path);

                    // krzw(multi-edition) begin
                    if (PathDiffers(previousPath, m.Path))
                    {
                        movedDefaultRows.Add(m);
                    }

                    // krzw(multi-edition) end
                }
                else
                {
                    _logger.Trace("Not changing path for: {0}", m.Title);
                }

                UpdateTags(m);
            }

            var followers = AlignEditionPaths(movies, movedDefaultRows, useExistingRelativeFolder);  // krzw(multi-edition)

            _movieRepository.UpdateMany(movies);
            _logger.Debug("{0} movies updated", movies.Count);
            _eventAggregator.PublishEvent(new MoviesBulkEditedEvent(movies));

            // krzw(multi-edition) begin
            if (followers.Any())
            {
                _movieRepository.SetFields(followers, s => s.Path);
                _logger.Debug("{0} edition rows followed their default row's path", followers.Count);
                _eventAggregator.PublishEvent(new MoviesBulkEditedEvent(followers));
            }

            // krzw(multi-edition) end
            return movies;
        }

        // krzw(multi-edition) begin
        // A default row cannot go while edition rows still hang off it: they would lose the row that owns their path
        // and the title would have no default row for legacy lookups. Edition rows deleted in the same batch do not count.
        private void EnsureEditionsGoFirst(Movie movie, ISet<int> batch)
        {
            if (movie == null || movie.IsEdition)
            {
                return;
            }

            var remaining = (_movieRepository.GetEditionSiblings(movie.Id) ?? new List<Movie>())
                            .Where(s => s.IsEdition && !batch.Contains(s.Id))
                            .Select(s => s.Edition)
                            .ToList();

            if (remaining.Empty())
            {
                return;
            }

            throw new NzbDroneClientException(HttpStatusCode.BadRequest, "Cannot delete {0}: its {1} edition(s) must be deleted first", movie, string.Join(", ", remaining));
        }

        // Edition rows in a bulk edit take the path of their default row when it is in the batch too. An edition row
        // whose default row is not in the batch may not end up on a path of its own (the API rejects such edits before
        // a folder move is queued; this guards callers that bypass it). Default rows whose path changed drag their
        // edition rows outside the batch along; those are returned for a separate path-only update.
        private List<Movie> AlignEditionPaths(List<Movie> movies, List<Movie> movedDefaultRows, bool useExistingRelativeFolder)
        {
            var batchIds = movies.Select(m => m.Id).ToHashSet();
            var defaultRows = movies.Where(m => m.IsDefaultEdition && m.MovieMetadataId > 0)
                                    .GroupBy(m => m.MovieMetadataId)
                                    .ToDictionary(g => g.Key, g => g.First());

            foreach (var edition in movies.Where(m => m.IsEdition && !m.RootFolderPath.IsNullOrWhiteSpace()))
            {
                if (defaultRows.TryGetValue(edition.MovieMetadataId, out var defaultRow))
                {
                    edition.Path = defaultRow.Path;
                    continue;
                }

                var ownPath = _moviePathBuilder.BuildPath(edition, useExistingRelativeFolder);

                if (PathDiffers(edition.Path, ownPath))
                {
                    throw new NzbDroneClientException(HttpStatusCode.BadRequest, "The path of the {0} edition of {1} follows its default edition and cannot be changed on its own", edition.Edition, edition.Title);
                }
            }

            var followers = new List<Movie>();

            foreach (var defaultRow in movedDefaultRows)
            {
                followers.AddRange(EditionsToFollow(defaultRow, batchIds));
            }

            return followers;
        }

        private void PropagatePathToEditions(Movie defaultRow, ISet<int> alreadyUpdated)
        {
            var followers = EditionsToFollow(defaultRow, alreadyUpdated);

            if (followers.Empty())
            {
                return;
            }

            _movieRepository.SetFields(followers, s => s.Path);
            _logger.Debug("{0} edition rows of {1} followed its path to '{2}'", followers.Count, defaultRow, defaultRow.Path);
            _eventAggregator.PublishEvent(new MoviesBulkEditedEvent(followers));
        }

        // The edition rows of defaultRow that are not on its path yet, with their Path set to it (not persisted here).
        private List<Movie> EditionsToFollow(Movie defaultRow, ISet<int> excludedIds)
        {
            if (defaultRow == null || defaultRow.IsEdition || defaultRow.Path.IsNullOrWhiteSpace())
            {
                return new List<Movie>();
            }

            var followers = (_movieRepository.GetEditionSiblings(defaultRow.Id) ?? new List<Movie>())
                            .Where(s => s.IsEdition && !excludedIds.Contains(s.Id) && PathDiffers(s.Path, defaultRow.Path))
                            .ToList();

            foreach (var follower in followers)
            {
                _logger.Trace("Changing path for the {0} edition of {1} to {2}", follower.Edition, follower.Title, defaultRow.Path);
                follower.Path = defaultRow.Path;
            }

            return followers;
        }

        private static bool PathDiffers(string before, string after)
        {
            if (after.IsNullOrWhiteSpace())
            {
                return false;
            }

            if (before.IsNullOrWhiteSpace())
            {
                return true;
            }

            return !before.PathEquals(after);
        }

        // krzw(multi-edition) end

        public void UpdateLastSearchTime(Movie movie)
        {
            _movieRepository.SetFields(movie, e => e.LastSearchTime);
        }

        public bool MoviePathExists(string folder)
        {
            return _movieRepository.MoviePathExists(folder);
        }

        public void RemoveAddOptions(Movie movie)
        {
            _movieRepository.SetFields(movie, s => s.AddOptions);
        }

        public bool UpdateTags(Movie movie)
        {
            _logger.Trace("Updating tags for {0}", movie);

            var tagsAdded = new HashSet<int>();
            var tagsRemoved = new HashSet<int>();
            var changes = _autoTaggingService.GetTagChanges(movie);

            foreach (var tag in changes.TagsToRemove)
            {
                if (movie.Tags.Contains(tag))
                {
                    movie.Tags.Remove(tag);
                    tagsRemoved.Add(tag);
                }
            }

            foreach (var tag in changes.TagsToAdd)
            {
                if (!movie.Tags.Contains(tag))
                {
                    movie.Tags.Add(tag);
                    tagsAdded.Add(tag);
                }
            }

            if (tagsAdded.Any() || tagsRemoved.Any())
            {
                _logger.Debug("Updated tags for '{0}'. Added: {1}, Removed: {2}", movie.Title, tagsAdded.Count, tagsRemoved.Count);

                return true;
            }

            _logger.Debug("Tags not updated for '{0}'", movie.Title);

            return false;
        }

        public List<Movie> GetMoviesByFileId(int fileId)
        {
            return _movieRepository.GetMoviesByFileId(fileId);
        }

        public List<Movie> GetMoviesByCollectionTmdbId(int collectionId)
        {
            return _movieRepository.GetMoviesByCollectionTmdbId(collectionId);
        }

        public List<Movie> GetMoviesBetweenDates(DateTime start, DateTime end, bool includeUnmonitored)
        {
            var movies = _movieRepository.MoviesBetweenDates(start.ToUniversalTime(), end.ToUniversalTime(), includeUnmonitored);

            return movies;
        }

        public PagingSpec<Movie> MoviesWithoutFiles(PagingSpec<Movie> pagingSpec)
        {
            var movieResult = _movieRepository.MoviesWithoutFiles(pagingSpec);

            return movieResult;
        }

        public bool MovieExists(Movie movie)
        {
            Movie result = null;

            if (movie.TmdbId != 0)
            {
                result = _movieRepository.FindAllByTmdbId(movie.TmdbId).FirstOrDefault();  // krzw(multi-edition): any row (default or edition) counts as existing
                if (result != null)
                {
                    return true;
                }
            }

            if (movie.ImdbId.IsNotNullOrWhiteSpace())
            {
                result = _movieRepository.FindByImdbId(movie.ImdbId);
                if (result != null)
                {
                    return true;
                }
            }

            if (movie.Title.IsNotNullOrWhiteSpace())
            {
                if (movie.Year > 1850)
                {
                    result = FindByTitle(movie.Title.CleanMovieTitle(), movie.Year);
                    if (result != null)
                    {
                        return true;
                    }
                }
                else
                {
                    result = FindByTitle(movie.Title.CleanMovieTitle());
                    if (result != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public List<int> GetRecommendedTmdbIds()
        {
            return _movieRepository.GetRecommendations();
        }

        public bool ExistsByMetadataId(int metadataId)
        {
            return _movieRepository.ExistsByMetadataId(metadataId);
        }

        public HashSet<int> AllMovieWithCollectionsTmdbIds()
        {
            return _movieRepository.AllMovieWithCollectionsTmdbIds();
        }

        private Movie ReturnSingleMovieOrThrow(List<Movie> movies, string edition = null)  // krzw(multi-edition): edition routes among one title's rows
        {
            if (movies.Count == 0)
            {
                return null;
            }

            if (movies.Count == 1)
            {
                return movies.First();
            }

            // krzw(multi-edition) begin: default + edition rows of one title are one movie; the parsed edition picks
            // the edition row it names, legacy callers (no edition) and unknown editions get the default row
            if (movies.All(m => m.MovieMetadataId > 0 && m.MovieMetadataId == movies[0].MovieMetadataId))
            {
                return EditionMatcher.SelectRow(movies, edition) ?? movies.First();
            }

            // krzw(multi-edition) end
            throw new MultipleMoviesFoundException(movies, "Expected one movie, but found {0}. Matching movies: {1}", movies.Count, string.Join(",", movies));
        }

        public void Handle(MovieFileAddedEvent message)
        {
            var movie = message.MovieFile.Movie;
            movie.MovieFileId = message.MovieFile.Id;
            _movieRepository.Update(movie);

            // _movieRepository.SetFileId(message.MovieFile.Id, message.MovieFile.Movie.Value.Id);
            _logger.Info("Assigning file [{0}] to movie [{1}]", message.MovieFile.RelativePath, message.MovieFile.Movie);
        }

        public void Handle(MovieFileDeletedEvent message)
        {
            foreach (var movie in GetMoviesByFileId(message.MovieFile.Id))
            {
                _logger.Debug("Detaching movie {0} from file.", movie.Id);
                movie.MovieFileId = 0;

                if (message.Reason != DeleteMediaFileReason.Upgrade && _configService.AutoUnmonitorPreviouslyDownloadedMovies)
                {
                    movie.Monitored = false;
                }

                UpdateMovie(movie);
            }
        }
    }
}
