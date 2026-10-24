using System;
using System.IO;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.MediaFiles
{
    public interface IDeleteMediaFiles
    {
        void DeleteMovieFile(Movie movie, MovieFile movieFile);
    }

    public class MediaFileDeletionService : IDeleteMediaFiles,
                                            IHandleAsync<MoviesDeletedEvent>,
                                            IHandle<MovieFileDeletedEvent>
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMovieService _movieService;
        private readonly IConfigService _configService;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public MediaFileDeletionService(IDiskProvider diskProvider,
                                        IRecycleBinProvider recycleBinProvider,
                                        IMediaFileService mediaFileService,
                                        IMovieService movieService,
                                        IConfigService configService,
                                        IEventAggregator eventAggregator,
                                        Logger logger)
        {
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _movieService = movieService;
            _configService = configService;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public void DeleteMovieFile(Movie movie, MovieFile movieFile)
        {
            var fullPath = Path.Combine(movie.Path, movieFile.RelativePath);
            var rootFolder = _diskProvider.GetParentFolder(movie.Path);

            if (!_diskProvider.FolderExists(rootFolder))
            {
                _logger.Warn("Movie's root folder ({0}) doesn't exist.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) doesn't exist.", rootFolder);
            }

            if (_diskProvider.GetDirectories(rootFolder).Empty())
            {
                _logger.Warn("Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
                throw new NzbDroneClientException(HttpStatusCode.Conflict, "Movie's root folder ({0}) is empty. Rescan will not update movies as a failsafe.", rootFolder);
            }

            if (_diskProvider.FolderExists(movie.Path) && _diskProvider.FileExists(fullPath))
            {
                _logger.Info("Deleting movie file: {0}", fullPath);

                var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));

                try
                {
                    _recycleBinProvider.DeleteFile(fullPath, subfolder);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to delete movie file");
                    throw new NzbDroneClientException(HttpStatusCode.InternalServerError, "Unable to delete movie file");
                }
            }

            // Delete the movie file from the database to clean it up even if the file was already deleted
            _mediaFileService.Delete(movieFile, DeleteMediaFileReason.Manual);

            _eventAggregator.PublishEvent(new DeleteCompletedEvent());
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            if (message.DeleteFiles)
            {
                var allMovies = _movieService.AllMoviePaths();

                foreach (var movie in message.Movies)
                {
                    var sharedWithEditions = false;  // krzw(multi-edition): the title's other rows keep the folder

                    foreach (var s in allMovies)
                    {
                        if (s.Key == movie.Id)
                        {
                            continue;
                        }

                        if (movie.Path.IsParentPath(s.Value))
                        {
                            _logger.Error("Movie path: '{0}' is a parent of another movie, not deleting files.", movie.Path);
                            return;
                        }

                        if (movie.Path.PathEquals(s.Value))
                        {
                            // krzw(multi-edition) begin
                            if (IsEditionSibling(movie, s.Key))
                            {
                                sharedWithEditions = true;
                                continue;
                            }

                            // krzw(multi-edition) end
                            _logger.Error("Movie path: '{0}' is the same as another movie, not deleting files.", movie.Path);
                            return;
                        }
                    }

                    // krzw(multi-edition) begin
                    if (sharedWithEditions)
                    {
                        DeleteOwnMovieFile(movie);
                        continue;
                    }

                    // krzw(multi-edition) end
                    if (_diskProvider.FolderExists(movie.Path))
                    {
                        _recycleBinProvider.DeleteFolder(movie.Path);
                    }
                }

                _eventAggregator.PublishEvent(new DeleteCompletedEvent());
            }
        }

        // krzw(multi-edition) begin
        // The other row at the same path is one of this title's editions (same metadata), not a foreign movie.
        private bool IsEditionSibling(Movie movie, int otherMovieId)
        {
            if (movie.MovieMetadataId <= 0)
            {
                return false;
            }

            var other = _movieService.GetMovie(otherMovieId);

            return other != null && other.MovieMetadataId == movie.MovieMetadataId;
        }

        // Only this row's own file goes to the recycle bin; the folder stays with the title's other editions. No
        // error is logged: sharing the folder is by design.
        private void DeleteOwnMovieFile(Movie movie)
        {
            var movieFile = movie.MovieFile;

            if (movieFile == null || movieFile.RelativePath.IsNullOrWhiteSpace())
            {
                _logger.Debug("{0} has no file of its own, leaving the shared folder '{1}' to its other editions", movie, movie.Path);
                return;
            }

            var fullPath = Path.Combine(movie.Path, movieFile.RelativePath);

            if (!_diskProvider.FileExists(fullPath))
            {
                _logger.Debug("Movie file '{0}' no longer exists, leaving the shared folder to the other editions", fullPath);
                return;
            }

            _logger.Info("Deleting movie file '{0}'; the folder stays with the other editions of {1}", fullPath, movie);

            var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(fullPath));

            try
            {
                _recycleBinProvider.DeleteFile(fullPath, subfolder);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Unable to delete movie file '{0}'", fullPath);
            }
        }

        // krzw(multi-edition) end

        [EventHandleOrder(EventHandleOrder.Last)]
        public void Handle(MovieFileDeletedEvent message)
        {
            if (_configService.DeleteEmptyFolders)
            {
                var movie = message.MovieFile.Movie;
                var moviePath = movie.Path;
                var folder = message.MovieFile.Path.GetParentPath();

                while (moviePath.IsParentPath(folder))
                {
                    if (_diskProvider.FolderExists(folder))
                    {
                        _diskProvider.RemoveEmptySubfolders(folder);
                    }

                    folder = folder.GetParentPath();
                }

                _diskProvider.RemoveEmptySubfolders(moviePath);

                if (_diskProvider.FolderEmpty(moviePath))
                {
                    _diskProvider.DeleteFolder(moviePath, true);
                }
            }
        }
    }
}
