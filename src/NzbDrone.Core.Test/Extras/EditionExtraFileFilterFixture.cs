using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Extras;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.Extras
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionExtraFileFilterFixture : CoreTest<EditionExtraFileFilter>
    {
        private const int MetadataId = 7;

        private string _moviePath;
        private Movie _default;
        private Movie _extended;
        private MovieFile _defaultFile;
        private MovieFile _extendedFile;

        [SetUp]
        public void Setup()
        {
            _moviePath = @"C:\Movies\Movie (2010)".AsOsAgnostic();

            _defaultFile = new MovieFile { Id = 10, MovieId = 1, RelativePath = "Movie (2010).mkv" };
            _extendedFile = new MovieFile { Id = 20, MovieId = 2, RelativePath = "Movie (2010) {edition-Extended}.mkv" };

            _default = new Movie
            {
                Id = 1,
                MovieMetadataId = MetadataId,
                Edition = "",
                Path = _moviePath,
                MovieFileId = 10,
                MovieFile = _defaultFile
            };

            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = MetadataId,
                Edition = "Extended",
                EditionAliases = new List<string> { "Extended", "Extended Cut", "Extended Edition" },
                Path = _moviePath,
                MovieFileId = 20,
                MovieFile = _extendedFile
            };

            GivenSiblings(_default, _extended);
            GivenSiblings(_extended, _default);
            GivenOwnFiles(_default, _defaultFile);
            GivenOwnFiles(_extended, _extendedFile);
        }

        private void GivenSiblings(Movie row, params Movie[] siblings)
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(row.Id))
                  .Returns(siblings.ToList());
        }

        private void GivenOwnFiles(Movie row, params MovieFile[] files)
        {
            Mocker.GetMock<IMediaFileService>()
                  .Setup(s => s.GetFilesByMovie(row.Id))
                  .Returns(files.ToList());
        }

        private string InFolder(params string[] parts)
        {
            return Path.Combine(_moviePath, Path.Combine(parts));
        }

        [Test]
        public void should_return_the_same_list_when_the_title_has_no_edition_rows()
        {
            GivenSiblings(_default);

            var files = new List<string> { InFolder("Movie (2010).en.srt"), InFolder("Movie (2010) {edition-Extended}.en.srt"), InFolder("poster.jpg") };

            Subject.FilterClaimable(_default, files).Should().BeSameAs(files);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.GetFilesByMovie(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void default_row_should_claim_its_own_stem_and_folder_level_files_but_not_the_editions()
        {
            var files = new List<string>
            {
                InFolder("Movie (2010).en.srt"),
                InFolder("Movie (2010)-thumb.jpg"),
                InFolder("Movie (2010).nfo"),
                InFolder("Movie (2010) {edition-Extended}.en.srt"),
                InFolder("Movie (2010) {edition-Extended}-thumb.jpg"),
                InFolder("poster.jpg"),
                InFolder("movie.nfo")
            };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(new[]
            {
                InFolder("Movie (2010).en.srt"),
                InFolder("Movie (2010)-thumb.jpg"),
                InFolder("Movie (2010).nfo"),
                InFolder("poster.jpg"),
                InFolder("movie.nfo")
            });
        }

        [Test]
        public void edition_row_should_claim_only_the_files_of_its_own_movie_file()
        {
            var files = new List<string>
            {
                InFolder("Movie (2010).en.srt"),
                InFolder("Movie (2010) {edition-Extended}.en.srt"),
                InFolder("Movie (2010) {edition-Extended}.fr.forced.srt"),
                InFolder("poster.jpg"),
                InFolder("movie.nfo")
            };

            Subject.FilterClaimable(_extended, files).Should().BeEquivalentTo(new[]
            {
                InFolder("Movie (2010) {edition-Extended}.en.srt"),
                InFolder("Movie (2010) {edition-Extended}.fr.forced.srt")
            });
        }

        [Test]
        public void should_hand_a_tagged_extra_to_the_edition_it_names_even_without_a_matching_stem()
        {
            // the Extended row has no file yet, the subtitle still names it
            _extended.MovieFile = null;
            _extended.MovieFileId = 0;
            GivenOwnFiles(_extended);

            var files = new List<string> { InFolder("Movie (2010) {edition-Extended Cut}.en.srt"), InFolder("Movie (2010).en.srt") };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(new[] { InFolder("Movie (2010).en.srt") });
            Subject.FilterClaimable(_extended, files).Should().BeEquivalentTo(new[] { InFolder("Movie (2010) {edition-Extended Cut}.en.srt") });
        }

        [Test]
        public void should_use_the_longest_stem_when_the_edition_file_is_not_tagged()
        {
            _extendedFile.RelativePath = "Movie (2010) Extended Cut 1080p.mkv";

            var files = new List<string> { InFolder("Movie (2010) Extended Cut 1080p.en.srt"), InFolder("Movie (2010).en.srt") };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(new[] { InFolder("Movie (2010).en.srt") });
            Subject.FilterClaimable(_extended, files).Should().BeEquivalentTo(new[] { InFolder("Movie (2010) Extended Cut 1080p.en.srt") });
        }

        [Test]
        public void should_match_stems_by_file_name_inside_subfolders()
        {
            var files = new List<string> { InFolder("Subs", "Movie (2010).en.srt"), InFolder("Subs", "Movie (2010) {edition-Extended}.en.srt") };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(new[] { InFolder("Subs", "Movie (2010).en.srt") });
            Subject.FilterClaimable(_extended, files).Should().BeEquivalentTo(new[] { InFolder("Subs", "Movie (2010) {edition-Extended}.en.srt") });
        }

        [Test]
        public void should_fall_back_to_the_stem_when_a_tag_names_no_row_of_the_title()
        {
            var files = new List<string> { InFolder("Movie (2010) {edition-Unrated}.en.srt") };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(files);
            Subject.FilterClaimable(_extended, files).Should().BeEmpty();
        }

        [Test]
        public void default_row_without_a_file_should_still_keep_folder_level_files()
        {
            _default.MovieFile = null;
            _default.MovieFileId = 0;
            GivenOwnFiles(_default);

            var files = new List<string> { InFolder("poster.jpg"), InFolder("Movie (2010) {edition-Extended}.en.srt") };

            Subject.FilterClaimable(_default, files).Should().BeEquivalentTo(new[] { InFolder("poster.jpg") });
        }

        [Test]
        public void should_return_the_input_for_empty_or_unsaved_input()
        {
            var empty = new List<string>();

            Subject.FilterClaimable(_default, empty).Should().BeSameAs(empty);
            Subject.FilterClaimable(_default, null).Should().BeNull();
            Subject.FilterClaimable(new Movie { Id = 0 }, new List<string> { InFolder("poster.jpg") }).Should().HaveCount(1);

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
        }
    }
}
