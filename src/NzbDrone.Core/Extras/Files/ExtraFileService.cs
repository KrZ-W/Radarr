using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Events;

namespace NzbDrone.Core.Extras.Files
{
    public interface IExtraFileService<TExtraFile>
        where TExtraFile : ExtraFile, new()
    {
        List<TExtraFile> GetFilesByMovie(int movieId);
        List<TExtraFile> GetFilesByMovieFile(int movieFileId);
        TExtraFile FindByPath(int movieId, string path);
        void Upsert(TExtraFile extraFile);
        void Upsert(List<TExtraFile> extraFiles);
        void Delete(int id);
        void DeleteMany(IEnumerable<int> ids);
    }

    public abstract class ExtraFileService<TExtraFile> : IExtraFileService<TExtraFile>,
                                                         IHandleAsync<MovieFileDeletedEvent>,
                                                         IHandleAsync<MoviesDeletedEvent>
        where TExtraFile : ExtraFile, new()
    {
        private readonly IExtraFileRepository<TExtraFile> _repository;
        private readonly IMovieService _movieService;
        private readonly IDiskProvider _diskProvider;
        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly Logger _logger;

        public ExtraFileService(IExtraFileRepository<TExtraFile> repository,
                                IMovieService movieService,
                                IDiskProvider diskProvider,
                                IRecycleBinProvider recycleBinProvider,
                                Logger logger)
        {
            _repository = repository;
            _movieService = movieService;
            _diskProvider = diskProvider;
            _recycleBinProvider = recycleBinProvider;
            _logger = logger;
        }

        public List<TExtraFile> GetFilesByMovie(int movieId)
        {
            return _repository.GetFilesByMovie(movieId);
        }

        public List<TExtraFile> GetFilesByMovieFile(int movieFileId)
        {
            return _repository.GetFilesByMovieFile(movieFileId);
        }

        public TExtraFile FindByPath(int movieId, string path)
        {
            return _repository.FindByPath(movieId, path);
        }

        public void Upsert(TExtraFile extraFile)
        {
            Upsert(new List<TExtraFile> { extraFile });
        }

        public void Upsert(List<TExtraFile> extraFiles)
        {
            extraFiles.ForEach(m =>
            {
                m.LastUpdated = DateTime.UtcNow;

                if (m.Id == 0)
                {
                    m.Added = m.LastUpdated;
                }
            });

            _repository.InsertMany(extraFiles.Where(m => m.Id == 0).ToList());
            _repository.UpdateMany(extraFiles.Where(m => m.Id > 0).ToList());
        }

        public void Delete(int id)
        {
            _repository.Delete(id);
        }

        public void DeleteMany(IEnumerable<int> ids)
        {
            _repository.DeleteMany(ids);
        }

        public void HandleAsync(MoviesDeletedEvent message)
        {
            // krzw(multi-edition) begin: an edition row leaving a folder it shared takes its file's extras along
            if (message.DeleteFiles)
            {
                foreach (var movie in message.Movies)
                {
                    RecycleExtrasOfSharedFolderRow(movie);
                }
            }

            // krzw(multi-edition) end
            _repository.DeleteForMovies(message.Movies.Select(m => m.Id).ToList());
        }

        // krzw(multi-edition) begin
        // Deleting a row whose folder stays with the title's other editions recycles only that row's movie file
        // (MediaFileDeletionService.DeleteOwnMovieFile), and no MovieFileDeletedEvent can follow because the row is
        // gone. So the extras linked to that file are recycled here, before their rows are dropped. A row with no
        // remaining sibling at its path keeps upstream behaviour: the whole folder is recycled elsewhere.
        private void RecycleExtrasOfSharedFolderRow(Movie movie)
        {
            if (movie == null || movie.MovieFileId <= 0 || movie.Path.IsNullOrWhiteSpace())
            {
                return;
            }

            var siblings = (_movieService.GetEditionSiblings(movie) ?? new List<Movie>())
                           .Where(s => s != null && s.Path.IsNotNullOrWhiteSpace() && s.Path.PathEquals(movie.Path))
                           .ToList();

            if (siblings.Empty())
            {
                return;
            }

            // an extra at a path a sibling's extra also claims (equal stems, see EditionExtraFileFilter) stays with the sibling
            var pathsOwnedBySiblings = siblings.SelectMany(s => _repository.GetFilesByMovie(s.Id) ?? new List<TExtraFile>())
                                               .Select(e => e.RelativePath)
                                               .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var extra in _repository.GetFilesByMovieFile(movie.MovieFileId) ?? new List<TExtraFile>())
            {
                var path = Path.Combine(movie.Path, extra.RelativePath);

                if (pathsOwnedBySiblings.Contains(extra.RelativePath))
                {
                    _logger.Debug("Not recycling extra '{0}': the path belongs to another edition of {1}", path, movie);
                    continue;
                }

                if (!_diskProvider.FileExists(path))
                {
                    continue;
                }

                _logger.Debug("Recycling extra '{0}' of the deleted {1}; the folder stays with its other editions", path, movie);

                var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(path));
                _recycleBinProvider.DeleteFile(path, subfolder);
            }
        }

        // krzw(multi-edition) end

        public void HandleAsync(MovieFileDeletedEvent message)
        {
            var movieFile = message.MovieFile;

            if (message.Reason == DeleteMediaFileReason.NoLinkedEpisodes)
            {
                _logger.Debug("Removing movie file from DB as part of cleanup routine, not deleting extra files from disk.");
            }
            else
            {
                var movie = _movieService.GetMovie(message.MovieFile.MovieId);

                // krzw(atomic-upgrade): an upgrade to the same file name recreates the extras under the
                // old paths right after the old file's delete event fires, and this handler is async. If
                // the replacement's extras already own a path, the file there is theirs: leave it alone.
                var pathsOwnedByOtherFiles = _repository.GetFilesByMovie(movieFile.MovieId)
                    .Where(e => e.MovieFileId.HasValue && e.MovieFileId.Value != movieFile.Id)
                    .Select(e => e.RelativePath)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var extra in _repository.GetFilesByMovieFile(movieFile.Id))
                {
                    var path = Path.Combine(movie.Path, extra.RelativePath);

                    if (pathsOwnedByOtherFiles.Contains(extra.RelativePath))
                    {
                        _logger.Debug("Not recycling extra '{0}': the path now belongs to another movie file", path);
                        continue;
                    }

                    if (_diskProvider.FileExists(path))
                    {
                        // Send to the recycling bin so they can be recovered if necessary
                        var subfolder = _diskProvider.GetParentFolder(movie.Path).GetRelativePath(_diskProvider.GetParentFolder(path));
                        _recycleBinProvider.DeleteFile(path, subfolder);
                    }
                }
            }

            _logger.Debug("Deleting Extra from database for movie file: {0}", movieFile);
            _repository.DeleteForMovieFile(movieFile.Id);
        }
    }
}
