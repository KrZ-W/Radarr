using System.Collections.Generic;  // krzw(multi-edition)
using System.Linq;  // krzw(multi-edition)
using FluentValidation.Validators;
using NzbDrone.Common.Extensions;  // krzw(multi-edition)
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions;  // krzw(multi-edition)

namespace NzbDrone.Core.Validation.Paths
{
    public class MovieExistsValidator : PropertyValidator
    {
        private readonly IMovieService _movieService;

        public MovieExistsValidator(IMovieService movieService)
        {
            _movieService = movieService;
        }

        protected override string GetDefaultMessageTemplate() => "This movie{edition} has already been added";  // krzw(multi-edition): {edition} is "" for a plain movie

        protected override bool IsValid(PropertyValidatorContext context)
        {
            if (context.PropertyValue == null)
            {
                return true;
            }

            var tmdbId = (int)context.PropertyValue;

            // krzw(multi-edition) begin: an edition row may join the title's existing default row; the same edition twice may not
            var editionName = EditionNameOf(context.ParentContext.InstanceToValidate);

            if (editionName.IsNotNullOrWhiteSpace())
            {
                context.MessageFormatter.AppendArgument("edition", $" edition '{editionName}'");

                return !EditionAlreadyAdded(tmdbId, editionName);
            }

            context.MessageFormatter.AppendArgument("edition", "");

            // krzw(multi-edition) end
            return _movieService.FindByTmdbId(tmdbId) == null;
        }

        // krzw(multi-edition) begin
        // The edition the instance asks for: Movie.Edition (string) or MovieResource.Edition.Name (object), "" otherwise.
        private static string EditionNameOf(object instance)
        {
            var edition = instance?.GetType().GetProperty("Edition")?.GetValue(instance);

            switch (edition)
            {
                case null:
                    return "";
                case string name:
                    return name;
                default:
                    return edition.GetType().GetProperty("Name")?.GetValue(edition) as string ?? "";
            }
        }

        // A duplicate is an existing edition row whose name or any alias normalizes to the new name; the default row
        // never collides (it has no edition name).
        private bool EditionAlreadyAdded(int tmdbId, string editionName)
        {
            var normalized = EditionNormalizer.Normalize(editionName);

            return (_movieService.FindAllByTmdbId(tmdbId) ?? new List<Movie>())
                   .Where(row => row.IsEdition)
                   .Any(row => EditionNormalizer.Normalize(row.Edition) == normalized ||
                               row.EditionAliases.Any(alias => EditionNormalizer.Normalize(alias) == normalized));
        }

        // krzw(multi-edition) end
    }
}
