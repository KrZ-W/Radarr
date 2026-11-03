using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport
{
    // krzw(symlink-import-guard): one answer to "is this import source a symbolic link, and what does it point at?",
    // shared by the decision spec (first line), the import funnel (second line, catches entry points that skip the
    // specs such as the final Manual Import step) and the file mover (last line, runs right before the bytes are
    // touched so a link created between the decision and the transfer is still refused).
    public class SymlinkImportCheck
    {
        public string Target { get; set; }
        public MovieFile LibraryFile { get; set; }
        public ImportRejectionReason Reason { get; set; }
        public string Message { get; set; }

        public bool IsCurrentFile => Reason == ImportRejectionReason.SourceIsSymlinkToCurrentFile;
    }

    public interface ISymlinkImportGuard
    {
        // Null when the setting is off or the source is not a symbolic link.
        SymlinkImportCheck Check(LocalMovie localMovie);
    }

    public class SymlinkImportGuard : ISymlinkImportGuard
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigService _configService;
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public SymlinkImportGuard(IDiskProvider diskProvider,
                                  IConfigService configService,
                                  IMovieService movieService,
                                  IMediaFileService mediaFileService,
                                  Logger logger)
        {
            _diskProvider = diskProvider;
            _configService = configService;
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        public SymlinkImportCheck Check(LocalMovie localMovie)
        {
            if (!_configService.RejectSymlinkImportSources || localMovie?.Path.IsNullOrWhiteSpace() != false)
            {
                return null;
            }

            var target = _diskProvider.GetSymbolicLinkTarget(localMovie.Path);

            if (target == null)
            {
                return null;
            }

            var check = new SymlinkImportCheck
            {
                Target = target,
                Reason = ImportRejectionReason.SourceIsSymlink,
                Message = $"Source is a symbolic link → {target}"
            };

            var libraryFile = FindOwnFile(localMovie, target) ?? FindLibraryFile(target);

            if (libraryFile == null)
            {
                return check;
            }

            check.LibraryFile = libraryFile;

            if (localMovie.Movie != null && localMovie.Movie.MovieFileId > 0 && localMovie.Movie.MovieFileId == libraryFile.Id)
            {
                check.Reason = ImportRejectionReason.SourceIsSymlinkToCurrentFile;
                check.Message = $"Source is a symbolic link → {target}, which is already the current file of this movie";
            }
            else
            {
                check.Reason = ImportRejectionReason.SourceIsSymlinkToLibraryFile;
                check.Message = $"Source is a symbolic link → {target}, which is already the library file of {Describe(localMovie, libraryFile)}";
            }

            _logger.Trace("{0} resolves to library file [{1}] ({2})", localMovie.Path, libraryFile.Id, check.Reason);

            return check;
        }

        // The current file of the movie this candidate maps to: cheap, and the case completed download
        // handling needs to recognise (a seed payload relinked to the library copy it was imported as).
        private MovieFile FindOwnFile(LocalMovie localMovie, string target)
        {
            var movie = localMovie.Movie;

            if (movie?.Path == null || movie.MovieFileId <= 0)
            {
                return null;
            }

            return _mediaFileService.GetFilesByMovie(movie.Id)
                                    .FirstOrDefault(f => FullPath(movie.Path, f).PathEquals(target));
        }

        // Any tracked file of any movie (edition rows share a folder, so every row under the target's folder is
        // checked): the link points into the library, so importing it could only ever duplicate (or, as an
        // upgrade, destroy) something Radarr already has.
        private MovieFile FindLibraryFile(string target)
        {
            foreach (var (movieId, moviePath) in _movieService.AllMoviePaths())
            {
                if (moviePath.IsNullOrWhiteSpace() || !moviePath.IsParentPath(target))
                {
                    continue;
                }

                var file = _mediaFileService.GetFilesByMovie(movieId)
                                            .FirstOrDefault(f => FullPath(moviePath, f).PathEquals(target));

                if (file != null)
                {
                    return file;
                }
            }

            return null;
        }

        private string Describe(LocalMovie localMovie, MovieFile libraryFile)
        {
            var movie = localMovie.Movie?.Id == libraryFile.MovieId ? localMovie.Movie : _movieService.GetMovie(libraryFile.MovieId);

            if (movie == null)
            {
                return $"movie {libraryFile.MovieId} ({libraryFile.RelativePath ?? libraryFile.Path})";
            }

            var description = movie.Year > 0 ? $"{movie.Title} ({movie.Year})" : movie.Title;

            return movie.Edition.IsNotNullOrWhiteSpace() ? $"{description} [{movie.Edition}]" : description;
        }

        private static string FullPath(string moviePath, MovieFile file)
        {
            return file.Path.IsNotNullOrWhiteSpace() ? file.Path : Path.Combine(moviePath, file.RelativePath ?? string.Empty);
        }
    }
}
