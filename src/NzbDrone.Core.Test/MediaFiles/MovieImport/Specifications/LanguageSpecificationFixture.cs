using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
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
    }
}
