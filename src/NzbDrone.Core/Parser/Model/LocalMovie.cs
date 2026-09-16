using System.Collections.Generic;
using NzbDrone.Common.Disk;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Download;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Parser.Model
{
    public class LocalMovie
    {
        public LocalMovie()
        {
            CustomFormats = new List<CustomFormat>();
        }

        public string Path { get; set; }
        public long Size { get; set; }
        public ParsedMovieInfo FileMovieInfo { get; set; }
        public ParsedMovieInfo DownloadClientMovieInfo { get; set; }
        public DownloadClientItem DownloadItem { get; set; }
        public ParsedMovieInfo FolderMovieInfo { get; set; }
        public Movie Movie { get; set; }
        public List<DeletedMovieFile> OldFiles { get; set; }
        public QualityModel Quality { get; set; }
        public List<Language> Languages { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public MediaInfoModel MediaInfo { get; set; }
        public bool ExistingFile { get; set; }
        public bool SceneSource { get; set; }
        public string ReleaseGroup { get; set; }
        public string Edition { get; set; }
        public string SceneName { get; set; }
        public bool OtherVideoFiles { get; set; }
        public List<CustomFormat> CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public GrabbedReleaseInfo Release { get; set; }
        public bool ScriptImported { get; set; }
        public string FileNameBeforeRename { get; set; }
        public bool ShouldImportExtras { get; set; }
        public List<string> PossibleExtraFiles { get; set; }
        public SubtitleTitleInfo SubtitleInfo { get; set; }

        // krzw(audio-language-verification): per-track probe outcome and why it ran; carried to MovieFile at import
        public List<AudioLanguageVerification> AudioLanguageVerification { get; set; }
        public AudioLanguageTrigger AudioLanguageTrigger { get; set; }

        // krzw(audio-track-retag): how the file actually reached the library (hardlink hint for the retag)
        public TransferMode? TransferMode { get; set; }

        // krzw(grabbed-release-title): sanitised title of the release this file was grabbed as.
        // CustomFormats above is the scoring ladder's result - the formats CustomFormatScore is computed
        // from - so the list and the score reported to notifications, scripts and history always agree.
        public string GrabbedReleaseTitle { get; set; }

        // krzw(grabbed-release-title): naming is the single deliberate carve-out. FileNameBuilder renders
        // the {Custom Formats} token from this legacy-ladder list, so switching the setting on never makes
        // Rename Files propose a library-wide rename. Left unset it falls back to CustomFormats, which is
        // exactly what naming read before this feature, so a LocalMovie built outside ImportDecisionMaker
        // or ManualImportService keeps its old behaviour and never renders a null.
        private List<CustomFormat> _namingCustomFormats;

        public List<CustomFormat> NamingCustomFormats
        {
            get => _namingCustomFormats ?? CustomFormats;
            set => _namingCustomFormats = value;
        }

        public override string ToString()
        {
            return Path;
        }
    }
}
