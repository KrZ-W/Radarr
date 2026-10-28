// krzw(edition-runtime-catalog): rejects a file whose runtime contradicts its edition tag, or an untagged default-row file whose runtime names a sibling edition row that the re-home did not take it to
using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class EditionRuntimeImportSpecification : IImportDecisionEngineSpecification
    {
        private readonly IEditionRuntimeImportEvaluator _evaluator;
        private readonly Logger _logger;

        public EditionRuntimeImportSpecification(IEditionRuntimeImportEvaluator evaluator, Logger logger)
        {
            _evaluator = evaluator;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            if (localMovie.EditionRuntimeRehomed)
            {
                return ImportSpecDecision.Accept();
            }

            var result = _evaluator.Evaluate(localMovie);

            if (result == null)
            {
                return ImportSpecDecision.Accept();
            }

            if (result.Conflict != null)
            {
                _logger.Debug("Edition runtime check rejected {0}: {1}", localMovie.Path, result.Conflict);
                return ImportSpecDecision.Reject(ImportRejectionReason.EditionMismatch, result.Conflict);
            }

            if (result.RuntimeSibling != null)
            {
                var message = string.Format(EditionRuntimeImportEvaluator.SiblingMessage, EditionRuntimeImportEvaluator.Round(result.FileMinutes), result.RuntimeSibling.Edition);

                _logger.Debug("Edition runtime check rejected {0}: {1}", localMovie.Path, message);
                return ImportSpecDecision.Reject(ImportRejectionReason.EditionMismatch, message);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
