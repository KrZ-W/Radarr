using System.Collections.Generic;  // krzw(multi-edition)
using System.Linq;
using FluentValidation.Validators;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Movies;

namespace NzbDrone.Core.Validation.Paths
{
    public class MoviePathValidator : PropertyValidator
    {
        private readonly IMovieService _moviesService;

        public MoviePathValidator(IMovieService moviesService)
        {
            _moviesService = moviesService;
        }

        protected override string GetDefaultMessageTemplate() => "Path '{path}' is already configured for an existing movie";

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            dynamic instance = context.ParentContext.InstanceToValidate;
            var instanceId = (int)instance.Id;

            context.MessageFormatter.AppendArgument("path", context.PropertyValue.ToString());

            var sharedRowIds = EditionRowIds(context.ParentContext.InstanceToValidate);  // krzw(multi-edition)

            // Skip the path for this movie and any invalid paths
            return !_moviesService.AllMoviePaths().Any(s => s.Key != instanceId &&
                                                            !sharedRowIds.Contains(s.Key) &&  // krzw(multi-edition): the title's other rows share its folder
                                                            s.Value.IsPathValid(PathValidationType.CurrentOs) &&
                                                            s.Value.PathEquals(context.PropertyValue.ToString()));
        }

        // krzw(multi-edition) begin
        // The default row and the edition rows of one title (same TMDb id) share one folder, so their paths may be
        // equal. Instances without a TMDb id (the recycle bin setting also runs this validator) share with nobody.
        private HashSet<int> EditionRowIds(object instance)
        {
            var tmdbId = instance?.GetType().GetProperty("TmdbId")?.GetValue(instance) as int? ?? 0;

            if (tmdbId <= 0)
            {
                return new HashSet<int>();
            }

            return (_moviesService.FindAllByTmdbId(tmdbId) ?? new List<Movie>()).Select(m => m.Id).ToHashSet();
        }

        // krzw(multi-edition) end
    }
}
