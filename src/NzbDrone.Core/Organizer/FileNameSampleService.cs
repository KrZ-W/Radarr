using System.Collections.Generic;
using NzbDrone.Common.Extensions;  // krzw(multi-edition)
using NzbDrone.Core.CustomFormats;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Organizer
{
    public interface IFilenameSampleService
    {
        SampleResult GetMovieSample(NamingConfig nameSpec);
        string GetMovieFolderSample(NamingConfig nameSpec);
    }

    public class FileNameSampleService : IFilenameSampleService
    {
        private readonly IBuildFileNames _buildFileNames;

        private static MovieFile _movieFile;
        private static Movie _movie;
        private static Movie _editionMovie;  // krzw(multi-edition)
        private static List<CustomFormat> _customFormats;

        public FileNameSampleService(IBuildFileNames buildFileNames)
        {
            _buildFileNames = buildFileNames;

            var mediaInfo = new MediaInfoModel
            {
                VideoFormat = "AVC",
                VideoBitDepth = 10,
                VideoMultiViewCount = 2,
                VideoColourPrimaries = "bt2020",
                VideoTransferCharacteristics = "HLG",
                AudioFormat = "DTS",
                AudioChannels = 6,
                AudioChannelPositions = "5.1",
                AudioLanguages = new List<string> { "ger" },
                Subtitles = new List<string> { "eng", "ger" }
            };

            _movieFile = new MovieFile
            {
                Quality = new QualityModel(Quality.Bluray1080p, new Revision(2)),
                RelativePath = "The.Movie.Title.2010.1080p.BluRay.DTS.x264-EVOLVE.mkv",
                SceneName = "The.Movie.Title.2010.1080p.BluRay.DTS.x264-EVOLVE",
                ReleaseGroup = "EVOLVE",
                MediaInfo = mediaInfo,
                Edition = "Ultimate extended edition",
            };

            _movie = new Movie
            {
                MovieMetadata = new MovieMetadata
                {
                    Title = "The Movie: Title",
                    OriginalTitle = "The Original Movie Title",
                    CollectionTitle = "The Movie Collection",
                    CollectionTmdbId = 123654,
                    Certification = "R",
                    Year = 2010,
                    ImdbId = "tt0066921",
                    TmdbId = 345691
                },
                MovieMetadataId = 1
            };

            // krzw(multi-edition) begin: an edition row of the same sample title, used only when the format names {Edition Name}
            _editionMovie = new Movie
            {
                MovieMetadata = _movie.MovieMetadata,
                MovieMetadataId = 1,
                Edition = "Director's Cut",
                EditionAliases = new List<string> { "director's cut", "directors cut", "director's edition" }
            };

            // krzw(multi-edition) end
            _customFormats = new List<CustomFormat>
            {
                new CustomFormat
                {
                    Name = "Surround Sound",
                    IncludeCustomFormatWhenRenaming = true
                },
                new CustomFormat
                {
                    Name = "x264",
                    IncludeCustomFormatWhenRenaming = true
                }
            };
        }

        public SampleResult GetMovieSample(NamingConfig nameSpec)
        {
            var movie = GetSampleMovie(nameSpec);  // krzw(multi-edition)

            var result = new SampleResult
            {
                FileName = BuildSample(movie, _movieFile, nameSpec),  // krzw(multi-edition)
                Movie = movie,  // krzw(multi-edition)
                MovieFile = _movieFile
            };

            return result;
        }

        public string GetMovieFolderSample(NamingConfig nameSpec)
        {
            return _buildFileNames.GetMovieFolder(_movie, nameSpec);
        }

        // krzw(multi-edition) begin: show an edition in the preview only when the format uses {Edition Name},
        // so the default sample (and the examples of users who never adopt the token) stay exactly as before
        private static Movie GetSampleMovie(NamingConfig nameSpec)
        {
            var format = nameSpec?.StandardMovieFormat;

            if (format.IsNotNullOrWhiteSpace() && FileNameBuilder.EditionNameRegex.IsMatch(format))
            {
                return _editionMovie;
            }

            return _movie;
        }

        // krzw(multi-edition) end
        private string BuildSample(Movie movie, MovieFile movieFile, NamingConfig nameSpec)
        {
            try
            {
                return _buildFileNames.BuildFileName(movie, movieFile, nameSpec, _customFormats);
            }
            catch (NamingFormatException)
            {
                return string.Empty;
            }
        }
    }
}
