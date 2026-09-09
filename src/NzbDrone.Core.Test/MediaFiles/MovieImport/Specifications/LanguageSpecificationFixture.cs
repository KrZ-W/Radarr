using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaFiles.AudioLanguage;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class LanguageSpecificationFixture : CoreTest<LanguageSpecification>
    {
        private Movie _movie;
        private LocalMovie _localMovie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew()
                .With(e => e.QualityProfile = new QualityProfile { Language = Language.French })
                .Build();
            _movie.MovieMetadata.Value.OriginalLanguage = Language.English;

            _localMovie = new LocalMovie
            {
                Path = @"/downloads/movie.mkv",
                Movie = _movie,
                Languages = new List<Language> { Language.French }
            };
        }

        [Test]
        public void should_accept_when_profile_language_is_any()
        {
            _movie.QualityProfile.Language = Language.Any;
            _localMovie.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_file_languages_contain_profile_language()
        {
            _localMovie.Languages = new List<Language> { Language.English, Language.French };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_existing_file_regardless_of_language()
        {
            _localMovie.ExistingFile = true;
            _localMovie.Languages = new List<Language> { Language.English };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_when_file_languages_do_not_contain_profile_language()
        {
            _localMovie.Languages = new List<Language> { Language.English };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_original_profile_language_when_file_has_original()
        {
            _movie.QualityProfile.Language = Language.Original;
            _movie.MovieMetadata.Value.OriginalLanguage = Language.German;
            _localMovie.Languages = new List<Language> { Language.German };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_original_profile_language_when_file_lacks_original()
        {
            _movie.QualityProfile.Language = Language.Original;
            _movie.MovieMetadata.Value.OriginalLanguage = Language.German;
            _localMovie.Languages = new List<Language> { Language.English };

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeFalse();
        }

        // krzw(audio-language-verification)
        private void GivenVerified(AudioLanguageTrigger trigger)
        {
            _localMovie.AudioLanguageTrigger = trigger;
            _localMovie.AudioLanguageVerification = new List<AudioLanguageVerification>
            {
                new AudioLanguageVerification { StreamIndex = 0, TaggedLanguage = "eng", DetectedLanguage = "en", Confidence = 0.97 },
                new AudioLanguageVerification { StreamIndex = 1, TaggedLanguage = "und", DetectedLanguage = "en", Confidence = 0.94 }
            };
        }

        [Test]
        public void should_say_audio_verified_when_a_probe_ran_and_the_file_is_still_rejected()
        {
            _localMovie.Languages = new List<Language> { Language.English };
            GivenVerified(AudioLanguageTrigger.Contradiction);

            var decision = Subject.IsSatisfiedBy(_localMovie, null);

            decision.Accepted.Should().BeFalse();
            decision.Message.Should().Be("File audio language English does not contain the profile's required language (French). Audio verified: no French track (detected en 0.97, en 0.94)");
        }

        [Test]
        public void should_say_audio_verified_for_original_language_rejections()
        {
            _movie.QualityProfile.Language = Language.Original;
            _movie.MovieMetadata.Value.OriginalLanguage = Language.German;
            _localMovie.Languages = new List<Language> { Language.English };
            GivenVerified(AudioLanguageTrigger.ImpendingRejection);

            Subject.IsSatisfiedBy(_localMovie, null).Message.Should().EndWith("Audio verified: no German track (detected en 0.97, en 0.94)");
        }

        [Test]
        public void should_not_mention_verification_when_no_probe_ran()
        {
            _localMovie.Languages = new List<Language> { Language.English };

            Subject.IsSatisfiedBy(_localMovie, null).Message.Should().Be("File audio language English does not contain the profile's required language (French)");
        }

        [Test]
        public void should_not_mention_verification_for_positive_verification_probes()
        {
            _localMovie.Languages = new List<Language> { Language.English };
            GivenVerified(AudioLanguageTrigger.PositiveVerification);

            Subject.IsSatisfiedBy(_localMovie, null).Message.Should().NotContain("Audio verified");
        }

        [Test]
        public void verification_should_not_change_the_decision()
        {
            _localMovie.Languages = new List<Language> { Language.French };
            GivenVerified(AudioLanguageTrigger.Contradiction);

            Subject.IsSatisfiedBy(_localMovie, null).Accepted.Should().BeTrue();
        }
    }
}
