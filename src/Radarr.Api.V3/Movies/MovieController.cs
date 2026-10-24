using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentValidation;
using FluentValidation.Results;  // krzw(multi-edition)
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.DecisionEngine.Specifications;
using NzbDrone.Core.Languages;
using NzbDrone.Core.MediaCover;
using NzbDrone.Core.MediaFiles.Events;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Commands;
using NzbDrone.Core.Movies.Editions;  // krzw(multi-edition)
using NzbDrone.Core.Movies.Events;
using NzbDrone.Core.Movies.Translations;
using NzbDrone.Core.MovieStats;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Validation;
using NzbDrone.Core.Validation.Paths;
using NzbDrone.SignalR;
using Radarr.Http;
using Radarr.Http.REST;
using Radarr.Http.REST.Attributes;

namespace Radarr.Api.V3.Movies
{
    [V3ApiController]
    public class MovieController : RestControllerWithSignalR<MovieResource, Movie>,
                                IHandle<MovieUpdatedEvent>,
                                IHandle<MovieEditedEvent>,
                                IHandle<MoviesDeletedEvent>,
                                IHandle<MovieRenamedEvent>,
                                IHandle<MoviesBulkEditedEvent>,
                                IHandle<MediaCoversUpdatedEvent>
    {
        private readonly IMovieService _moviesService;
        private readonly IMovieTranslationService _movieTranslationService;
        private readonly IAddMovieService _addMovieService;
        private readonly IAddMovieEditionService _addMovieEditionService;  // krzw(multi-edition)
        private readonly IMovieStatisticsService _movieStatisticsService;
        private readonly IMapCoversToLocal _coverMapper;
        private readonly IManageCommandQueue _commandQueueManager;
        private readonly IRootFolderService _rootFolderService;
        private readonly IUpgradableSpecification _qualityUpgradableSpecification;
        private readonly IConfigService _configService;

        public MovieController(IBroadcastSignalRMessage signalRBroadcaster,
                           IMovieService moviesService,
                           IMovieTranslationService movieTranslationService,
                           IAddMovieService addMovieService,
                           IAddMovieEditionService addMovieEditionService,  // krzw(multi-edition)
                           IMovieStatisticsService movieStatisticsService,
                           IMapCoversToLocal coverMapper,
                           IManageCommandQueue commandQueueManager,
                           IRootFolderService rootFolderService,
                           IUpgradableSpecification qualityUpgradableSpecification,
                           IConfigService configService,
                           RootFolderValidator rootFolderValidator,
                           MappedNetworkDriveValidator mappedNetworkDriveValidator,
                           MoviePathValidator moviesPathValidator,
                           MovieExistsValidator moviesExistsValidator,
                           MovieAncestorValidator moviesAncestorValidator,
                           RecycleBinValidator recycleBinValidator,
                           SystemFolderValidator systemFolderValidator,
                           QualityProfileExistsValidator qualityProfileExistsValidator,
                           RootFolderExistsValidator rootFolderExistsValidator,
                           MovieFolderAsRootFolderValidator movieFolderAsRootFolderValidator)
            : base(signalRBroadcaster)
        {
            _moviesService = moviesService;
            _movieTranslationService = movieTranslationService;
            _addMovieService = addMovieService;
            _addMovieEditionService = addMovieEditionService;  // krzw(multi-edition)
            _movieStatisticsService = movieStatisticsService;
            _qualityUpgradableSpecification = qualityUpgradableSpecification;
            _configService = configService;
            _coverMapper = coverMapper;
            _commandQueueManager = commandQueueManager;
            _rootFolderService = rootFolderService;

            SharedValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
                .IsValidPath()
                .SetValidator(rootFolderValidator)
                .SetValidator(mappedNetworkDriveValidator)
                .SetValidator(moviesPathValidator)
                .SetValidator(moviesAncestorValidator)
                .SetValidator(recycleBinValidator)
                .SetValidator(systemFolderValidator)
                .When(s => s.Path.IsNotNullOrWhiteSpace() && s.Edition == null);  // krzw(multi-edition): edition rows share the default row's folder (AddEdition copies it, UpdateMovie keeps it)

            PostValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .When(s => s.RootFolderPath.IsNullOrWhiteSpace() && s.Edition == null);  // krzw(multi-edition)
            PostValidator.RuleFor(s => s.RootFolderPath).Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath()
                .SetValidator(rootFolderExistsValidator)
                .SetValidator(movieFolderAsRootFolderValidator)
                .When(s => s.Path.IsNullOrWhiteSpace() && s.Edition == null);  // krzw(multi-edition)

            PutValidator.RuleFor(s => s.Path).Cascade(CascadeMode.Stop)
                .NotEmpty()
                .IsValidPath();

            SharedValidator.RuleFor(s => s.QualityProfileId).Cascade(CascadeMode.Stop)
                .ValidId()
                .SetValidator(qualityProfileExistsValidator);

            PostValidator.RuleFor(s => s.Title).NotEmpty().When(s => s.TmdbId <= 0);
            PostValidator.RuleFor(s => s.TmdbId).NotNull().NotEmpty().SetValidator(moviesExistsValidator);

            // krzw(multi-edition) begin: the edition object (POST adds an edition row, PUT edits one; MovieExistsValidator keeps POSTed names unique)
            SharedValidator.RuleFor(s => s.Edition.Name).Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage(EditionValidation.NameRequiredMessage)
                .Must(name => name.Trim().Length <= EditionValidation.MaxNameLength).WithMessage(string.Format(EditionValidation.NameTooLongMessage, EditionValidation.MaxNameLength))
                .When(s => s.Edition != null);

            // krzw(multi-edition) end
        }

        [HttpGet]
        public List<MovieResource> AllMovie(int? tmdbId, bool excludeLocalCovers = false, int? languageId = null, bool includeEditions = false)  // krzw(multi-edition): edition rows are hidden unless asked for
        {
            var moviesResources = new List<MovieResource>();

            var translationLanguage = languageId is > 0
                ? Language.All.Single(l => l.Id == languageId.Value)
                : (Language)_configService.MovieInfoLanguage;

            if (tmdbId.HasValue)
            {
                // krzw(multi-edition) begin: one query for the title's rows; the default row lists its editions either way
                var rows = _moviesService.FindAllByTmdbId(tmdbId.Value);

                foreach (var movie in rows.Where(m => includeEditions || m.IsDefaultEdition))
                {
                    moviesResources.AddIfNotNull(MapToResource(movie, translationLanguage, rows));
                }

                // krzw(multi-edition) end
            }
            else
            {
                var movieStats = _movieStatisticsService.MovieStatistics();
                var availDelay = _configService.AvailabilityDelay;

                var movieTask = Task.Run(() => _moviesService.GetAllMovies());

                var translations = _movieTranslationService
                    .GetAllTranslationsForLanguage(translationLanguage);

                // krzw(regional-translations): several rows per language; keep the preferred variant
                var tdict = translations.OrderByRegionalPreference(_configService.RegionalTranslationVariants).ToDictionaryIgnoreDuplicates(x => x.MovieMetadataId);
                var sdict = movieStats.ToDictionary(x => x.MovieId);

                var movies = movieTask.GetAwaiter().GetResult();

                // krzw(multi-edition) begin: group the rows once so every default row can list its editions without a query per movie
                var rowsByMetadata = movies.GroupBy(m => m.MovieMetadataId).ToDictionary(g => g.Key, g => g.ToList());

                if (!includeEditions)
                {
                    movies = movies.Where(m => m.IsDefaultEdition).ToList();
                }

                // krzw(multi-edition) end
                moviesResources = new List<MovieResource>(movies.Count);

                foreach (var movie in movies)
                {
                    var translation = GetTranslationFromDict(tdict, movie.MovieMetadata, translationLanguage);
                    var resource = movie.ToResource(availDelay, translation, _qualityUpgradableSpecification);  // krzw(multi-edition)
                    moviesResources.Add(resource.LinkEditions(movie, rowsByMetadata[movie.MovieMetadataId]));  // krzw(multi-edition)
                }

                if (!excludeLocalCovers)
                {
                    MapCoversToLocal(moviesResources.ToArray());
                }

                LinkMovieStatistics(moviesResources, sdict);

                var rootFolders = _rootFolderService.All();

                moviesResources.ForEach(m => m.RootFolderPath = _rootFolderService.GetBestRootFolderPath(m.Path, rootFolders));
            }

            return moviesResources;
        }

        protected override MovieResource GetResourceById(int id)
        {
            var movie = _moviesService.GetMovie(id);

            return MapToResource(movie);
        }

        protected MovieResource MapToResource(Movie movie, Language translationLanguage = null, List<Movie> titleRows = null)  // krzw(multi-edition): titleRows = the title's rows when the caller has them
        {
            if (movie == null)
            {
                return null;
            }

            translationLanguage ??= (Language)_configService.MovieInfoLanguage;
            var availDelay = _configService.AvailabilityDelay;

            var translations = _movieTranslationService.GetAllTranslationsForMovieMetadata(movie.MovieMetadataId);
            var translation = GetMovieTranslation(translations, movie.MovieMetadata, translationLanguage);

            var resource = movie.ToResource(availDelay, translation, _qualityUpgradableSpecification);
            resource.LinkEditions(movie, titleRows ?? _moviesService.FindAllByTmdbId(movie.TmdbId));  // krzw(multi-edition): one query, like the statistics lookup below
            MapCoversToLocal(resource);
            FetchAndLinkMovieStatistics(resource);

            resource.RootFolderPath = _rootFolderService.GetBestRootFolderPath(resource.Path);

            return resource;
        }

        private MovieTranslation GetMovieTranslation(List<MovieTranslation> translations, MovieMetadata movie, Language configLanguage)
        {
            if (configLanguage == Language.Original)
            {
                return new MovieTranslation
                {
                    Title = movie.OriginalTitle,
                    Overview = movie.Overview
                };
            }

            // krzw(regional-translations): several rows per language; keep the preferred variant
            return translations.Where(t => t.Language == configLanguage && t.MovieMetadataId == movie.Id)
                               .OrderByRegionalPreference(_configService.RegionalTranslationVariants)
                               .FirstOrDefault();
        }

        private MovieTranslation GetTranslationFromDict(Dictionary<int, MovieTranslation> translations, MovieMetadata movie, Language configLanguage)
        {
            if (configLanguage == Language.Original)
            {
                return new MovieTranslation
                {
                    Title = movie.OriginalTitle,
                    Overview = movie.Overview
                };
            }

            if (!translations.TryGetValue(movie.Id, out var translation))
            {
                translation = new MovieTranslation
                {
                    Title = movie.Title,
                    Language = Language.English
                };
            }

            return translation;
        }

        [RestPostById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public ActionResult<MovieResource> AddMovie([FromBody] MovieResource moviesResource)
        {
            // krzw(multi-edition) begin: an edition object adds an edition row to the title's existing default row (path and root folder come from it)
            if (moviesResource.Edition != null)
            {
                var edition = _addMovieEditionService.AddEdition(moviesResource.ToModel(), moviesResource.TmdbId);

                return Created(edition.Id);
            }

            // krzw(multi-edition) end
            var movie = _addMovieService.AddMovie(moviesResource.ToModel());

            return Created(movie.Id);
        }

        [RestPutById]
        [Consumes("application/json")]
        [Produces("application/json")]
        public ActionResult<MovieResource> UpdateMovie([FromBody] MovieResource moviesResource, [FromQuery] bool moveFiles = false)
        {
            var movie = _moviesService.GetMovie(moviesResource.Id);

            ValidateEditionUpdate(movie, moviesResource);  // krzw(multi-edition): before a move command is queued

            if (moveFiles)
            {
                var sourcePath = movie.Path;
                var destinationPath = moviesResource.Path;

                _commandQueueManager.Push(new MoveMovieCommand
                {
                    MovieId = movie.Id,
                    SourcePath = sourcePath,
                    DestinationPath = destinationPath
                },
                    trigger: CommandTrigger.Manual);
            }

            var model = moviesResource.ToModel(movie);

            var updatedMovie = _moviesService.UpdateMovie(model);

            BroadcastResourceChange(ModelAction.Updated, MapToResource(updatedMovie));

            return Accepted(moviesResource.Id);
        }

        [RestDeleteById]
        public void DeleteMovie(int id, bool deleteFiles = false, bool addImportExclusion = false)
        {
            _moviesService.DeleteMovie(id, deleteFiles, addImportExclusion);
        }

        // krzw(multi-edition) begin
        // Edition rows follow their default row's folder (the path is read-only) and keep their edition when a client
        // that does not know the field sends none. A default row cannot be turned into an edition row (its title would
        // lose the row legacy lookups resolve to), and a renamed edition must stay unique among the title's rows.
        private void ValidateEditionUpdate(Movie storedMovie, MovieResource resource)
        {
            var failures = new List<ValidationFailure>();

            if (storedMovie.IsDefaultEdition)
            {
                if (resource.Edition != null)
                {
                    failures.Add(new ValidationFailure("Edition", "The default edition of a movie cannot be turned into an edition; add an edition instead", resource.Edition.Name));
                }
            }
            else
            {
                if (resource.Path.IsNotNullOrWhiteSpace() && !resource.Path.PathEquals(storedMovie.Path))
                {
                    failures.Add(new ValidationFailure("Path", $"The path of the {storedMovie.Edition} edition follows its default edition and cannot be changed on its own", resource.Path));
                }

                if (resource.Edition == null)
                {
                    resource.Edition = storedMovie.ToEditionResource();
                }
                else
                {
                    var problem = EditionValidation.NameProblem(resource.Edition.Name);

                    if (problem != null)
                    {
                        failures.Add(new ValidationFailure("Edition", problem, resource.Edition.Name));
                    }
                    else if (EditionValidation.NameCollides(_moviesService.FindAllByTmdbId(storedMovie.TmdbId), resource.Edition.Name, storedMovie.Id))
                    {
                        failures.Add(new ValidationFailure("Edition", string.Format(EditionValidation.AlreadyAddedMessage, resource.Edition.Name.Trim()), resource.Edition.Name));
                    }
                    else
                    {
                        resource.Edition.Aliases = EditionValidation.BuildAliases(resource.Edition.Name, resource.Edition.Aliases);
                    }
                }
            }

            if (failures.Any())
            {
                throw new ValidationException(failures);
            }
        }

        // krzw(multi-edition) end

        private void MapCoversToLocal(params MovieResource[] movies)
        {
            foreach (var movieResource in movies)
            {
                _coverMapper.ConvertToLocalUrls(movieResource.Id, movieResource.Images);
            }
        }

        private void FetchAndLinkMovieStatistics(MovieResource resource)
        {
            LinkMovieStatistics(resource, _movieStatisticsService.MovieStatistics(resource.Id));
        }

        private void LinkMovieStatistics(List<MovieResource> resources, Dictionary<int, MovieStatistics> sDict)
        {
            foreach (var movie in resources)
            {
                if (sDict.TryGetValue(movie.Id, out var stats))
                {
                    LinkMovieStatistics(movie, stats);
                }
            }
        }

        private void LinkMovieStatistics(MovieResource resource, MovieStatistics movieStatistics)
        {
            resource.Statistics = movieStatistics.ToResource();
            resource.HasFile = movieStatistics.MovieFileCount > 0;
            resource.SizeOnDisk = movieStatistics.SizeOnDisk;
        }

        [NonAction]
        public void Handle(MovieUpdatedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Movie));
        }

        [NonAction]
        public void Handle(MovieEditedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Movie));
        }

        [NonAction]
        public void Handle(MoviesDeletedEvent message)
        {
            foreach (var movie in message.Movies)
            {
                BroadcastResourceChange(ModelAction.Deleted, movie.Id);
            }
        }

        [NonAction]
        public void Handle(MovieRenamedEvent message)
        {
            BroadcastResourceChange(ModelAction.Updated, MapToResource(message.Movie));
        }

        [NonAction]
        public void Handle(MoviesBulkEditedEvent message)
        {
            foreach (var movie in message.Movies)
            {
                BroadcastResourceChange(ModelAction.Updated, MapToResource(movie));
            }
        }

        [NonAction]
        public void Handle(MediaCoversUpdatedEvent message)
        {
            if (message.Updated)
            {
                BroadcastResourceChange(ModelAction.Updated, message.Movie.Id);
            }
        }
    }
}
