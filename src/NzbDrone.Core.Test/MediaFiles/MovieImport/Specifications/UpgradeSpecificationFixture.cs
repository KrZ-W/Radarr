using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class UpgradeSpecificationFixture : CoreTest<UpgradeSpecification>
    {
        private Movie _movie;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                                     .With(e => e.QualityProfile = new QualityProfile { Items = Qualities.QualityFixture.GetDefaultQualities() })
                                     .Build();

            _localMovie = new LocalMovie()
            {
                Path = @"C:\Test\30 Rock\30.rock.s01e01.avi",
                Quality = new QualityModel(Quality.HDTV720p, new Revision(version: 1)),
                Movie = _movie
            };
        }

        [Test]
        public void should_return_true_if_no_existing_episodeFile()
        {
            _localMovie.Movie.MovieFile = null;
            _localMovie.Movie.MovieFileId = 0;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_for_existing_episodeFile()
        {
            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                    new MovieFile
                    {
                        Quality = new QualityModel(Quality.SDTV, new Revision(version: 1))
                    };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_not_an_upgrade_for_existing_episodeFile()
        {
            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Quality = new QualityModel(Quality.Bluray720p, new Revision(version: 1))
                };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_false_if_not_a_revision_upgrade_and_prefers_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.PreferAndUpgrade);

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Quality = new QualityModel(Quality.HDTV720p, new Revision(version: 2))
                };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_return_true_if_not_a_revision_upgrade_and_does_not_prefer_propers()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Quality = new QualityModel(Quality.HDTV720p, new Revision(version: 2))
                };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_when_comparing_to_a_lower_quality_proper()
        {
            Mocker.GetMock<IConfigService>()
                  .Setup(s => s.DownloadPropersAndRepacks)
                  .Returns(ProperDownloadTypes.DoNotPrefer);

            _localMovie.Quality = new QualityModel(Quality.Bluray1080p);

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile =
                new MovieFile
                {
                    Quality = new QualityModel(Quality.Bluray1080p, new Revision(version: 2))
                };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_movie_file_is_null()
        {
            _localMovie.Movie.MovieFile = null;
            _localMovie.Movie.MovieFileId = 1;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_upgrade_to_custom_format_score()
        {
            var movieFileCustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.Bluray1080p)
            };

            _movie.QualityProfile.FormatItems = movieFileCustomFormats.Select(c => new ProfileFormatItem
            {
                Format = c,
                Score = 10
            })
                .ToList();

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(movieFileCustomFormats);

            _localMovie.Quality = new QualityModel(Quality.Bluray1080p);
            _localMovie.CustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();
            _localMovie.CustomFormatScore = 20;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_true_if_not_upgrade_to_custom_format_score_but_is_upgrade_to_quality()
        {
            var movieFileCustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.Bluray720p)
            };

            _movie.QualityProfile.FormatItems = movieFileCustomFormats.Select(c => new ProfileFormatItem
            {
                Format = c,
                Score = 50
            })
                .ToList();

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(movieFileCustomFormats);

            _localMovie.Quality = new QualityModel(Quality.Bluray1080p);
            _localMovie.CustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();
            _localMovie.CustomFormatScore = 20;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_return_false_if_not_upgrade_to_custom_format_score()
        {
            var movieFileCustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.Bluray1080p)
            };

            _movie.QualityProfile.FormatItems = movieFileCustomFormats.Select(c => new ProfileFormatItem
            {
                Format = c,
                Score = 50
            })
                .ToList();

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(movieFileCustomFormats);

            _localMovie.Quality = new QualityModel(Quality.Bluray1080p);
            _localMovie.CustomFormats = Builder<CustomFormat>.CreateListOfSize(1).Build().ToList();
            _localMovie.CustomFormatScore = 20;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_quality_downgrade_when_new_release_has_priority_custom_format()
        {
            // Mirrors the user-reported scenario: existing Bluray-2160p file, new WEBDL-1080p
            // with a priority CF (e.g. VFQ language). Priority CF must override quality downgrade.
            var priorityFormat = new CustomFormat("VFQ") { Id = 1 };

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.Bluray2160p)
            };

            _movie.QualityProfile.FormatItems = new System.Collections.Generic.List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = priorityFormat, Score = 100, Priority = true }
            };
            _movie.QualityProfile.CutoffFormatScore = 10000;
            _movie.QualityProfile.MinUpgradeFormatScore = 0;

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(new System.Collections.Generic.List<CustomFormat>());

            _localMovie.Quality = new QualityModel(Quality.WEBDL1080p);
            _localMovie.CustomFormats = new System.Collections.Generic.List<CustomFormat> { priorityFormat };
            _localMovie.CustomFormatScore = 100;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_priority_custom_format_downgrade_even_if_quality_upgrade()
        {
            var priorityFormat = new CustomFormat("VFQ") { Id = 1 };

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p)
            };

            _movie.QualityProfile.FormatItems = new System.Collections.Generic.List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = priorityFormat, Score = 100, Priority = true }
            };

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(new System.Collections.Generic.List<CustomFormat> { priorityFormat });

            _localMovie.Quality = new QualityModel(Quality.Bluray2160p);
            _localMovie.CustomFormats = new System.Collections.Generic.List<CustomFormat>();
            _localMovie.CustomFormatScore = 0;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_respect_custom_format_cutoff_on_priority_upgrade()
        {
            var existingPriorityFormat = new CustomFormat("Existing Priority") { Id = 1 };
            var newPriorityFormat = new CustomFormat("New Priority") { Id = 2 };

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.WEBDL1080p)
            };

            _movie.QualityProfile.FormatItems = new System.Collections.Generic.List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = existingPriorityFormat, Score = 100, Priority = true },
                new ProfileFormatItem { Format = newPriorityFormat, Score = 200, Priority = true }
            };
            _movie.QualityProfile.CutoffFormatScore = 100; // existing already meets cutoff

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(new System.Collections.Generic.List<CustomFormat> { existingPriorityFormat });

            _localMovie.Quality = new QualityModel(Quality.WEBDL1080p);
            _localMovie.CustomFormats = new System.Collections.Generic.List<CustomFormat> { newPriorityFormat };
            _localMovie.CustomFormatScore = 200;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_still_accept_quality_upgrade_with_no_priority_custom_formats_configured()
        {
            // Regression: a profile with only regular (non-priority) CFs must behave exactly
            // as upstream Radarr — quality is the deciding factor, CFs only matter on tie.
            var regularFormat = new CustomFormat("Regular") { Id = 1 };

            var movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.HDTV720p)
            };

            _movie.QualityProfile.FormatItems = new System.Collections.Generic.List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = regularFormat, Score = 50, Priority = false }
            };

            Mocker.GetMock<IConfigService>()
                .Setup(s => s.DownloadPropersAndRepacks)
                .Returns(ProperDownloadTypes.DoNotPrefer);

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(s => s.ParseCustomFormat(movieFile))
                .Returns(new System.Collections.Generic.List<CustomFormat> { regularFormat });

            _localMovie.Quality = new QualityModel(Quality.Bluray1080p);
            _localMovie.CustomFormats = new System.Collections.Generic.List<CustomFormat>();
            _localMovie.CustomFormatScore = 0;

            _localMovie.Movie.MovieFileId = 1;
            _localMovie.Movie.MovieFile = movieFile;

            // Quality upgrade with no priority CFs in play — must accept even though regular CF score dropped.
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }
    }
}
