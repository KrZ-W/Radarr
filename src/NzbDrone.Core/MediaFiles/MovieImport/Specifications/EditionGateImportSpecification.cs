// krzw(multi-edition): edition gate for imports - the EditionGate rule applied to the aggregated file edition; skipped for rows the user picked in manual import, applied to files already inside the library folder too (spec sections 7 and 9), and a download's grab history makes the grabbed-for row keep its files
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class EditionGateImportSpecification : IImportDecisionEngineSpecification
    {
        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public EditionGateImportSpecification(IMovieService movieService, Logger logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (localMovie.EditionOverride)
            {
                _logger.Debug("Movie row was picked by the user, skipping edition gate for {0}", localMovie.Path);
                return ImportSpecDecision.Accept();
            }

            // krzw(edition-runtime-catalog) begin
            if (localMovie.EditionRuntimeRehomed)
            {
                _logger.Debug("Movie row was chosen by runtime, skipping edition gate for {0}", localMovie.Path);
                return ImportSpecDecision.Accept();
            }

            // krzw(edition-runtime-catalog) end
            var movie = localMovie.Movie;

            if (movie == null)
            {
                return ImportSpecDecision.Accept();
            }

            // The grab history (AggregateReleaseInfo) names the rows a download was grabbed for; its files belong to
            // this row even when neither the client title nor the file name carries the edition.
            var grabbedForRow = localMovie.Release?.MovieIds?.Contains(movie.Id) == true;

            var rejection = EditionGate.Evaluate(movie, localMovie.Edition, () => _movieService.GetEditionSiblings(movie.Id), grabbedForRow);

            if (rejection != null)
            {
                _logger.Debug("Edition gate rejected {0} for {1}: {2}", localMovie.Path, movie, rejection);
                return ImportSpecDecision.Reject(ImportRejectionReason.EditionMismatch, rejection);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
