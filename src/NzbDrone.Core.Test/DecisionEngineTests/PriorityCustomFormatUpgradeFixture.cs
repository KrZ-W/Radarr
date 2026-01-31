using System;
using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.DecisionEngineTests
{
    [TestFixture]
    public class PriorityCustomFormatUpgradeFixture : CoreTest<UpgradeDiskSpecification>
    {
        private UpgradeDiskSpecification _upgradeDisk;
        private RemoteMovie _parseResult;
        private MovieFile _existingFile;
        private CustomFormat _priorityFormat;
        private CustomFormat _regularFormat;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<UpgradableSpecification>();
            _upgradeDisk = Mocker.Resolve<UpgradeDiskSpecification>();

            // Create a "priority" custom format
            _priorityFormat = new CustomFormat("Priority Format", new ResolutionSpecification { Value = (int)Resolution.R1080p }) { Id = 1 };
            _regularFormat = new CustomFormat("Regular Format", new ResolutionSpecification { Value = (int)Resolution.R720p }) { Id = 2 };

            // Use SDTV which is BELOW the Bluray1080p cutoff, so cutoff is not met
            _existingFile = new MovieFile { Quality = new QualityModel(Quality.SDTV, new Revision(version: 1)), DateAdded = DateTime.Now };

            var fakeMovie = Builder<Movie>.CreateNew()
                .With(c => c.QualityProfile = new QualityProfile
                {
                    UpgradeAllowed = true,
                    Cutoff = Quality.Bluray1080p.Id,
                    Items = Qualities.QualityFixture.GetDefaultQualities(),
                    MinFormatScore = 0,
                    CutoffFormatScore = 10000, // High cutoff so CF cutoff isn't met
                    MinUpgradeFormatScore = 0, // No minimum upgrade increment required
                    FormatItems = new List<ProfileFormatItem>
                    {
                        new ProfileFormatItem { Format = _priorityFormat, Score = 100, Priority = true },
                        new ProfileFormatItem { Format = _regularFormat, Score = 100, Priority = false }
                    }
                })
                .With(e => e.MovieFile = _existingFile)
                .Build();

            _parseResult = new RemoteMovie
            {
                Movie = fakeMovie,

                // New release also below cutoff but same quality level
                ParsedMovieInfo = new ParsedMovieInfo { Quality = new QualityModel(Quality.SDTV, new Revision(version: 1)) },
                CustomFormats = new List<CustomFormat>()
            };

            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<MovieFile>()))
                .Returns(new List<CustomFormat>());
        }

        [Test]
        public void should_upgrade_when_new_release_has_priority_format_and_existing_does_not()
        {
            // Existing file has NO custom formats
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<MovieFile>()))
                .Returns(new List<CustomFormat>());

            // New release has the Priority format
            _parseResult.CustomFormats = new List<CustomFormat> { _priorityFormat };

            // Same quality, but new release should be considered an upgrade due to Priority CF
            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_not_upgrade_when_priority_format_score_is_equal()
        {
            // Existing file HAS the priority format
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<MovieFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            // New release also has the same priority format
            _parseResult.CustomFormats = new List<CustomFormat> { _priorityFormat };

            // Same quality and same priority score - should NOT be an upgrade
            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_not_upgrade_from_priority_to_regular_format_even_if_regular_has_higher_score()
        {
            // Change regular format to have higher score
            _parseResult.Movie.QualityProfile.FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = _priorityFormat, Score = 50, Priority = true },
                new ProfileFormatItem { Format = _regularFormat, Score = 200, Priority = false }
            };

            // Existing file HAS the priority format (lower score)
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<MovieFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            // New release has regular format (higher score, but not priority)
            _parseResult.CustomFormats = new List<CustomFormat> { _regularFormat };

            // Priority format should take precedence - should NOT upgrade to regular even if score is higher
            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_upgrade_to_higher_priority_score()
        {
            // Create second priority format with higher score
            var higherPriorityFormat = new CustomFormat("Higher Priority", new ResolutionSpecification { Value = (int)Resolution.R2160p }) { Id = 3 };

            _parseResult.Movie.QualityProfile.FormatItems = new List<ProfileFormatItem>
            {
                new ProfileFormatItem { Format = _priorityFormat, Score = 50, Priority = true },
                new ProfileFormatItem { Format = higherPriorityFormat, Score = 100, Priority = true }
            };

            // Existing file has lower priority format
            Mocker.GetMock<ICustomFormatCalculationService>()
                .Setup(x => x.ParseCustomFormat(It.IsAny<MovieFile>()))
                .Returns(new List<CustomFormat> { _priorityFormat });

            // New release has higher priority format
            _parseResult.CustomFormats = new List<CustomFormat> { higherPriorityFormat };

            // Should upgrade to higher priority score
            _upgradeDisk.IsSatisfiedBy(_parseResult, null).Accepted.Should().BeTrue();
        }
    }
}
