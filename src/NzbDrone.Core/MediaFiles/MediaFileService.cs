using System.Collections.Generic;
using System.IO;
using System.Linq;
using NzbDrone.Common;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IMediaFileService
    {
        MovieFile Add(MovieFile movieFile);
        void Update(MovieFile movieFile);
        void Update(List<MovieFile> movieFile);
        void Delete(MovieFile movieFile, DeleteMediaFileReason reason);
        List<MovieFile> GetFilesByMovie(int movieId);
        List<MovieFile> GetFilesByMovies(IEnumerable<int> movieIds);
        List<MovieFile> GetFilesWithoutMediaInfo();
        List<string> FilterExistingFiles(List<string> files, Movie movie);
        List<MovieFile> GetSiblingEditionFiles(Movie movie);  // krzw(multi-edition)
        MovieFile GetMovie(int id);
        List<MovieFile> GetMovies(IEnumerable<int> ids);
        List<MovieFile> GetFilesWithRelativePath(int movieIds, string relativePath);
    }

    public class MediaFileService : IMediaFileService, IHandleAsync<MoviesDeletedEvent>
    {
        private readonly IMediaFileRepository _mediaFileRepository;
        private readonly IMovieRepository _movieRepository;
        private readonly IEventAggregator _eventAggregator;

        public MediaFileService(IMediaFileRepository mediaFileRepository,
                                IMovieRepository movieRepository,
                                IEventAggregator eventAggregator)
        {
            _mediaFileRepository = mediaFileRepository;
            _movieRepository = movieRepository;
            _eventAggregator = eventAggregator;
        }

        public MovieFile Add(MovieFile movieFile)
        {
            var addedFile = _mediaFileRepository.Insert(movieFile);
            if (addedFile.Movie == null)
            {
                addedFile.Movie = _movieRepository.Get(movieFile.MovieId);
            }

            _eventAggregator.PublishEvent(new MovieFileAddedEvent(addedFile));

            return addedFile;
        }

        public void Update(MovieFile movieFile)
        {
            _mediaFileRepository.Update(movieFile);
        }

        public void Update(List<MovieFile> movieFiles)
        {
            _mediaFileRepository.UpdateMany(movieFiles);
        }

        public void Delete(MovieFile movieFile, DeleteMediaFileReason reason)
        {
            // Little hack so we have the movie attached for the event consumers
            if (movieFile.Movie == null)
            {
                movieFile.Movie = _movieRepository.Get(movieFile.MovieId);
            }

            movieFile.Path = Path.Combine(movieFile.Movie.Path, movieFile.RelativePath);

            _mediaFileRepository.Delete(movieFile);
            _eventAggregator.PublishEvent(new MovieFileDeletedEvent(movieFile, reason));
        }

        public List<MovieFile> GetFilesByMovie(int movieId)
        {
            return _mediaFileRepository.GetFilesByMovie(movieId);
        }

        public List<MovieFile> GetFilesByMovies(IEnumerable<int> movieIds)
        {
            return _mediaFileRepository.GetFilesByMovies(movieIds);
        }

        public List<MovieFile> GetFilesWithoutMediaInfo()
        {
            return _mediaFileRepository.GetFilesWithoutMediaInfo();
        }

        public List<string> FilterExistingFiles(List<string> files, Movie movie)
        {
            var movieFiles = GetFilesByMovie(movie.Id).Select(f => Path.Combine(movie.Path, f.RelativePath)).ToList();

            movieFiles.AddRange(GetSiblingEditionFiles(movie).Select(f => Path.Combine(movie.Path, f.RelativePath)));  // krzw(multi-edition): the other edition rows' files share this folder and are not unmapped

            if (!movieFiles.Any())
            {
                return files;
            }

            return files.Except(movieFiles, PathEqualityComparer.Instance).ToList();
        }

        // krzw(multi-edition) begin
        // Files owned by the other rows of movie's title (they share its folder). One indexed query, empty for a
        // title without edition rows; rows that are not persisted yet have no siblings.
        public List<MovieFile> GetSiblingEditionFiles(Movie movie)
        {
            if (movie == null || movie.Id <= 0 || movie.MovieMetadataId <= 0)
            {
                return new List<MovieFile>();
            }

            return _mediaFileRepository.GetFilesBySiblingEditions(movie.MovieMetadataId, movie.Id) ?? new List<MovieFile>();
        }

        // krzw(multi-edition) end

        public List<MovieFile> GetMovies(IEnumerable<int> ids)
        {
            return _mediaFileRepository.Get(ids).ToList();
        }

        public MovieFile GetMovie(int id)
        {
            return _mediaFileRepository.Get(id);
        }

        public List<MovieFile> GetFilesWithRelativePath(int movieId, string relativePath)
        {
            return _mediaFileRepository.GetFilesWithRelativePath(movieId, relativePath);
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            _mediaFileRepository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        public static List<string> FilterExistingFiles(List<string> files, List<MovieFile> movieFiles, Movie movie)
        {
            var seriesFilePaths = movieFiles.Select(f => Path.Combine(movie.Path, f.RelativePath)).ToList();

            if (!seriesFilePaths.Any())
            {
                return files;
            }

            return files.Except(seriesFilePaths, PathEqualityComparer.Instance).ToList();
        }
    }
}
