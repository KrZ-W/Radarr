namespace NzbDrone.Core.MediaFiles.MovieImport;

public enum ImportRejectionReason
{
    Unknown,
    FileLocked,
    UnknownMovie,
    DangerousFile,
    ExecutableFile,
    ArchiveFile,
    MovieFolder,
    InvalidFilePath,
    UnsupportedExtension,
    InvalidMovie,
    UnableToParse,
    Error,
    DecisionError,
    MovieAlreadyImported,
    MinimumFreeSpace,
    NoAudio,
    MovieNotFoundInRelease,
    Sample,
    SampleIndeterminate,
    Unpacking,
    MultiPartMovie,
    NotQualityUpgrade,
    NotRevisionUpgrade,
    NotCustomFormatUpgrade,
    WantedLanguage,  // krzw(import-enforcement)
    CustomFormatMinimumScore,  // krzw(import-enforcement)
    SourceIsSymlink,  // krzw(symlink-import-guard)
    SourceIsSymlinkToLibraryFile,  // krzw(symlink-import-guard): the link resolves to a tracked library file of another movie
    SourceIsSymlinkToCurrentFile,  // krzw(symlink-import-guard): the link resolves to the current file of the very movie it maps to
    EditionMismatch  // krzw(multi-edition)
}
