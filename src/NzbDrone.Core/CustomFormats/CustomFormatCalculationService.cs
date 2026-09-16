using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Blocklisting;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Profiles.Qualities;

namespace NzbDrone.Core.CustomFormats
{
    public interface ICustomFormatCalculationService
    {
        List<CustomFormat> ParseCustomFormat(RemoteMovie remoteMovie, long size);
        List<CustomFormat> ParseCustomFormat(MovieFile movieFile, Movie movie);
        List<CustomFormat> ParseCustomFormat(MovieFile movieFile);
        List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Movie movie);
        List<CustomFormat> ParseCustomFormat(MovieHistory history, Movie movie);
        List<CustomFormat> ParseCustomFormat(LocalMovie localMovie);

        // krzw(grabbed-release-title): scoring-only ladder, see ParseCustomFormatForScoring below.
        // Naming ({Custom Formats} token) deliberately stays on the legacy ParseCustomFormat overloads.
        List<CustomFormat> ParseCustomFormatForScoring(MovieFile movieFile, Movie movie);
        List<CustomFormat> ParseCustomFormatForScoring(MovieFile movieFile);
        List<CustomFormat> ParseCustomFormatForScoring(LocalMovie localMovie);
    }

    public class CustomFormatCalculationService : ICustomFormatCalculationService
    {
        private readonly ICustomFormatService _formatService;
        private readonly IConfigService _configService;  // krzw(grabbed-release-title)
        private readonly Logger _logger;

        public CustomFormatCalculationService(ICustomFormatService formatService, IConfigService configService, Logger logger)
        {
            _formatService = formatService;
            _configService = configService;  // krzw(grabbed-release-title)
            _logger = logger;
        }

        public List<CustomFormat> ParseCustomFormat(RemoteMovie remoteMovie, long size)
        {
            var input = new CustomFormatInput
            {
                MovieInfo = remoteMovie.ParsedMovieInfo,
                Movie = remoteMovie.Movie,
                Size = size,
                Languages = remoteMovie.Languages,
                IndexerFlags = remoteMovie.Release?.IndexerFlags ?? 0
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(MovieFile movieFile, Movie movie)
        {
            return ParseCustomFormat(movieFile, movie, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(MovieFile movieFile)
        {
            return ParseCustomFormat(movieFile, movieFile.Movie, _formatService.All());
        }

        public List<CustomFormat> ParseCustomFormat(Blocklist blocklist, Movie movie)
        {
            var parsed = Parser.Parser.ParseMovieTitle(blocklist.SourceTitle);

            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { movie.Title },
                SimpleReleaseTitle = parsed?.SimpleReleaseTitle ?? blocklist.SourceTitle.SimplifyReleaseTitle(),
                ReleaseTitle = parsed?.ReleaseTitle ?? blocklist.SourceTitle,
                Year = movie.Year,
                Edition = parsed?.Edition,
                Quality = blocklist.Quality,
                Languages = blocklist.Languages,
                ReleaseGroup = parsed?.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = movie,
                Size = blocklist.Size ?? 0,
                Languages = blocklist.Languages,
                IndexerFlags = blocklist.IndexerFlags
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(MovieHistory history, Movie movie)
        {
            var parsed = Parser.Parser.ParseMovieTitle(history.SourceTitle);

            long.TryParse(history.Data.GetValueOrDefault("size"), out var size);
            Enum.TryParse(history.Data.GetValueOrDefault("indexerFlags"), true, out IndexerFlags indexerFlags);

            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { movie.Title },
                SimpleReleaseTitle = parsed?.SimpleReleaseTitle ?? history.SourceTitle.SimplifyReleaseTitle(),
                ReleaseTitle = parsed?.ReleaseTitle ?? history.SourceTitle,
                Year = movie.Year,
                Edition = parsed?.Edition,
                Quality = history.Quality,
                Languages = history.Languages,
                ReleaseGroup = parsed?.ReleaseGroup,
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = movie,
                Size = size,
                Languages = history.Languages,
                IndexerFlags = indexerFlags
            };

            return ParseCustomFormat(input);
        }

        public List<CustomFormat> ParseCustomFormat(LocalMovie localMovie)
        {
            return ParseCustomFormat(localMovie, GetLegacyReleaseTitle(localMovie), _formatService.All());
        }

        private static List<CustomFormat> ParseCustomFormat(LocalMovie localMovie, string releaseTitle, List<CustomFormat> allCustomFormats)
        {
            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { localMovie.Movie.Title },
                SimpleReleaseTitle = releaseTitle.SimplifyReleaseTitle(),
                ReleaseTitle = localMovie.SceneName,
                Year = localMovie.Movie.Year,
                Quality = localMovie.Quality,
                Edition = localMovie.Edition,
                Languages = localMovie.Languages,
                ReleaseGroup = localMovie.ReleaseGroup
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = localMovie.Movie,
                Size = localMovie.Size,
                Languages = localMovie.Languages,
                AudioTitles = localMovie.MediaInfo?.AudioTitles,  // krzw(audio-title)
                IndexerFlags = localMovie.IndexerFlags,
                Filename = Path.GetFileName(localMovie.Path)
            };

            return ParseCustomFormat(input, allCustomFormats);
        }

        private List<CustomFormat> ParseCustomFormat(CustomFormatInput input)
        {
            return ParseCustomFormat(input, _formatService.All());
        }

        private static List<CustomFormat> ParseCustomFormat(CustomFormatInput input, List<CustomFormat> allCustomFormats)
        {
            var matches = new List<CustomFormat>();

            foreach (var customFormat in allCustomFormats)
            {
                var specificationMatches = customFormat.Specifications
                    .GroupBy(t => t.GetType())
                    .Select(g => new SpecificationMatchesGroup
                    {
                        Matches = g.ToDictionary(t => t, t => t.IsSatisfiedBy(input))
                    })
                    .ToList();

                if (specificationMatches.All(x => x.DidMatch))
                {
                    matches.Add(customFormat);
                }
            }

            return matches.OrderBy(x => x.Name).ToList();
        }

        private List<CustomFormat> ParseCustomFormat(MovieFile movieFile, Movie movie, List<CustomFormat> allCustomFormats)
        {
            return ParseCustomFormat(movieFile, movie, GetLegacyReleaseTitle(movieFile), allCustomFormats);
        }

        // krzw(grabbed-release-title): the title today's chain picks for a file; extracted unchanged
        // from the overload above so the scoring ladder can re-score the same file under other titles.
        private string GetLegacyReleaseTitle(MovieFile movieFile)
        {
            var releaseTitle = string.Empty;

            if (movieFile.SceneName.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using scene name for release title: {0}", movieFile.SceneName);
                releaseTitle = movieFile.SceneName;
            }
            else if (movieFile.OriginalFilePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using original file path for release title: {0}", Path.GetFileName(movieFile.OriginalFilePath));
                releaseTitle = Path.GetFileName(movieFile.OriginalFilePath);
            }
            else if (movieFile.RelativePath.IsNotNullOrWhiteSpace())
            {
                _logger.Trace("Using relative path for release title: {0}", Path.GetFileName(movieFile.RelativePath));
                releaseTitle = Path.GetFileName(movieFile.RelativePath);
            }

            return releaseTitle;
        }

        private static List<CustomFormat> ParseCustomFormat(MovieFile movieFile, Movie movie, string releaseTitle, List<CustomFormat> allCustomFormats)
        {
            var movieInfo = new ParsedMovieInfo
            {
                MovieTitles = new List<string> { movie.Title },
                SimpleReleaseTitle = releaseTitle.SimplifyReleaseTitle(),
                Year = movie.Year,
                Quality = movieFile.Quality,
                Languages = movieFile.Languages,
                ReleaseGroup = movieFile.ReleaseGroup,
                Edition = movieFile.Edition
            };

            var input = new CustomFormatInput
            {
                MovieInfo = movieInfo,
                Movie = movie,
                Size = movieFile.Size,
                Languages = movieFile.Languages,
                AudioTitles = movieFile.MediaInfo?.AudioTitles,  // krzw(audio-title)
                IndexerFlags = movieFile.IndexerFlags,
                Filename = Path.GetFileName(movieFile.RelativePath)
            };

            return ParseCustomFormat(input, allCustomFormats);
        }

        // krzw(grabbed-release-title) -------------------------------------------------------------
        // Scoring-only ladder. The legacy ParseCustomFormat overloads above stay exactly as they were
        // because FileNameBuilder renders the {Custom Formats} naming token through them: changing them
        // in place would make Rename Files propose a library-wide rename.
        //
        // Selection is Pareto over (priority score, total score), never a plain max:
        //
        //   incumbent  = the legacy ladder result (SceneName -> OriginalFilePath -> RelativePath)
        //   candidates = { GrabbedReleaseTitle, SceneName, filename(OriginalFilePath) } + incumbent
        //   eligible   = candidates with priority >= incumbent.priority AND total >= incumbent.total
        //   winner     = best eligible by (total, then priority); ties keep the incumbent
        //
        // The incumbent is always eligible, so neither score can ever decrease. That matters because
        // krzw(cf-priority) compares the priority score before quality (UpgradableSpecification): a
        // plain max-by-total could lower the priority score and cause the very re-grab this prevents.
        //
        // Only the release title varies between candidates. ReleaseGroup, Languages, Quality, Size,
        // IndexerFlags, Edition, AudioTitles and Filename are taken from the file and stay constant.
        public List<CustomFormat> ParseCustomFormatForScoring(MovieFile movieFile, Movie movie)
        {
            var allCustomFormats = _formatService.All();
            var incumbentTitle = GetLegacyReleaseTitle(movieFile);
            var incumbent = ParseCustomFormat(movieFile, movie, incumbentTitle, allCustomFormats);

            if (!TryGetScoringProfile(movieFile.GrabbedReleaseTitle, movie, out var qualityProfile))
            {
                return incumbent;
            }

            var candidates = BuildCandidateTitles(incumbentTitle,
                movieFile.GrabbedReleaseTitle,
                movieFile.SceneName,
                movieFile.OriginalFilePath.IsNotNullOrWhiteSpace() ? Path.GetFileName(movieFile.OriginalFilePath) : null);

            return SelectBestScoringFormats(qualityProfile,
                incumbent,
                candidates,
                title => ParseCustomFormat(movieFile, movie, title, allCustomFormats),
                movieFile.ToString());
        }

        public List<CustomFormat> ParseCustomFormatForScoring(MovieFile movieFile)
        {
            return ParseCustomFormatForScoring(movieFile, movieFile.Movie);
        }

        public List<CustomFormat> ParseCustomFormatForScoring(LocalMovie localMovie)
        {
            var allCustomFormats = _formatService.All();
            var incumbentTitle = GetLegacyReleaseTitle(localMovie);
            var incumbent = ParseCustomFormat(localMovie, incumbentTitle, allCustomFormats);

            if (!TryGetScoringProfile(localMovie.GrabbedReleaseTitle, localMovie.Movie, out var qualityProfile))
            {
                return incumbent;
            }

            var candidates = BuildCandidateTitles(incumbentTitle,
                localMovie.GrabbedReleaseTitle,
                localMovie.SceneName,
                Path.GetFileName(localMovie.Path));

            return SelectBestScoringFormats(qualityProfile,
                incumbent,
                candidates,
                title => ParseCustomFormat(localMovie, title, allCustomFormats),
                localMovie.Path);
        }

        // krzw(grabbed-release-title): the title today's chain picks for a file being imported.
        private static string GetLegacyReleaseTitle(LocalMovie localMovie)
        {
            return localMovie.SceneName.IsNotNullOrWhiteSpace() ? localMovie.SceneName : Path.GetFileName(localMovie.Path);
        }

        // krzw(grabbed-release-title): off by default, and a no-op without a grabbed title or a profile
        // to score against (the profile is taken from the entity in hand - injecting IQualityProfileService
        // here would close a DryIoc constructor ring through SeriesPathBuilder/FileNameBuilder).
        private bool TryGetScoringProfile(string grabbedReleaseTitle, Movie movie, out QualityProfile qualityProfile)
        {
            qualityProfile = movie?.QualityProfile;

            return qualityProfile != null &&
                   grabbedReleaseTitle.IsNotNullOrWhiteSpace() &&
                   _configService.ScoreFilesByGrabbedReleaseTitle;
        }

        // krzw(grabbed-release-title): the alternatives worth re-scoring, incumbent excluded (it is the
        // starting point) and de-duplicated. RelativePath is deliberately not listed: ReleaseTitleSpecification
        // already ORs CustomFormatInput.Filename, which is the relative path's file name, for every candidate.
        private static List<string> BuildCandidateTitles(string incumbentTitle, params string[] titles)
        {
            var candidates = new List<string>();

            foreach (var title in titles)
            {
                if (title.IsNullOrWhiteSpace() ||
                    title.Equals(incumbentTitle, StringComparison.Ordinal) ||
                    candidates.Contains(title, StringComparer.Ordinal))
                {
                    continue;
                }

                candidates.Add(title);
            }

            return candidates;
        }

        // krzw(grabbed-release-title): the Pareto selection described above.
        private List<CustomFormat> SelectBestScoringFormats(QualityProfile qualityProfile,
                                                            List<CustomFormat> incumbentFormats,
                                                            List<string> candidateTitles,
                                                            Func<string, List<CustomFormat>> parse,
                                                            string subject)
        {
            var incumbentScore = qualityProfile.CalculateCustomFormatScore(incumbentFormats);
            var incumbentPriority = qualityProfile.CalculatePriorityFormatScore(incumbentFormats);

            var bestFormats = incumbentFormats;
            var bestScore = incumbentScore;
            var bestPriority = incumbentPriority;
            string bestTitle = null;

            foreach (var title in candidateTitles)
            {
                var formats = parse(title);
                var score = qualityProfile.CalculateCustomFormatScore(formats);
                var priority = qualityProfile.CalculatePriorityFormatScore(formats);

                if (priority < incumbentPriority || score < incumbentScore)
                {
                    _logger.Trace("Release title '{0}' would lower the score of {1} ({2}/{3} vs {4}/{5}), ignoring", title, subject, score, priority, incumbentScore, incumbentPriority);
                    continue;
                }

                if (score > bestScore || (score == bestScore && priority > bestPriority))
                {
                    bestFormats = formats;
                    bestScore = score;
                    bestPriority = priority;
                    bestTitle = title;
                }
            }

            if (bestTitle != null)
            {
                _logger.Debug("Scoring {0} with release title '{1}': [{2}] ({3}, priority {4})", subject, bestTitle, bestFormats.ConcatToString(), bestScore, bestPriority);
            }

            return bestFormats;
        }
    }
}
