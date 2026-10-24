// krzw(multi-edition): edition gate for grabs - edition rows take only releases that name them (or untagged ones when they accept those), the default row refuses releases a sibling edition row claims (spec section 7)
using NLog;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.DecisionEngine.Specifications
{
    public class EditionGateSpecification : IDownloadDecisionEngineSpecification
    {
        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public EditionGateSpecification(IMovieService movieService, Logger logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        public SpecificationPriority Priority => SpecificationPriority.Database;
        public RejectionType Type => RejectionType.Permanent;

        public virtual DownloadSpecDecision IsSatisfiedBy(RemoteMovie subject, ReleaseDecisionInformation information)
        {
            var movie = subject.Movie;

            if (movie == null)
            {
                return DownloadSpecDecision.Accept();
            }

            var parsedEdition = subject.ParsedMovieInfo?.Edition;

            // routing (ParsingService) hands over the rows it loaded; only a release that arrived without them costs a query
            var rejection = EditionGate.Evaluate(movie, parsedEdition, () => subject.EditionSiblings ?? _movieService.GetEditionSiblings(movie));

            if (rejection != null)
            {
                _logger.Debug("Edition gate rejected release '{0}' for {1}: {2}", subject.Release?.Title, movie, rejection);
                return DownloadSpecDecision.Reject(DownloadRejectionReason.EditionMismatch, rejection);
            }

            return DownloadSpecDecision.Accept();
        }
    }
}
