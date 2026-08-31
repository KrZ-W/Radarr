using System;
using System.IO;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.MediaFiles
{
    public interface IUpgradeMediaFiles
    {
        MovieFileMoveResult UpgradeMovieFile(MovieFile movieFile, LocalMovie localMovie, bool copyOnly = false);
        void FinalizeUpgrade(MovieFileMoveResult moveResult);
        void RollbackUpgrade(MovieFileMoveResult moveResult);
    }

    public class UpgradeMediaFileService : IUpgradeMediaFiles
    {
        // Suffix appended when parking an existing file aside. Not a recognised video extension, so a
        // library scan will never pick a parked file up as an importable media file.
        private const string ParkedFileSuffix = ".krzw-upgrade-bak";

        private readonly IRecycleBinProvider _recycleBinProvider;
        private readonly IMediaFileService _mediaFileService;
        private readonly IMoveMovieFiles _movieFileMover;
        private readonly IDiskProvider _diskProvider;
        private readonly Logger _logger;

        public UpgradeMediaFileService(IRecycleBinProvider recycleBinProvider,
                                       IMediaFileService mediaFileService,
                                       IMoveMovieFiles movieFileMover,
                                       IDiskProvider diskProvider,
                                       Logger logger)
        {
            _recycleBinProvider = recycleBinProvider;
            _mediaFileService = mediaFileService;
            _movieFileMover = movieFileMover;
            _diskProvider = diskProvider;
            _logger = logger;
        }

        public MovieFileMoveResult UpgradeMovieFile(MovieFile movieFile, LocalMovie localMovie, bool copyOnly = false)
        {
            _logger.Trace("Upgrading movie file.");

            var moveFileResult = new MovieFileMoveResult();
            moveFileResult.SourcePath = localMovie.Path;

            var existingFile = localMovie.Movie.MovieFileId > 0 ? localMovie.Movie.MovieFile : null;

            var rootFolder = _diskProvider.GetParentFolder(localMovie.Movie.Path);

            // If there are existing movie files and the root folder is missing, throw, so the old file isn't left behind during the import process.
            if (existingFile != null && !_diskProvider.FolderExists(rootFolder))
            {
                throw new RootFolderNotFoundException($"Root folder '{rootFolder}' was not found.");
            }

            // Park the existing file aside (rename, do NOT delete). This frees the destination slot for
            // the incoming file while keeping the original fully recoverable. The actual deletion is
            // deferred to FinalizeUpgrade, which the caller invokes only after the replacement's DB row
            // has been written. On any failure before that point the original is restored.
            if (existingFile != null)
            {
                var movieFilePath = Path.Combine(localMovie.Movie.Path, existingFile.RelativePath);
                var subfolder = rootFolder.GetRelativePath(_diskProvider.GetParentFolder(movieFilePath));

                var pending = new PendingUpgradeFile
                {
                    MovieFile = existingFile,
                    OriginalPath = movieFilePath,
                    Subfolder = subfolder,
                    BackupPath = null
                };

                if (_diskProvider.FileExists(movieFilePath))
                {
                    var backupPath = movieFilePath + ParkedFileSuffix;

                    // Clear a stale parked file left by a previously interrupted run.
                    if (_diskProvider.FileExists(backupPath))
                    {
                        _diskProvider.DeleteFile(backupPath);
                    }

                    _logger.Debug("Parking existing movie file before upgrade: {0} -> {1}", movieFilePath, backupPath);
                    _diskProvider.MoveFile(movieFilePath, backupPath);
                    pending.BackupPath = backupPath;
                }
                else
                {
                    _logger.Warn("Existing movie file missing from disk, nothing to park: {0}", movieFilePath);
                }

                moveFileResult.PendingUpgrades.Add(pending);
            }

            localMovie.OldFiles = moveFileResult.OldFiles;

            try
            {
                if (copyOnly)
                {
                    moveFileResult.MovieFile = _movieFileMover.CopyMovieFile(movieFile, localMovie);
                }
                else
                {
                    moveFileResult.MovieFile = _movieFileMover.MoveMovieFile(movieFile, localMovie);
                }
            }
            catch
            {
                // The replacement transfer failed. Restore the parked original so the slot is never left
                // empty, then rethrow for the caller to record the failed import. No DB rows have been
                // touched yet, so nothing needs to be re-added.
                RestoreParkedFiles(moveFileResult);
                throw;
            }

            moveFileResult.NewFilePath = Path.Combine(localMovie.Movie.Path, moveFileResult.MovieFile.RelativePath);

            return moveFileResult;
        }

        public void FinalizeUpgrade(MovieFileMoveResult moveResult)
        {
            // The replacement is on disk and in the database. Now remove the parked original(s): send them
            // to the recycle bin and delete their DB rows (which raises the movieFileDeleted/Upgrade
            // event). A failure here must never fail the already-committed import.
            foreach (var pending in moveResult.PendingUpgrades)
            {
                string recycleBinPath = null;

                if (pending.BackupPath != null)
                {
                    try
                    {
                        // Recycle under the original filename when the slot is free (the replacement went
                        // to a different path); otherwise recycle the parked file as-is.
                        if (pending.OriginalPath != moveResult.NewFilePath && !_diskProvider.FileExists(pending.OriginalPath))
                        {
                            _diskProvider.MoveFile(pending.BackupPath, pending.OriginalPath);
                            recycleBinPath = _recycleBinProvider.DeleteFile(pending.OriginalPath, pending.Subfolder);
                        }
                        else
                        {
                            recycleBinPath = _recycleBinProvider.DeleteFile(pending.BackupPath, pending.Subfolder);
                        }
                    }
                    catch (Exception e)
                    {
                        _logger.Warn(e, "Upgrade committed but unable to recycle parked original '{0}'; attempting permanent delete", pending.BackupPath);

                        try
                        {
                            // The file is either still parked or was renamed back just before the
                            // recycle failed; clean up whichever location holds it.
                            if (_diskProvider.FileExists(pending.BackupPath))
                            {
                                _diskProvider.DeleteFile(pending.BackupPath);
                            }
                            else if (pending.OriginalPath != moveResult.NewFilePath && _diskProvider.FileExists(pending.OriginalPath))
                            {
                                _diskProvider.DeleteFile(pending.OriginalPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warn(ex, "Unable to delete parked original '{0}'; it may need manual cleanup", pending.BackupPath);
                        }
                    }
                }

                _mediaFileService.Delete(pending.MovieFile, DeleteMediaFileReason.Upgrade);
                moveResult.OldFiles.Add(new DeletedMovieFile(pending.MovieFile, recycleBinPath));
            }
        }

        public void RollbackUpgrade(MovieFileMoveResult moveResult)
        {
            // The import failed after the replacement was placed but before it was committed to the DB.
            // Put the replacement back where it came from (or drop the copy), then restore the parked
            // original(s).
            if (moveResult.NewFilePath.IsNotNullOrWhiteSpace() && _diskProvider.FileExists(moveResult.NewFilePath))
            {
                try
                {
                    RemoveOrReturnToSource(moveResult.NewFilePath, moveResult.SourcePath);
                }
                catch (Exception e)
                {
                    _logger.Warn(e, "Unable to remove failed-import replacement file '{0}'", moveResult.NewFilePath);
                }
            }

            RestoreParkedFiles(moveResult);
        }

        private void RestoreParkedFiles(MovieFileMoveResult moveResult)
        {
            foreach (var pending in moveResult.PendingUpgrades)
            {
                if (pending.BackupPath == null)
                {
                    continue;
                }

                try
                {
                    if (!_diskProvider.FileExists(pending.BackupPath))
                    {
                        continue;
                    }

                    // The original slot may still hold a stray replacement (same-path upgrade); clear it
                    // so the genuine original can be put back.
                    if (_diskProvider.FileExists(pending.OriginalPath))
                    {
                        RemoveOrReturnToSource(pending.OriginalPath, moveResult.SourcePath);
                    }

                    _logger.Debug("Restoring parked original after failed upgrade: {0} -> {1}", pending.BackupPath, pending.OriginalPath);
                    _diskProvider.MoveFile(pending.BackupPath, pending.OriginalPath);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Unable to restore parked original '{0}' to '{1}' after a failed upgrade; the file remains parked and may need manual recovery", pending.BackupPath, pending.OriginalPath);
                }
            }
        }

        private void RemoveOrReturnToSource(string path, string sourcePath)
        {
            // A missing source means the replacement was moved out of the download location; return it
            // so the download client item stays importable on a later attempt. A present source means
            // it was copied or hardlinked, so the stray library copy can simply be removed.
            if (sourcePath.IsNotNullOrWhiteSpace() && !_diskProvider.FileExists(sourcePath))
            {
                _logger.Debug("Returning failed-import replacement to its source: {0} -> {1}", path, sourcePath);
                _diskProvider.MoveFile(path, sourcePath);
            }
            else
            {
                _diskProvider.DeleteFile(path);
            }
        }
    }
}
