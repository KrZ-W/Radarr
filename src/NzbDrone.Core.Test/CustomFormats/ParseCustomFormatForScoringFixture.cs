using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.CustomFormats
{
    // krzw(grabbed-release-title)
    [TestFixture]
    public class ParseCustomFormatForScoringFixture : CoreTest<CustomFormatCalculationService>
    {
        private const string SceneNameTitle = "Le.Sapin.a.les.boules.1989.MULTi.VFi.1080p.BluRay.x264-GRP";
        private const string GrabbedTitle = "Le Sapin a les boules TRUEFRENCH HDLight 1080p 1989";

        private Movie _movie;
        private MovieFile _movieFile;

        [SetUp]
        public void Setup()
        {
            _movie = new Movie
            {
                Id = 1,
                Title = "Le Sapin a les boules",
                Year = 1989,
                QualityProfile = new QualityProfile
                {
                    Name = "Test",
                    FormatItems = new List<ProfileFormatItem>()
                }
            };

            _movieFile = new MovieFile
            {
                Id = 7,
                MovieId = _movie.Id,
                Movie = _movie,
                RelativePath = "renamed by radarr.mkv",
                SceneName = SceneNameTitle,
                GrabbedReleaseTitle = GrabbedTitle,
                Quality = new QualityModel(Quality.Bluray1080p),
                Languages = new List<Language>()
            };

            GivenSetting(true);
        }

        private void GivenSetting(bool enabled)
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.ScoreFilesByGrabbedReleaseTitle)
                  .Returns(enabled);
        }

        private CustomFormat GivenFormat(int id, string name, string releaseTitleRegex, int score, bool priority = false)
        {
            var format = new CustomFormat(name, new ReleaseTitleSpecification { Value = releaseTitleRegex }) { Id = id };

            _movie.QualityProfile.FormatItems.Add(new ProfileFormatItem { Format = format, Score = score, Priority = priority });

            return format;
        }

        private void GivenAllFormats(params CustomFormat[] formats)
        {
            Mocker.GetMock<ICustomFormatService>()
                  .Setup(s => s.All())
                  .Returns(formats.ToList());
        }

        [Test]
        public void should_match_a_format_that_only_the_grabbed_release_title_satisfies()
        {
            // Guards the Radarr-specific trap: the candidate title has to reach
            // ParsedMovieInfo.SimpleReleaseTitle, because ReleaseTitleSpecification reads that (or the
            // file name) and never reads ReleaseTitle. Assigning ReleaseTitle scores every candidate
            // identically and the whole feature silently does nothing.
            var grabOnly = GivenFormat(1, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(grabOnly);

            Subject.ParseCustomFormatForScoring(_movieFile, _movie).Should().Contain(grabOnly);
        }

        [Test]
        public void should_use_the_grabbed_release_title_when_it_scores_higher()
        {
            var sceneFormat = GivenFormat(1, "VFi", @"\bVFi\b", 10);
            var grabFormat = GivenFormat(2, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(grabFormat);
            formats.Should().NotContain(sceneFormat);
        }

        [Test]
        public void should_keep_the_scene_name_when_the_grabbed_release_title_scores_lower()
        {
            var sceneFormat = GivenFormat(1, "MULTi", @"\bMULTi\b", 100);
            var grabFormat = GivenFormat(2, "HDLight", "HDLight", 10);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_keep_the_scene_name_when_the_grabbed_release_title_would_lower_the_priority_score()
        {
            // The fork compares the priority score before quality, so a plain max-by-total would make the
            // file weaker on the axis that is checked first and cause the re-grab this feature prevents.
            var priorityFormat = GivenFormat(1, "VFi", @"\bVFi\b", 50, priority: true);
            var grabFormat = GivenFormat(2, "HDLight", "HDLight", 500);
            GivenAllFormats(priorityFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(priorityFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_keep_the_incumbent_when_the_scores_tie()
        {
            var sceneFormat = GivenFormat(1, "MULTi", @"\bMULTi\b", 100);
            var grabFormat = GivenFormat(2, "HDLight", "HDLight", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_return_the_legacy_ladder_when_the_setting_is_off()
        {
            GivenSetting(false);

            var sceneFormat = GivenFormat(1, "VFi", @"\bVFi\b", 10);
            var grabFormat = GivenFormat(2, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_return_the_legacy_ladder_when_there_is_no_grabbed_release_title()
        {
            _movieFile.GrabbedReleaseTitle = null;

            var sceneFormat = GivenFormat(1, "VFi", @"\bVFi\b", 10);
            var grabFormat = GivenFormat(2, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_return_the_legacy_ladder_when_the_movie_has_no_quality_profile()
        {
            _movie.QualityProfile = null;

            var grabFormat = new CustomFormat("TRUEFRENCH", new ReleaseTitleSpecification { Value = "TRUEFRENCH" }) { Id = 2 };
            var sceneFormat = new CustomFormat("VFi", new ReleaseTitleSpecification { Value = @"\bVFi\b" }) { Id = 1 };
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_not_change_the_formats_used_for_naming()
        {
            // FileNameBuilder renders the {Custom Formats} token through the legacy overload. If the
            // scoring rule leaked into it, enabling the setting would propose a library-wide rename.
            var sceneFormat = GivenFormat(1, "VFi", @"\bVFi\b", 10);
            var grabFormat = GivenFormat(2, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var formats = Subject.ParseCustomFormat(_movieFile, _movie);

            formats.Should().Contain(sceneFormat);
            formats.Should().NotContain(grabFormat);
        }

        [Test]
        public void should_use_the_grabbed_release_title_for_a_file_being_imported()
        {
            var sceneFormat = GivenFormat(1, "VFi", @"\bVFi\b", 10);
            var grabFormat = GivenFormat(2, "TRUEFRENCH", "TRUEFRENCH", 100);
            GivenAllFormats(sceneFormat, grabFormat);

            var localMovie = new LocalMovie
            {
                Movie = _movie,
                Path = @"C:\downloads\renamed by radarr.mkv".AsOsAgnostic(),
                SceneName = SceneNameTitle,
                GrabbedReleaseTitle = GrabbedTitle,
                Quality = new QualityModel(Quality.Bluray1080p),
                Languages = new List<Language>()
            };

            var formats = Subject.ParseCustomFormatForScoring(localMovie);

            formats.Should().Contain(grabFormat);
            formats.Should().NotContain(sceneFormat);
        }

        [Test]
        public void should_not_lower_the_score_of_a_file_whose_scene_name_is_missing()
        {
            _movieFile.SceneName = null;
            _movieFile.OriginalFilePath = "some.folder/Le.Sapin.a.les.boules.1989.MULTi.VFi.1080p.BluRay.x264-GRP.mkv";

            var originalFormat = GivenFormat(1, "VFi", @"\bVFi\b", 100);
            var grabFormat = GivenFormat(2, "HDLight", "HDLight", 10);
            GivenAllFormats(originalFormat, grabFormat);

            var formats = Subject.ParseCustomFormatForScoring(_movieFile, _movie);

            formats.Should().Contain(originalFormat);
            formats.Should().NotContain(grabFormat);
        }
    }
}
