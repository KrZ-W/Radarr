using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.IndexerSearch.Definitions;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;  // krzw(multi-edition)
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Parser.RomanNumerals;

namespace NzbDrone.Core.Parser
{
    public interface IParsingService
    {
        Movie GetMovie(string title);
        RemoteMovie Map(ParsedMovieInfo parsedMovieInfo, string imdbId, int tmdbId, SearchCriteriaBase searchCriteria = null);
        RemoteMovie Map(ParsedMovieInfo parsedMovieInfo, int movieId);
        ParsedMovieInfo ParseMinimalPathMovieInfo(string path);
    }

    public class ParsingService : IParsingService
    {
        private static HashSet<ArabicRomanNumeral> _arabicRomanNumeralMappings;

        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public ParsingService(IMovieService movieService,
                              Logger logger)
        {
            _movieService = movieService;
            _logger = logger;

            if (_arabicRomanNumeralMappings == null)
            {
                _arabicRomanNumeralMappings = RomanNumeralParser.GetArabicRomanNumeralsMapping();
            }
        }

        public ParsedMovieInfo ParseMinimalPathMovieInfo(string path)
        {
            var fileInfo = new FileInfo(path);

            var result = Parser.ParseMovieTitle(fileInfo.Name, true);

            if (result == null)
            {
                _logger.Debug("Attempting to parse movie info using directory and file names. '{0}'", fileInfo.Directory.Name);
                result = Parser.ParseMovieTitle(fileInfo.Directory.Name + " " + fileInfo.Name);
            }

            if (result == null)
            {
                _logger.Debug("Attempting to parse movie info using directory name. '{0}'", fileInfo.Directory.Name);
                result = Parser.ParseMovieTitle(fileInfo.Directory.Name + fileInfo.Extension);
            }

            return result;
        }

        public Movie GetMovie(string title)
        {
            var parsedMovieInfo = Parser.ParseMovieTitle(title);

            if (parsedMovieInfo == null)
            {
                return _movieService.FindByTitle(title);
            }

            var result = TryGetMovieByTitleAndOrYear(parsedMovieInfo);

            if (result != null)
            {
                return result.Movie;
            }

            return null;
        }

        public RemoteMovie Map(ParsedMovieInfo parsedMovieInfo, string imdbId, int tmdbId, SearchCriteriaBase searchCriteria = null)
        {
            return Map(parsedMovieInfo, imdbId, tmdbId, null, searchCriteria);
        }

        public RemoteMovie Map(ParsedMovieInfo parsedMovieInfo, int movieId)
        {
            return new RemoteMovie
            {
                ParsedMovieInfo = parsedMovieInfo,
                Movie = _movieService.GetMovie(movieId)
            };
        }

        public RemoteMovie Map(ParsedMovieInfo parsedMovieInfo, string imdbId, int tmdbId, Movie movie, SearchCriteriaBase searchCriteria)
        {
            var remoteMovie = new RemoteMovie
            {
                ParsedMovieInfo = parsedMovieInfo
            };

            if (movie == null)
            {
                var movieMatch = FindMovie(parsedMovieInfo, imdbId, tmdbId, searchCriteria);

                if (movieMatch != null)
                {
                    movie = movieMatch.Movie;
                    remoteMovie.MovieMatchType = movieMatch.MatchType;
                    remoteMovie.EditionSiblings = movieMatch.EditionSiblings;  // krzw(multi-edition): the gate reuses the rows routing loaded
                }
            }

            if (movie != null)
            {
                remoteMovie.Movie = movie;
            }

            remoteMovie.Languages = parsedMovieInfo.Languages;

            if (searchCriteria != null)
            {
                remoteMovie.MovieRequested = remoteMovie.Movie?.Id == searchCriteria.Movie?.Id;
            }

            return remoteMovie;
        }

        private FindMovieResult FindMovie(ParsedMovieInfo parsedMovieInfo, string imdbId, int tmdbId, SearchCriteriaBase searchCriteria)
        {
            FindMovieResult result = null;

            if (!string.IsNullOrWhiteSpace(imdbId) && imdbId != "0")
            {
                result = TryGetMovieByImDbId(parsedMovieInfo, imdbId, searchCriteria);  // krzw(multi-edition)
            }

            if (result == null && tmdbId > 0)
            {
                result = TryGetMovieByTmdbId(parsedMovieInfo, tmdbId, searchCriteria);  // krzw(multi-edition)
            }

            if (result == null)
            {
                if (searchCriteria != null)
                {
                    result = TryGetMovieBySearchCriteria(parsedMovieInfo, imdbId, tmdbId, searchCriteria);
                }
                else
                {
                    result = TryGetMovieByTitleAndOrYear(parsedMovieInfo);
                }
            }

            if (result == null)
            {
                _logger.Debug($"No matching movie for titles '{string.Join(", ", parsedMovieInfo.MovieTitles)} ({parsedMovieInfo.Year})'");
            }

            return result;
        }

        private FindMovieResult TryGetMovieByImDbId(ParsedMovieInfo parsedMovieInfo, string imdbId, SearchCriteriaBase searchCriteria)  // krzw(multi-edition)
        {
            var defaultRow = _movieService.FindByImdbId(imdbId);  // krzw(multi-edition): the default row; edition rows are routed below
            var rows = EditionRows(defaultRow, parsedMovieInfo, searchCriteria, out var allRowsLoaded);  // krzw(multi-edition)
            var movie = SelectEditionRow(rows, parsedMovieInfo, searchCriteria);  // krzw(multi-edition)

            // Should fix practically all problems, where indexer is shite at adding correct imdbids to movies.
            if (movie != null && (parsedMovieInfo.Year < 1800 || movie.MovieMetadata.Value.Year == parsedMovieInfo.Year || movie.MovieMetadata.Value.SecondaryYear == parsedMovieInfo.Year))
            {
                return new FindMovieResult(movie, MovieMatchType.Id) { EditionSiblings = allRowsLoaded ? SiblingsOf(movie, rows) : null };  // krzw(multi-edition)
            }

            return null;
        }

        private FindMovieResult TryGetMovieByTmdbId(ParsedMovieInfo parsedMovieInfo, int tmdbId, SearchCriteriaBase searchCriteria)  // krzw(multi-edition)
        {
            var rows = _movieService.FindAllByTmdbId(tmdbId);  // krzw(multi-edition): every row of the title
            var movie = SelectEditionRow(rows, parsedMovieInfo, searchCriteria);  // krzw(multi-edition): routed by the release edition

            // Should fix practically all problems, where indexer is shite at adding correct imdbids to movies.
            if (movie != null && (parsedMovieInfo.Year < 1800 || movie.MovieMetadata.Value.Year == parsedMovieInfo.Year || movie.MovieMetadata.Value.SecondaryYear == parsedMovieInfo.Year))
            {
                return new FindMovieResult(movie, MovieMatchType.Id) { EditionSiblings = SiblingsOf(movie, rows) };  // krzw(multi-edition)
            }

            return null;
        }

        private FindMovieResult TryGetMovieByTitleAndOrYear(ParsedMovieInfo parsedMovieInfo)
        {
            var candidates = _movieService.FindByTitleCandidates(parsedMovieInfo.MovieTitles, out var otherTitles);

            Movie movieByTitleAndOrYear;
            if (parsedMovieInfo.Year > 1800)
            {
                movieByTitleAndOrYear = _movieService.FindByTitle(parsedMovieInfo.MovieTitles, parsedMovieInfo.Year, otherTitles, candidates, parsedMovieInfo.Edition);  // krzw(multi-edition)
                if (movieByTitleAndOrYear != null)
                {
                    return new FindMovieResult(movieByTitleAndOrYear, MovieMatchType.Title) { EditionSiblings = SiblingsOf(movieByTitleAndOrYear, candidates) };  // krzw(multi-edition)
                }

                return null;
            }

            movieByTitleAndOrYear = _movieService.FindByTitle(parsedMovieInfo.MovieTitles, null, otherTitles, candidates, parsedMovieInfo.Edition);  // krzw(multi-edition)
            if (movieByTitleAndOrYear != null)
            {
                return new FindMovieResult(movieByTitleAndOrYear, MovieMatchType.Title) { EditionSiblings = SiblingsOf(movieByTitleAndOrYear, candidates) };  // krzw(multi-edition)
            }

            return null;
        }

        // krzw(multi-edition) begin
        // Picks the Movie row of one title (default + edition rows) a release belongs to. A search maps every
        // release of the searched title onto the searched row: a search for the Extended row must never grab for
        // the default row, and the edition gate decides acceptance there. Otherwise the parsed edition routes:
        // the edition row it names (monitored or not), else the default row.
        private static Movie SelectEditionRow(List<Movie> rows, ParsedMovieInfo parsedMovieInfo, SearchCriteriaBase searchCriteria)
        {
            if (rows == null || rows.Count == 0)
            {
                return null;
            }

            var searchedMovie = searchCriteria?.Movie;

            if (searchedMovie != null && rows.Any(r => r.Id == searchedMovie.Id))
            {
                return searchedMovie;
            }

            return EditionMatcher.SelectRow(rows, parsedMovieInfo.Edition);
        }

        // The default row plus its edition rows. The sibling lookup is skipped when it could not change the
        // outcome: a search for a row of this title (that row is the answer), or an untagged release. allRowsLoaded
        // says whether the result is the title's full row set (only then may the edition gate reuse it).
        private List<Movie> EditionRows(Movie defaultRow, ParsedMovieInfo parsedMovieInfo, SearchCriteriaBase searchCriteria, out bool allRowsLoaded)
        {
            var rows = new List<Movie>();
            allRowsLoaded = false;

            if (defaultRow == null)
            {
                return rows;
            }

            rows.Add(defaultRow);

            var searchedMovie = searchCriteria?.Movie;

            if (searchedMovie != null && searchedMovie.MovieMetadataId > 0 && searchedMovie.MovieMetadataId == defaultRow.MovieMetadataId)
            {
                if (searchedMovie.Id != defaultRow.Id)
                {
                    rows.Add(searchedMovie);
                }

                return rows;
            }

            if (parsedMovieInfo.Edition.IsNotNullOrWhiteSpace())
            {
                rows.AddRange(_movieService.GetEditionSiblings(defaultRow.Id));
                allRowsLoaded = true;
            }

            return rows;
        }

        // The other rows of movie's title among rows (which may hold rows of other titles, as the title candidates do).
        // Routing already materialised them, so the edition gate need not query them again.
        private static List<Movie> SiblingsOf(Movie movie, IEnumerable<Movie> rows)
        {
            if (movie == null || movie.MovieMetadataId <= 0 || rows == null)
            {
                return null;
            }

            return rows.Where(r => r != null && r.Id != movie.Id && r.MovieMetadataId == movie.MovieMetadataId).ToList();
        }

        // krzw(multi-edition) end

        private FindMovieResult TryGetMovieBySearchCriteria(ParsedMovieInfo parsedMovieInfo, string imdbId, int tmdbId, SearchCriteriaBase searchCriteria)
        {
            Movie possibleMovie = null;

            var possibleTitles = new List<string>
            {
                searchCriteria.Movie.MovieMetadata.Value.CleanTitle
            };
            possibleTitles.AddIfNotNull(searchCriteria.Movie.MovieMetadata.Value.CleanOriginalTitle);
            possibleTitles.AddRange(searchCriteria.Movie.MovieMetadata.Value.AlternativeTitles.Select(t => t.CleanTitle));
            possibleTitles.AddRange(searchCriteria.Movie.MovieMetadata.Value.Translations.Select(t => t.CleanTitle));

            var cleanTitles = parsedMovieInfo.MovieTitles.Select(t => t.CleanMovieTitle()).ToArray();

            if (possibleTitles.Any(pt =>
                cleanTitles.Contains(pt)
                || _arabicRomanNumeralMappings.Any(mn =>
                    cleanTitles.Contains(pt.Replace(mn.ArabicNumeralAsString, mn.RomanNumeralLowerCase))
                    || cleanTitles.Any(t => t.Replace(mn.ArabicNumeralAsString, mn.RomanNumeralLowerCase) == pt))))
            {
                possibleMovie = searchCriteria.Movie;
            }

            if (possibleMovie != null)
            {
                if (parsedMovieInfo.Year < 1800 || possibleMovie.MovieMetadata.Value.Year == parsedMovieInfo.Year || possibleMovie.MovieMetadata.Value.SecondaryYear == parsedMovieInfo.Year)
                {
                    return new FindMovieResult(possibleMovie, MovieMatchType.Title);
                }
            }

            if (tmdbId > 0 && tmdbId == searchCriteria.Movie.TmdbId)
            {
                return new FindMovieResult(searchCriteria.Movie, MovieMatchType.Id);
            }

            if (imdbId.IsNotNullOrWhiteSpace() && imdbId == searchCriteria.Movie.ImdbId)
            {
                return new FindMovieResult(searchCriteria.Movie, MovieMatchType.Id);
            }

            return null;
        }
    }
}
