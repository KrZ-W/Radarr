// krzw(multi-edition): edition gate for imports - the EditionGate rule applied to the aggregated file edition; skipped for rows the user picked in manual import and for files already inside the library folder (spec section 7)
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

            if (localMovie.ExistingFile)
            {
                _logger.Debug("{0} is in movie folder, skipping edition gate", localMovie.Path);
                return ImportSpecDecision.Accept();
            }

            var movie = localMovie.Movie;

            if (movie == null)
            {
                return ImportSpecDecision.Accept();
            }

            var rejection = EditionGate.Evaluate(movie, localMovie.Edition, () => _movieService.GetEditionSiblings(movie.Id));

            if (rejection != null)
            {
                _logger.Debug("Edition gate rejected {0} for {1}: {2}", localMovie.Path, movie, rejection);
                return ImportSpecDecision.Reject(ImportRejectionReason.EditionMismatch, rejection);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
