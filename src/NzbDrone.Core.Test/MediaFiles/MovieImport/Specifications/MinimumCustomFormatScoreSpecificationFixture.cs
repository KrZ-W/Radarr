using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class MinimumCustomFormatScoreSpecificationFixture : CoreTest<MinimumCustomFormatScoreSpecification>
    {
        private Movie _movie;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                .With(e => e.QualityProfile = new QualityProfile { MinFormatScore = 0 })
                .Build();

            _localMovie = new LocalMovie
            {
                Path = @"/downloads/movie.mkv",
                Movie = _movie,
                CustomFormats = new List<CustomFormat>(),
                CustomFormatScore = 0
            };
        }

        [Test]
        public void should_accept_when_score_equals_minimum()
        {
            _localMovie.CustomFormatScore = 0;
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_score_above_minimum()
        {
            _localMovie.CustomFormatScore = 5000;
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_score_below_minimum()
        {
            _localMovie.CustomFormatScore = -10000;
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_existing_file_regardless_of_score()
        {
            _localMovie.ExistingFile = true;
            _localMovie.CustomFormatScore = -10000;
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_minimum_is_negative_and_score_above_it()
        {
            _movie.QualityProfile.MinFormatScore = -5000;
            _localMovie.CustomFormatScore = -1000;
            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        // krzw(audio-language-verification)
        [Test]
        public void should_say_audio_verified_when_a_probe_ran_and_the_score_is_still_too_low()
        {
            _localMovie.CustomFormatScore = -10000;
            _localMovie.AudioLanguageTrigger = AudioLanguageTrigger.ImpendingRejection;
            _localMovie.AudioLanguageVerification = new List<AudioLanguageVerification>
            {
                new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "en", Confidence = 0.97 }
            };

            var decision = Subject.IsSatisfiedBy(_localMovie, null);

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().EndWith(". Audio verified (detected en 0.97)");
        }

        [Test]
        public void should_not_mention_verification_when_no_probe_ran()
        {
            _localMovie.CustomFormatScore = -10000;

            Subject.IsSatisfiedBy(_localMovie, null).Message.Should().NotContain("Audio verified");
        }
    }
}
