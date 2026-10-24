using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests.ParsingServiceTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class EditionRoutingFixture : CoreTest<ParsingService>
    {
        private const int TmdbId = 603;
        private const string ImdbId = "tt0133093";

        private MovieMetadata _metadata;
        private Movie _default;
        private Movie _extended;
        private List<Movie> _rows;
        private List<string> _otherTitles;

        [SetUp]
        public void Setup()
        {
            _metadata = new MovieMetadata
            {
                Id = 7,
                TmdbId = TmdbId,
                ImdbId = ImdbId,
                Title = "The Matrix",
                CleanTitle = "thematrix",
                Year = 1999
            };

            _default = new Movie { Id = 1, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "", Monitored = true };
            _extended = new Movie { Id = 2, MovieMetadataId = 7, MovieMetadata = _metadata, Edition = "Extended", EditionAliases = new List<string> { "Extended Cut" }, Monitored = false };
            _rows = new List<Movie> { _default, _extended };
            _otherTitles = new List<string>();

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(_rows);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByImdbId(ImdbId))
                  .Returns(_default);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(_default.Id))
                  .Returns(new List<Movie> { _extended });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(_extended.Id))
                  .Returns(new List<Movie> { _default });

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitleCandidates(It.IsAny<List<string>>(), out _otherTitles))
                  .Returns(_rows);
        }

        private static ParsedMovieInfo Parsed(string edition, int year = 1999)
        {
            return new ParsedMovieInfo
            {
                MovieTitles = { "The Matrix" },
                Year = year,
                Edition = edition,
                Languages = new List<Language> { Language.English }
            };
        }

        private static MovieSearchCriteria SearchFor(Movie movie)
        {
            return new MovieSearchCriteria { Movie = movie };
        }

        [Test]
        public void tmdb_lookup_should_route_a_tagged_release_to_its_edition_row()
        {
            var remoteMovie = Subject.Map(Parsed("Extended"), "", TmdbId);

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieMatchType.Should().Be(MovieMatchType.Id);
        }

        [Test]
        public void tmdb_lookup_should_route_by_alias_and_multi_word_editions()
        {
            Subject.Map(Parsed("Extended Cut"), "", TmdbId).Movie.Should().BeSameAs(_extended);
            Subject.Map(Parsed("Extended Remastered"), "", TmdbId).Movie.Should().BeSameAs(_extended);
        }

        [TestCase(null)]
        [TestCase("")]
        public void tmdb_lookup_should_route_an_untagged_release_to_the_default_row(string edition)
        {
            Subject.Map(Parsed(edition), "", TmdbId).Movie.Should().BeSameAs(_default);
        }

        [Test]
        public void tmdb_lookup_should_route_an_unknown_edition_to_the_default_row()
        {
            Subject.Map(Parsed("Director's Cut"), "", TmdbId).Movie.Should().BeSameAs(_default);
        }

        [Test]
        public void tmdb_lookup_should_still_reject_a_wrong_year()
        {
            Subject.Map(Parsed("Extended", 2003), "", TmdbId).Movie.Should().BeNull();
        }

        [Test]
        public void tmdb_lookup_should_route_an_edition_release_to_the_default_row_when_it_is_the_only_row()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindAllByTmdbId(TmdbId))
                  .Returns(new List<Movie> { _default });

            Subject.Map(Parsed("Extended"), "", TmdbId).Movie.Should().BeSameAs(_default);
        }

        [Test]
        public void imdb_lookup_should_route_a_tagged_release_to_its_edition_row()
        {
            var remoteMovie = Subject.Map(Parsed("Extended"), ImdbId, 0);

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieMatchType.Should().Be(MovieMatchType.Id);
        }

        [Test]
        public void imdb_lookup_should_route_an_untagged_release_to_the_default_row_without_loading_siblings()
        {
            Subject.Map(Parsed(""), ImdbId, 0).Movie.Should().BeSameAs(_default);

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void imdb_lookup_should_route_an_unknown_edition_to_the_default_row()
        {
            Subject.Map(Parsed("Unrated"), ImdbId, 0).Movie.Should().BeSameAs(_default);
        }

        [Test]
        public void imdb_lookup_should_return_nothing_for_an_unknown_id()
        {
            Subject.Map(Parsed("Extended"), "tt0000001", 0).Movie.Should().BeNull();
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Extended")]
        [TestCase("Director's Cut")]
        public void search_for_an_edition_row_should_map_every_tmdb_release_to_the_searched_row(string edition)
        {
            var remoteMovie = Subject.Map(Parsed(edition), "", TmdbId, SearchFor(_extended));

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieRequested.Should().BeTrue();
        }

        [TestCase("")]
        [TestCase("Extended")]
        public void search_for_an_edition_row_should_map_every_imdb_release_to_the_searched_row(string edition)
        {
            var remoteMovie = Subject.Map(Parsed(edition), ImdbId, 0, SearchFor(_extended));

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieRequested.Should().BeTrue();
        }

        [TestCase("")]
        [TestCase("Extended")]
        public void search_for_an_edition_row_should_map_a_title_only_release_to_the_searched_row(string edition)
        {
            var remoteMovie = Subject.Map(Parsed(edition), "", 0, SearchFor(_extended));

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieMatchType.Should().Be(MovieMatchType.Title);
            remoteMovie.MovieRequested.Should().BeTrue();
        }

        [Test]
        public void search_for_the_default_row_should_not_reroute_a_tagged_release_to_the_edition_row()
        {
            var remoteMovie = Subject.Map(Parsed("Extended"), "", TmdbId, SearchFor(_default));

            remoteMovie.Movie.Should().BeSameAs(_default);
            remoteMovie.MovieRequested.Should().BeTrue();

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void search_for_another_movie_should_route_the_release_normally()
        {
            var otherMovie = new Movie { Id = 99, MovieMetadataId = 8, MovieMetadata = new MovieMetadata { Id = 8, TmdbId = 604, Title = "Other", CleanTitle = "other", Year = 2003 } };

            var remoteMovie = Subject.Map(Parsed("Extended"), "", TmdbId, SearchFor(otherMovie));

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieRequested.Should().BeFalse();
        }

        [Test]
        public void title_lookup_should_pass_the_parsed_edition_to_the_movie_service()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitle(It.IsAny<List<string>>(), 1999, It.IsAny<List<string>>(), _rows, "Extended"))
                  .Returns(_extended);

            var remoteMovie = Subject.Map(Parsed("Extended"), "", 0);

            remoteMovie.Movie.Should().BeSameAs(_extended);
            remoteMovie.MovieMatchType.Should().Be(MovieMatchType.Title);

            Mocker.GetMock<IMovieService>()
                  .Verify(v => v.FindByTitle(It.IsAny<List<string>>(), It.IsAny<int?>(), It.IsAny<List<string>>(), It.IsAny<List<Movie>>()), Times.Never());
        }

        [Test]
        public void get_movie_should_route_a_tagged_title_by_its_edition()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitle(It.IsAny<List<string>>(), 1999, It.IsAny<List<string>>(), _rows, "Extended"))
                  .Returns(_extended);

            Subject.GetMovie("The.Matrix.1999.Extended.1080p.BluRay.x264-GRP").Should().BeSameAs(_extended);
        }

        [Test]
        public void get_movie_should_ask_for_the_default_row_for_an_untagged_title()
        {
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.FindByTitle(It.IsAny<List<string>>(), 1999, It.IsAny<List<string>>(), _rows, It.Is<string>(e => string.IsNullOrWhiteSpace(e))))
                  .Returns(_default);

            Subject.GetMovie("The.Matrix.1999.1080p.BluRay.x264-GRP").Should().BeSameAs(_default);
        }
    }
}
