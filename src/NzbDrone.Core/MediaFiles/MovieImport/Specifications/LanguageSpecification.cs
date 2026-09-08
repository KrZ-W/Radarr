using NLog;
using NzbDrone.Core.Download;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles.MovieImport.Specifications
{
    public class LanguageSpecification : IImportDecisionEngineSpecification
    {
        private readonly Logger _logger;

        public LanguageSpecification(Logger logger)
        {
            _logger = logger;
        }

        public ImportSpecDecision IsSatisfiedBy(LocalMovie localMovie, DownloadClientItem downloadClientItem)
        {
            // krzw: language is a download-time gate. Files already inside the library (disk rescan,
            // DB rebuild, upgrade crash recovery) must map back regardless of their audio language,
            // matching MinimumCustomFormatScoreSpecification.
            if (localMovie.ExistingFile)
            {
                _logger.Debug("Existing file, skipping language check");
                return ImportSpecDecision.Accept();
            }

            var wantedLanguage = localMovie.Movie.QualityProfile.Language;

            if (wantedLanguage == Language.Any)
            {
                return ImportSpecDecision.Accept();
            }

            var languages = localMovie.Languages;

            if (wantedLanguage == Language.Original)
            {
                var originalLanguage = localMovie.Movie.MovieMetadata.Value.OriginalLanguage;
                if (!languages.Contains(originalLanguage))
                {
                    _logger.Debug("Original Language ({0}) is wanted, but found {1} in file. Skipping {2}", originalLanguage, languages.ToExtendedString(), localMovie.Path);
                    return ImportSpecDecision.Reject(ImportRejectionReason.WantedLanguage,
                        "File audio language {0} does not contain the profile's required Original Language ({1})",
                        languages.ToExtendedString(),
                        originalLanguage);
                }

                return ImportSpecDecision.Accept();
            }

            if (!languages.Contains(wantedLanguage))
            {
                _logger.Debug("Wanted language {0} not found in file languages {1}. Skipping {2}", wantedLanguage, languages.ToExtendedString(), localMovie.Path);
                return ImportSpecDecision.Reject(ImportRejectionReason.WantedLanguage,
                    "File audio language {0} does not contain the profile's required language ({1})",
                    languages.ToExtendedString(),
                    wantedLanguage);
            }

            return ImportSpecDecision.Accept();
        }
    }
}
