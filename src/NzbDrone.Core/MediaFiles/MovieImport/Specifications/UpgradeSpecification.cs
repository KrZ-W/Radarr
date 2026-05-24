using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class UpgradeSpecification : IImportDecisionEngineSpecification
    {
        private readonly IConfigService _configService;
        private readonly ICustomFormatCalculationService _formatService;
        private readonly Logger _logger;

        public UpgradeSpecification(IConfigService configService,
                                    ICustomFormatCalculationService formatService,
                                    Logger logger)
        {
            _configService = configService;
            _formatService = formatService;
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            var downloadPropersAndRepacks = _configService.DownloadPropersAndRepacks;
            var qualityProfile = localMovie.Movie.QualityProfile;
            var qualityComparer = new QualityModelComparer(qualityProfile);

            if (localMovie.Movie.MovieFileId > 0)
            {
                var movieFile = localMovie.Movie.MovieFile;

                if (movieFile == null)
                {
                    _logger.Trace("Unable to get movie file details from the DB. MovieId: {0} MovieFileId: {1}", localMovie.Movie.Id, localMovie.Movie.MovieFileId);

                    return ImportSpecDecision.Accept();
                }

                movieFile.Movie = localMovie.Movie;
                var currentCustomFormats = _formatService.ParseCustomFormat(movieFile);
                var currentPriorityScore = qualityProfile.CalculatePriorityFormatScore(currentCustomFormats);
                var newPriorityScore = qualityProfile.CalculatePriorityFormatScore(localMovie.CustomFormats);
                var currentFormatScore = qualityProfile.CalculateCustomFormatScore(currentCustomFormats);
                var newFormatScore = localMovie.CustomFormatScore;
                var newCustomFormats = localMovie.CustomFormats;

                // Priority CFs are compared BEFORE quality, matching grab-side UpgradableSpecification.
                // A higher priority score wins even if the new file is a quality downgrade.
                if (newPriorityScore > currentPriorityScore)
                {
                    if (currentFormatScore >= qualityProfile.CutoffFormatScore)
                    {
                        _logger.Debug("Priority CF upgrade blocked at import: existing meets CF cutoff. Existing: [{0}] ({1}). Cutoff: {2}",
                            currentCustomFormats.ConcatToString(),
                            currentFormatScore,
                            qualityProfile.CutoffFormatScore);
                        return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                            "Existing movie file meets custom format cutoff. Existing: [{0}] ({1}). Cutoff: {2}",
                            currentCustomFormats.ConcatToString(),
                            currentFormatScore,
                            qualityProfile.CutoffFormatScore);
                    }

                    if (newFormatScore < currentFormatScore + qualityProfile.MinUpgradeFormatScore)
                    {
                        _logger.Debug("Priority CF upgrade blocked at import: score increment {0} < minimum {1}",
                            newFormatScore - currentFormatScore,
                            qualityProfile.MinUpgradeFormatScore);
                        return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                            "Custom format score increment {0} is below the minimum {1} required for upgrade",
                            newFormatScore - currentFormatScore,
                            qualityProfile.MinUpgradeFormatScore);
                    }

                    _logger.Debug("Priority CF upgrade at import: [{0}] ({1}) > [{2}] ({3}), accepting regardless of quality",
                        newCustomFormats.ConcatToString(),
                        newPriorityScore,
                        currentCustomFormats.ConcatToString(),
                        currentPriorityScore);
                    return ImportSpecDecision.Accept();
                }

                if (newPriorityScore < currentPriorityScore)
                {
                    _logger.Debug("Priority CF downgrade at import: [{0}] ({1}) < [{2}] ({3}), rejecting regardless of quality",
                        newCustomFormats.ConcatToString(),
                        newPriorityScore,
                        currentCustomFormats.ConcatToString(),
                        currentPriorityScore);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                        "Priority custom format downgrade. Existing: [{0}] ({1}). New: [{2}] ({3}).",
                        currentCustomFormats.ConcatToString(),
                        currentPriorityScore,
                        newCustomFormats.ConcatToString(),
                        newPriorityScore);
                }

                // Priority scores equal — fall through to standard quality/revision/CF checks.
                var qualityCompare = qualityComparer.Compare(localMovie.Quality.Quality, movieFile.Quality.Quality);

                if (qualityCompare < 0)
                {
                    _logger.Debug("This file isn't a quality upgrade for movie. Existing quality: {0}. New Quality {1}. Skipping {2}", movieFile.Quality.Quality, localMovie.Quality.Quality, localMovie.Path);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotQualityUpgrade, "Not an upgrade for existing movie file. Existing quality: {0}. New Quality {1}.", movieFile.Quality.Quality, localMovie.Quality.Quality);
                }

                // Same quality, propers/repacks are preferred and it is not a revision update. Reject revision downgrade.

                if (qualityCompare == 0 &&
                    downloadPropersAndRepacks != ProperDownloadTypes.DoNotPrefer &&
                    localMovie.Quality.Revision.CompareTo(movieFile.Quality.Revision) < 0)
                {
                    _logger.Debug("This file isn't a quality revision upgrade for movie. Skipping {0}", localMovie.Path);
                    return ImportSpecDecision.Reject(ImportRejectionReason.NotRevisionUpgrade, "Not a quality revision upgrade for existing movie file(s)");
                }

                if (qualityCompare == 0 && newFormatScore < currentFormatScore)
                {
                    _logger.Debug("New item's custom formats [{0}] ({1}) do not improve on [{2}] ({3}), skipping",
                        newCustomFormats != null ? newCustomFormats.ConcatToString() : "",
                        newFormatScore,
                        currentCustomFormats != null ? currentCustomFormats.ConcatToString() : "",
                        currentFormatScore);

                    return ImportSpecDecision.Reject(ImportRejectionReason.NotCustomFormatUpgrade,
                        "Not a Custom Format upgrade for existing movie file(s). New: [{0}] ({1}) do not improve on Existing: [{2}] ({3})",
                        newCustomFormats != null ? newCustomFormats.ConcatToString() : "",
                        newFormatScore,
                        currentCustomFormats != null ? currentCustomFormats.ConcatToString() : "",
                        currentFormatScore);
                }

                _logger.Debug("New item's custom formats [{0}] ({1}) do improve on [{2}] ({3}), accepting",
                    newCustomFormats != null ? newCustomFormats.ConcatToString() : "",
                    newFormatScore,
                    currentCustomFormats != null ? currentCustomFormats.ConcatToString() : "",
                    currentFormatScore);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
