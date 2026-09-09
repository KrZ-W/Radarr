export default interface MediaManagement {
  autoUnmonitorPreviouslyDownloadedMovies: boolean;
  recycleBin: string;
  recycleBinCleanupDays: number;
  downloadPropersAndRepacks: string;
  createEmptyMovieFolders: boolean;
  deleteEmptyFolders: boolean;
  fileDate: string;
  rescanAfterRefresh: string;
  setPermissionsLinux: boolean;
  chmodFolder: string;
  chownGroup: string;
  skipFreeSpaceCheckWhenImporting: boolean;
  minimumFreeSpaceWhenImporting: number;
  copyUsingHardlinks: boolean;
  useScriptImport: boolean;
  scriptImportPath: string;
  importExtraFiles: boolean;
  extraFileExtensions: string;
  enableMediaInfo: boolean;
  regionalTranslationVariants: string; // krzw(regional-translations)
  regionalTranslationSearchMode: string; // krzw(regional-translations)
  // krzw(audio-language-verification)
  audioLanguageVerificationEnabled: boolean;
  audioLanguageVerificationEndpoint: string;
  audioLanguageVerificationConfidenceThreshold: number;
  audioLanguageVerificationClipOffset: number;
  audioLanguageVerificationClipLength: number;
  audioLanguageVerificationVerifyTagged: string;
  audioLanguageVerificationVerifyTaggedGroups: string;
  audioLanguageVerificationTimeout: number;
}
