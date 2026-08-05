using System.Collections.Generic;
using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.CustomFormats;
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
    }
}
