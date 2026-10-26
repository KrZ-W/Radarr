// krzw(multi-edition): with several edition rows sharing one folder, each row claims only the extras of its own movie file
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;

namespace NzbDrone.Core.Extras
{
    public interface IEditionExtraFileFilter
    {
        List<string> FilterClaimable(Movie movie, List<string> filesOnDisk);
    }

    public class EditionExtraFileFilter : IEditionExtraFileFilter
    {
        private readonly IMovieService _movieService;
        private readonly IMediaFileService _mediaFileService;
        private readonly Logger _logger;

        public EditionExtraFileFilter(IMovieService movieService,
                                      IMediaFileService mediaFileService,
                                      Logger logger)
        {
            _movieService = movieService;
            _mediaFileService = mediaFileService;
            _logger = logger;
        }

        // A row without edition siblings keeps upstream behaviour: every extra in the folder is its own. With
        // siblings, an extra is this row's when
        //   1. its name carries a {edition-X} tag naming this row (a tag naming a sibling hands it over);
        //   2. else the stem of this row's movie file is the longest movie-file stem the extra's name starts with
        //      ("Movie (2010).en.srt" -> "Movie (2010).mkv", "Movie (2010) {edition-Extended}.en.srt" -> the Extended file);
        //   3. else (folder-level files such as poster.jpg or movie.nfo match no stem) this is the default row, which
        //      keeps them exactly as upstream does; edition rows leave them.
        public List<string> FilterClaimable(Movie movie, List<string> filesOnDisk)
        {
            if (movie == null || movie.Id <= 0 || filesOnDisk == null || filesOnDisk.Count == 0)
            {
                return filesOnDisk;
            }

            var siblings = (_movieService.GetEditionSiblings(movie) ?? new List<Movie>()).Where(s => s != null).ToList();

            if (siblings.Empty())
            {
                return filesOnDisk;
            }

            var ownStems = (_mediaFileService.GetFilesByMovie(movie.Id) ?? new List<MovieFile>())
                           .Select(f => FileStem.Of(movie.Path, f.RelativePath))
                           .Where(s => s != null)
                           .ToList();

            var siblingStems = siblings.Where(s => s.MovieFile != null)
                                       .Select(s => FileStem.Of(movie.Path, s.MovieFile.RelativePath))
                                       .Where(s => s != null)
                                       .ToList();

            var claimable = new List<string>();

            foreach (var file in filesOnDisk)
            {
                if (Claims(movie, siblings, ownStems, siblingStems, file))
                {
                    claimable.Add(file);
                }
                else
                {
                    _logger.Debug("Extra file '{0}' belongs to another edition of {1}, leaving it", file, movie.Title);
                }
            }

            return claimable;
        }

        private static bool Claims(Movie movie, List<Movie> siblings, List<FileStem> ownStems, List<FileStem> siblingStems, string file)
        {
            var fileName = Path.GetFileName(file);
            var tag = Parser.Parser.ParseEditionTag(fileName);

            if (tag.IsNotNullOrWhiteSpace())
            {
                if (EditionMatcher.RowMatchesEdition(movie, tag))
                {
                    return true;
                }

                if (siblings.Any(s => EditionMatcher.RowMatchesEdition(s, tag)))
                {
                    return false;
                }
            }

            var own = BestScore(ownStems, file, fileName);
            var sibling = BestScore(siblingStems, file, fileName);

            if (own == null && sibling == null)
            {
                return movie.IsDefaultEdition;
            }

            if (own == null)
            {
                return false;
            }

            if (sibling == null)
            {
                return true;
            }

            // Equal stems in the same folder (same name, different container) are claimed by both rows: nothing
            // tells them apart until one is renamed with its edition tag.
            return own.Value >= sibling.Value;
        }

        // The longest stem the file name starts with wins; a stem in the extra's own folder beats one elsewhere.
        private static int? BestScore(List<FileStem> stems, string file, string fileName)
        {
            var directory = Path.GetDirectoryName(file) ?? "";
            int? best = null;

            foreach (var stem in stems)
            {
                if (!fileName.StartsWith(stem.Name, DiskProviderBase.PathStringComparison))
                {
                    continue;
                }

                var score = (stem.Name.Length * 2) + (stem.Directory.PathEquals(directory) ? 1 : 0);

                if (best == null || score > best.Value)
                {
                    best = score;
                }
            }

            return best;
        }

        private class FileStem
        {
            public string Directory { get; private set; }
            public string Name { get; private set; }

            public static FileStem Of(string moviePath, string relativePath)
            {
                if (moviePath.IsNullOrWhiteSpace() || relativePath.IsNullOrWhiteSpace())
                {
                    return null;
                }

                var name = Path.GetFileNameWithoutExtension(relativePath);

                if (name.IsNullOrWhiteSpace())
                {
                    return null;
                }

                return new FileStem
                {
                    Directory = Path.Combine(moviePath, Path.GetDirectoryName(relativePath) ?? ""),
                    Name = name
                };
            }
        }
    }
}
