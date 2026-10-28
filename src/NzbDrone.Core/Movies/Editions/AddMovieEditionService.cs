// krzw(multi-edition): adds an edition row (same metadata and folder as the title's default row) for a movie that is already in the library
using System;
using System.Collections.Generic;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.EnsureThat;

namespace NzbDrone.Core.Movies.Editions
{
    public interface IAddMovieEditionService
    {
        Movie AddEdition(Movie editionRow, int tmdbId);
    }

    public class AddMovieEditionService : IAddMovieEditionService
    {
        private readonly IMovieService _movieService;
        private readonly Logger _logger;

        public AddMovieEditionService(IMovieService movieService, Logger logger)
        {
            _movieService = movieService;
            _logger = logger;
        }

        // editionRow carries what the caller chose: Edition, EditionAliases, EditionAcceptUntagged, ExpectedRuntimeMinutes, QualityProfileId,
        // Monitored, MinimumAvailability, Tags and AddOptions. Metadata, path and root folder come from the default
        // row of tmdbId, so no TMDb call is made here; the MovieAddedEvent published by MovieService.AddMovie runs
        // the refresh, the folder scan and search-on-add exactly as for a normal add.
        public Movie AddEdition(Movie editionRow, int tmdbId)
        {
            Ensure.That(editionRow, () => editionRow).IsNotNull();

            var defaultRow = _movieService.FindByTmdbId(tmdbId);

            if (defaultRow == null)
            {
                throw Failure("TmdbId", $"A movie with TMDb id {tmdbId} has not been added yet; add the movie before adding an edition of it", tmdbId);
            }

            var name = editionRow.Edition;
            var problem = EditionValidation.NameProblem(name);

            if (problem != null)
            {
                throw Failure("Edition", problem, name);
            }

            if (EditionValidation.NameCollides(_movieService.FindAllByTmdbId(tmdbId), name))
            {
                throw Failure("Edition", string.Format(EditionValidation.AlreadyAddedMessage, name), name);
            }

            var movie = new Movie
            {
                MovieMetadata = defaultRow.MovieMetadata,
                MovieMetadataId = defaultRow.MovieMetadataId,
                Path = defaultRow.Path,
                RootFolderPath = defaultRow.RootFolderPath,

                Edition = name,
                EditionAliases = EditionValidation.BuildAliases(name, editionRow.EditionAliases),
                EditionAcceptUntagged = editionRow.EditionAcceptUntagged,
                ExpectedRuntimeMinutes = editionRow.ExpectedRuntimeMinutes,  // krzw(edition-runtime-catalog)

                QualityProfileId = editionRow.QualityProfileId > 0 ? editionRow.QualityProfileId : defaultRow.QualityProfileId,
                Monitored = editionRow.Monitored,
                MinimumAvailability = editionRow.MinimumAvailability,
                Tags = editionRow.Tags ?? new HashSet<int>(),
                AddOptions = editionRow.AddOptions,
                Added = DateTime.UtcNow
            };

            _logger.Info("Adding the {0} edition of {1} Path: [{2}]", name, defaultRow, movie.Path);

            _movieService.UpdateTags(movie);

            return _movieService.AddMovie(movie);
        }

        private static ValidationException Failure(string property, string message, object attemptedValue)
        {
            return new ValidationException(new List<ValidationFailure>
            {
                new ValidationFailure(property, message, attemptedValue)
            });
        }
    }
}
