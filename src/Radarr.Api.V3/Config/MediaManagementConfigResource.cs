using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.AudioTags;
using NzbDrone.Core.Qualities;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Config
{
    public class MediaManagementConfigResource : RestResource
    {
        public bool AutoUnmonitorPreviouslyDownloadedMovies { get; set; }
        public string RecycleBin { get; set; }
        public int RecycleBinCleanupDays { get; set; }
        public ProperDownloadTypes DownloadPropersAndRepacks { get; set; }
        public bool CreateEmptyMovieFolders { get; set; }
        public bool DeleteEmptyFolders { get; set; }
        public FileDateType FileDate { get; set; }
        public RescanAfterRefreshType RescanAfterRefresh { get; set; }
        public bool AutoRenameFolders { get; set; }
        public bool PathsDefaultStatic { get; set; }

        public bool SetPermissionsLinux { get; set; }
        public string ChmodFolder { get; set; }
        public string ChownGroup { get; set; }

        public bool SkipFreeSpaceCheckWhenImporting { get; set; }
        public int MinimumFreeSpaceWhenImporting { get; set; }
        public bool CopyUsingHardlinks { get; set; }
        public bool UseScriptImport { get; set; }
        public string ScriptImportPath { get; set; }
        public bool ImportExtraFiles { get; set; }
        public string ExtraFileExtensions { get; set; }
        public bool EnableMediaInfo { get; set; }

        // krzw(audio-language-verification)
        public bool AudioLanguageVerificationEnabled { get; set; }
        public string AudioLanguageVerificationEndpoint { get; set; }
        public double AudioLanguageVerificationConfidenceThreshold { get; set; }
        public int AudioLanguageVerificationClipOffset { get; set; }
        public int AudioLanguageVerificationClipLength { get; set; }
        public AudioLanguageVerifyTaggedMode AudioLanguageVerificationVerifyTagged { get; set; }
        public string AudioLanguageVerificationVerifyTaggedGroups { get; set; }
        public int AudioLanguageVerificationTimeout { get; set; }

        // krzw(audio-track-retag)
        public bool AudioTrackRetagEnabled { get; set; }
        public AudioTrackRetagHardlinkMode AudioTrackRetagHardlinkMode { get; set; }
    }

    public static class MediaManagementConfigResourceMapper
    {
        public static MediaManagementConfigResource ToResource(IConfigService model)
        {
            return new MediaManagementConfigResource
            {
                AutoUnmonitorPreviouslyDownloadedMovies = model.AutoUnmonitorPreviouslyDownloadedMovies,
                RecycleBin = model.RecycleBin,
                RecycleBinCleanupDays = model.RecycleBinCleanupDays,
                DownloadPropersAndRepacks = model.DownloadPropersAndRepacks,
                CreateEmptyMovieFolders = model.CreateEmptyMovieFolders,
                DeleteEmptyFolders = model.DeleteEmptyFolders,
                FileDate = model.FileDate,
                RescanAfterRefresh = model.RescanAfterRefresh,
                AutoRenameFolders = model.AutoRenameFolders,

                SetPermissionsLinux = model.SetPermissionsLinux,
                ChmodFolder = model.ChmodFolder,
                ChownGroup = model.ChownGroup,

                SkipFreeSpaceCheckWhenImporting = model.SkipFreeSpaceCheckWhenImporting,
                MinimumFreeSpaceWhenImporting = model.MinimumFreeSpaceWhenImporting,
                CopyUsingHardlinks = model.CopyUsingHardlinks,
                UseScriptImport = model.UseScriptImport,
                ScriptImportPath = model.ScriptImportPath,
                ImportExtraFiles = model.ImportExtraFiles,
                ExtraFileExtensions = model.ExtraFileExtensions,
                EnableMediaInfo = model.EnableMediaInfo,

                // krzw(audio-language-verification)
                AudioLanguageVerificationEnabled = model.AudioLanguageVerificationEnabled,
                AudioLanguageVerificationEndpoint = model.AudioLanguageVerificationEndpoint,
                AudioLanguageVerificationConfidenceThreshold = model.AudioLanguageVerificationConfidenceThreshold,
                AudioLanguageVerificationClipOffset = model.AudioLanguageVerificationClipOffset,
                AudioLanguageVerificationClipLength = model.AudioLanguageVerificationClipLength,
                AudioLanguageVerificationVerifyTagged = model.AudioLanguageVerificationVerifyTagged,
                AudioLanguageVerificationVerifyTaggedGroups = model.AudioLanguageVerificationVerifyTaggedGroups,
                AudioLanguageVerificationTimeout = model.AudioLanguageVerificationTimeout,

                // krzw(audio-track-retag)
                AudioTrackRetagEnabled = model.AudioTrackRetagEnabled,
                AudioTrackRetagHardlinkMode = model.AudioTrackRetagHardlinkMode
            };
        }
    }
}
