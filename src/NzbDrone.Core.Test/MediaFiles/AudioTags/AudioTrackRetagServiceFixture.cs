using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Processes;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.AudioTags;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.AudioTags
{
    [TestFixture]
    public class AudioTrackRetagServiceFixture : CoreTest<AudioTrackRetagService>
    {
        private const string MoviePath = "/movies/Movie (2020)";
        private const string LibraryPath = "/movies/Movie (2020)/Movie (2020).mkv";
        private const string TempPath = LibraryPath + AudioTrackRetagService.TempSuffix;
        private const string BackupPath = LibraryPath + AudioTrackRetagService.BackupSuffix;

        private Movie _movie;
        private MovieFile _movieFile;
        private LocalMovie _localMovie;
        private List<string> _probedTags;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie { Id = 7, Path = MoviePath };
            _movieFile = new MovieFile
            {
                Id = 42,
                MovieId = 7,
                Movie = _movie,
                RelativePath = "Movie (2020).mkv",
                Path = "/downloads/Movie.2020.FRENCH.1080p.mkv",
                Languages = new List<Language> { Language.French },
                MediaInfo = new MediaInfoModel { AudioLanguages = new List<string> { "eng" } },
                AudioLanguageVerification = new List<AudioLanguageVerification>
                {
                    new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "fr", Confidence = 0.99, ProbedAt = DateTime.UtcNow }
                }
            };
            _localMovie = new LocalMovie { Movie = _movie, Path = _movieFile.Path, TransferMode = TransferMode.Copy };
            _probedTags = new List<string> { "fre" };

            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioTrackRetagEnabled).Returns(true);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioTrackRetagHardlinkMode).Returns(AudioTrackRetagHardlinkMode.Skip);
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioLanguageVerificationConfidenceThreshold).Returns(0.85);

            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(LibraryPath)).Returns(true);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(1);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetFileSize(LibraryPath)).Returns(5_000_000_000L);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetParentFolder(LibraryPath)).Returns(MoviePath);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetAvailableSpace(MoviePath)).Returns(50_000_000_000L);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.TryRenameFile(TempPath, LibraryPath)).Returns(true);

            // ffprobe re-probe: MediaInfo.AudioLanguages drops empty tags (like VideoFileInfoReader does),
            // the layout reader keeps one entry per stream (like the verification feature reads them)
            Mocker.GetMock<IVideoFileInfoReader>()
                  .Setup(r => r.GetMediaInfo(It.IsAny<string>()))
                  .Returns(() => new MediaInfoModel { AudioLanguages = _probedTags.Where(t => t.IsNotNullOrWhiteSpace()).ToList(), SchemaRevision = 14 });
            Mocker.GetMock<IAudioTrackLayoutReader>()
                  .Setup(r => r.Read(It.IsAny<MediaInfoModel>()))
                  .Returns(() => _probedTags.Select((t, i) => new AudioTrackInfo { StreamIndex = i, Codec = "aac", Channels = 2, Language = t }).ToList());

            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetMovie(42)).Returns(_movieFile);
        }

        private void GivenHardlinked()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(2);
        }

        private void GivenMode(AudioTrackRetagHardlinkMode mode)
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioTrackRetagHardlinkMode).Returns(mode);
        }

        private void Import(bool newDownload = true)
        {
            Subject.Handle(new MovieFileImportedEvent(_localMovie, _movieFile, new List<DeletedMovieFile>(), newDownload, null));
        }

        private void VerifyNoEdit()
        {
            Mocker.GetMock<IAudioTrackTagger>().Verify(t => t.SetLanguages(It.IsAny<string>(), It.IsAny<IReadOnlyList<AudioTrackRetagTrack>>(), It.IsAny<TimeSpan>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
        }

        private void VerifyNoUpdate()
        {
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
            _movieFile.AudioTrackRetag.Should().BeNull();
        }

        private void VerifyEdited(string path)
        {
            Mocker.GetMock<IAudioTrackTagger>().Verify(t => t.SetLanguages(path, It.Is<IReadOnlyList<AudioTrackRetagTrack>>(e => e.Count == 1 && e[0].StreamIndex == 0 && e[0].To == "fre"), AudioTrackRetagService.EditTimeout), Times.Once());
        }

        // ----- trigger matrix -----

        [Test]
        public void should_retag_after_import_and_record_done()
        {
            Import();

            VerifyEdited(LibraryPath);
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(_movieFile), Times.Once());

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.AudioTrackRetag.Mode.Should().Be(AudioTrackRetagHardlinkMode.Skip);
            _movieFile.AudioTrackRetag.Tracks.Should().HaveCount(1);
            _movieFile.AudioTrackRetag.Tracks[0].From.Should().Be("eng");
            _movieFile.AudioTrackRetag.Tracks[0].To.Should().Be("fre");
            _movieFile.AudioTrackRetag.Error.Should().BeNull();
            _movieFile.AudioTrackRetag.At.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        }

        [Test]
        public void should_update_media_info_and_languages_from_the_re_probed_file()
        {
            Import();

            Mocker.GetMock<IVideoFileInfoReader>().Verify(r => r.GetMediaInfo(LibraryPath), Times.Once());
            _movieFile.MediaInfo.AudioLanguages.Should().Equal("fre");
            _movieFile.Languages.Should().Equal(Language.French);
        }

        [Test]
        public void should_keep_the_languages_the_import_stored_for_a_multi_track_file()
        {
            _movieFile.AudioLanguageVerification.Add(new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "eng", DetectedLanguage = "en", Confidence = 0.98 });
            _movieFile.Languages = new List<Language> { Language.French, Language.English };
            _probedTags = new List<string> { "fre", "eng" };

            Import();

            _movieFile.Languages.Should().Equal(Language.French, Language.English);
        }

        [Test]
        public void should_verify_by_stream_position_when_an_untagged_track_precedes_the_edited_one()
        {
            // stream 0 has no language tag at all, stream 1 is the mistagged French one;
            // MediaInfo.AudioLanguages would be ["fre"] and index 1 would be out of range
            _movieFile.AudioLanguageVerification = new List<AudioLanguageVerification>
            {
                new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = null, DetectedLanguage = null, Confidence = 0 },
                new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "eng", DetectedLanguage = "fr", Confidence = 0.99 }
            };
            _probedTags = new List<string> { null, "fre" };

            Import();

            Mocker.GetMock<IAudioTrackTagger>().Verify(t => t.SetLanguages(LibraryPath, It.Is<IReadOnlyList<AudioTrackRetagTrack>>(e => e.Count == 1 && e[0].StreamIndex == 1), It.IsAny<TimeSpan>()), Times.Once());
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.Languages.Should().Equal(Language.French);
            _movieFile.Languages.Should().NotContain(Language.Unknown);
        }

        [Test]
        public void should_not_add_unknown_for_an_und_track()
        {
            _movieFile.AudioLanguageVerification.Add(new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "und", DetectedLanguage = null, Confidence = 0 });
            _probedTags = new List<string> { "fre", "und" };

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.Languages.Should().Equal(Language.French);
        }

        [Test]
        public void should_retain_the_verified_language_of_a_track_skipped_for_lack_of_iso_code()
        {
            _movieFile.AudioLanguageVerification.Add(new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "eng", DetectedLanguage = "xx", Confidence = 0.95 });
            _movieFile.Languages = new List<Language> { Language.French, Language.English };
            _probedTags = new List<string> { "fre", "eng" };

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.AudioTrackRetag.SkippedTracks.Should().HaveCount(1);
            _movieFile.Languages.Should().Equal(Language.French, Language.English);
        }

        [Test]
        public void should_not_start_any_process_when_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioTrackRetagEnabled).Returns(false);
            Mocker.SetConstant<IAudioTrackTagger>(Mocker.Resolve<MkvPropEditTrackTagger>());

            Import();

            Mocker.GetMock<IProcessProvider>().Verify(p => p.Start(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>(), It.IsAny<Action<string>>(), It.IsAny<Action<string>>()), Times.Never());
            Mocker.GetMock<IProcessProvider>().Verify(p => p.StartAndCapture(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<StringDictionary>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            VerifyNoUpdate();
        }

        [Test]
        public void should_ignore_files_found_by_a_disk_scan()
        {
            // rescan / refresh import existing files with NewDownload = false
            Import(newDownload: false);

            VerifyNoEdit();
            VerifyNoUpdate();
        }

        [Test]
        public void should_ignore_non_mkv_import()
        {
            _movieFile.RelativePath = "Movie (2020).mp4";
            _movieFile.AudioLanguageVerification = null;

            Import();

            VerifyNoEdit();
            VerifyNoUpdate();
        }

        [Test]
        public void should_record_skipped_container_for_verified_non_mkv_import()
        {
            _movieFile.RelativePath = "Movie (2020).mp4";

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedContainer);
        }

        [Test]
        public void should_ignore_file_without_verification_record()
        {
            _movieFile.AudioLanguageVerification = null;

            Import();

            VerifyNoEdit();
            VerifyNoUpdate();
        }

        [Test]
        public void should_ignore_track_below_threshold()
        {
            _movieFile.AudioLanguageVerification[0].Confidence = 0.5;

            Import();

            VerifyNoEdit();
            VerifyNoUpdate();
        }

        [Test]
        public void should_ignore_file_already_done()
        {
            var previous = new AudioTrackRetag { Result = AudioTrackRetagResult.Done, At = DateTime.UtcNow.AddDays(-1) };
            _movieFile.AudioTrackRetag = previous;

            Import();

            VerifyNoEdit();
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
            _movieFile.AudioTrackRetag.Should().BeSameAs(previous);
        }

        [Test]
        public void should_record_failed_when_file_is_missing()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(LibraryPath)).Returns(false);

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("not found");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_failed_when_no_mismatched_track_is_mappable()
        {
            _movieFile.AudioLanguageVerification[0].DetectedLanguage = "xx";

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Tracks.Should().BeEmpty();
            _movieFile.AudioTrackRetag.SkippedTracks.Should().HaveCount(1);
        }

        // ----- hardlink modes -----

        [Test]
        public void should_skip_hardlinked_file_in_skip_mode()
        {
            GivenHardlinked();

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
            _movieFile.AudioTrackRetag.Mode.Should().Be(AudioTrackRetagHardlinkMode.Skip);
            _movieFile.AudioTrackRetag.Tracks.Should().HaveCount(1);
            _movieFile.Languages.Should().Equal(Language.French);
        }

        [Test]
        public void should_skip_when_link_count_is_unavailable_in_skip_mode()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(0);
            _localMovie.TransferMode = TransferMode.Copy;

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
            _movieFile.AudioTrackRetag.Error.Should().Be(AudioTrackRetagService.LinkCountUnavailable);
        }

        [Test]
        public void should_copy_when_link_count_is_unavailable_in_copy_mode()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(0);
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(LibraryPath, TempPath, true), Times.Once());
            VerifyEdited(TempPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void should_retag_in_place_when_link_count_is_unavailable_in_in_place_mode()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(0);
            GivenMode(AudioTrackRetagHardlinkMode.RetagInPlace);

            Import();

            VerifyEdited(LibraryPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void should_use_transfer_mode_hint_when_link_count_is_unknown()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(0);
            _localMovie.TransferMode = TransferMode.HardLink;

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
        }

        [Test]
        public void should_trust_link_count_over_transfer_mode_hint()
        {
            _localMovie.TransferMode = TransferMode.HardLink;
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(1);

            Import();

            VerifyEdited(LibraryPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void should_fall_back_to_hint_when_link_count_throws()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Throws(new System.IO.IOException("stat failed"));
            _localMovie.TransferMode = TransferMode.HardLink;

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
        }

        [Test]
        public void should_copy_then_retag_hardlinked_file()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(LibraryPath, TempPath, true), Times.Once());
            VerifyEdited(TempPath);
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryRenameFile(TempPath, LibraryPath), Times.Once());
            Mocker.GetMock<IAudioTrackTagger>().Verify(t => t.SetLanguages(LibraryPath, It.IsAny<IReadOnlyList<AudioTrackRetagTrack>>(), It.IsAny<TimeSpan>()), Times.Never());

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.AudioTrackRetag.Mode.Should().Be(AudioTrackRetagHardlinkMode.CopyThenRetag);
        }

        [Test]
        public void should_swap_through_a_backup_when_rename_is_unsupported()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.TryRenameFile(TempPath, LibraryPath)).Returns(false);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(LibraryPath, BackupPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(TempPath, LibraryPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(BackupPath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(LibraryPath), Times.Never());
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void should_restore_the_original_when_the_swap_fails_after_parking_it()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.TryRenameFile(TempPath, LibraryPath)).Returns(false);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.MoveFile(TempPath, LibraryPath, false)).Throws(new System.IO.IOException("move failed"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(TempPath)).Returns(true);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(LibraryPath, BackupPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(BackupPath, LibraryPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(BackupPath), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(TempPath), Times.Once());
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("move failed");
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_not_copy_a_file_that_is_not_hardlinked_in_copy_mode()
        {
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            VerifyEdited(LibraryPath);
        }

        [Test]
        public void should_refuse_copy_when_free_space_is_insufficient()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetAvailableSpace(MoviePath)).Returns(1_000_000L);

            Import();

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("free space");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_temp_and_leave_original_when_copy_fails()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);
            Mocker.GetMock<IDiskProvider>().Setup(d => d.CopyFile(LibraryPath, TempPath, true)).Throws(new System.IO.IOException("disk error"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(TempPath)).Returns(true);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(TempPath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryRenameFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            Mocker.GetMock<IAudioTrackTagger>().Verify(t => t.SetLanguages(It.IsAny<string>(), It.IsAny<IReadOnlyList<AudioTrackRetagTrack>>(), It.IsAny<TimeSpan>()), Times.Never());

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("disk error");
            _movieFile.Languages.Should().Equal(Language.French);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_temp_when_the_edit_of_the_copy_fails()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.CopyThenRetag);
            Mocker.GetMock<IAudioTrackTagger>().Setup(t => t.SetLanguages(TempPath, It.IsAny<IReadOnlyList<AudioTrackRetagTrack>>(), It.IsAny<TimeSpan>())).Throws(new AudioTrackRetagException("mkvpropedit exited with code 2"));
            Mocker.GetMock<IDiskProvider>().Setup(d => d.FileExists(TempPath)).Returns(true);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.DeleteFile(TempPath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(d => d.TryRenameFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_retag_hardlinked_file_in_place()
        {
            GivenHardlinked();
            GivenMode(AudioTrackRetagHardlinkMode.RetagInPlace);

            Import();

            Mocker.GetMock<IDiskProvider>().Verify(d => d.CopyFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never());
            VerifyEdited(LibraryPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
            _movieFile.AudioTrackRetag.Mode.Should().Be(AudioTrackRetagHardlinkMode.RetagInPlace);
        }

        // ----- failures after the edit -----

        [Test]
        public void should_record_failed_when_mkvpropedit_fails()
        {
            Mocker.GetMock<IAudioTrackTagger>().Setup(t => t.SetLanguages(LibraryPath, It.IsAny<IReadOnlyList<AudioTrackRetagTrack>>(), It.IsAny<TimeSpan>())).Throws(new AudioTrackRetagException("mkvpropedit exited with code 2"));

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("code 2");
            _movieFile.MediaInfo.AudioLanguages.Should().Equal("eng");
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_record_failed_when_the_file_still_reports_the_old_tag()
        {
            _probedTags = new List<string> { "eng" };

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
            _movieFile.AudioTrackRetag.Error.Should().Contain("a0=eng");
        }

        [Test]
        public void should_accept_terminology_code_after_the_edit()
        {
            _probedTags = new List<string> { "fra" };

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void should_record_failed_when_the_file_cannot_be_probed_after_the_edit()
        {
            Mocker.GetMock<IVideoFileInfoReader>().Setup(r => r.GetMediaInfo(It.IsAny<string>())).Returns((MediaInfoModel)null);

            Import();

            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Failed);
        }

        [Test]
        public void should_never_throw_into_the_import()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.Update(It.IsAny<MovieFile>())).Throws(new InvalidOperationException("db"));

            Assert.DoesNotThrow(() => Import());

            ExceptionVerification.ExpectedWarns(1);
        }

        // ----- manual command -----

        [Test]
        public void manual_command_should_retag_one_file()
        {
            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyEdited(LibraryPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void manual_command_should_work_when_the_import_trigger_is_disabled()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.AudioTrackRetagEnabled).Returns(false);

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyEdited(LibraryPath);
        }

        [Test]
        public void manual_command_should_skip_when_link_count_is_unavailable_in_skip_mode()
        {
            Mocker.GetMock<IDiskProvider>().Setup(d => d.GetHardLinkCount(LibraryPath)).Returns(0);

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
            _movieFile.AudioTrackRetag.Error.Should().Be(AudioTrackRetagService.LinkCountUnavailable);
        }

        [Test]
        public void manual_command_should_apply_the_hardlink_mode()
        {
            GivenHardlinked();

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedHardlinked);
        }

        [Test]
        public void manual_command_should_retry_a_file_skipped_earlier()
        {
            _movieFile.AudioTrackRetag = new AudioTrackRetag { Result = AudioTrackRetagResult.SkippedHardlinked };
            GivenMode(AudioTrackRetagHardlinkMode.RetagInPlace);

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyEdited(LibraryPath);
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.Done);
        }

        [Test]
        public void manual_command_should_not_retag_a_file_already_done()
        {
            _movieFile.AudioTrackRetag = new AudioTrackRetag { Result = AudioTrackRetagResult.Done };

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyNoEdit();
            Mocker.GetMock<IMediaFileService>().Verify(s => s.Update(It.IsAny<MovieFile>()), Times.Never());
        }

        [Test]
        public void manual_command_should_record_skipped_container_for_non_mkv()
        {
            _movieFile.RelativePath = "Movie (2020).mp4";
            _movieFile.AudioLanguageVerification = null;

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyNoEdit();
            _movieFile.AudioTrackRetag.Result.Should().Be(AudioTrackRetagResult.SkippedContainer);
        }

        [Test]
        public void manual_command_should_load_the_movie_when_the_file_has_none()
        {
            _movieFile.Movie = null;
            Mocker.GetMock<IMovieService>().Setup(s => s.GetMovie(7)).Returns(_movie);

            Subject.Execute(new RetagAudioTracksCommand(42));

            VerifyEdited(LibraryPath);
        }

        [Test]
        public void manual_command_should_reject_unknown_file()
        {
            Mocker.GetMock<IMediaFileService>().Setup(s => s.GetMovie(99)).Returns((MovieFile)null);

            Assert.Throws<ArgumentException>(() => Subject.Execute(new RetagAudioTracksCommand(99)));
        }
    }
}
