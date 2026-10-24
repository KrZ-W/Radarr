using System.IO;  // krzw(multi-edition)
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators
{
    public class AggregateEdition : IAggregateLocalMovie
    {
        public int Order => 1;

        public LocalMovie Aggregate(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            // krzw(multi-edition) begin
            // An explicit Plex {edition-Name} tag on the file or its folder is a deliberate statement about this file
            // and beats every parsed guess, including the download client title (spec §6.1: tag first, then aggregate).
            var movieEdition = ParsePlexEditionTag(localMovie.Path);

            if (movieEdition.IsNullOrWhiteSpace())
            {
                movieEdition = localMovie.DownloadClientMovieInfo?.Edition;
            }

            // krzw(multi-edition) end

            if (movieEdition.IsNullOrWhiteSpace())
            {
                movieEdition = localMovie.FolderMovieInfo?.Edition;
            }

            if (movieEdition.IsNullOrWhiteSpace())
            {
                movieEdition = localMovie.FileMovieInfo?.Edition;
            }

            localMovie.Edition = movieEdition;

            return localMovie;
        }

        // krzw(multi-edition) begin
        private static string ParsePlexEditionTag(string path)
        {
            if (path.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var edition = Parser.Parser.ParseEditionTag(Path.GetFileName(path));

            if (edition.IsNullOrWhiteSpace())
            {
                edition = Parser.Parser.ParseEditionTag(Path.GetFileName(Path.GetDirectoryName(path)));
            }

            return edition;
        }

        // krzw(multi-edition) end
    }
}
