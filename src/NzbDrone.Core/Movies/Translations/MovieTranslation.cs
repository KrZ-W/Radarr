using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies.AlternativeTitles;

namespace NzbDrone.Core.Movies.Translations
{
    public class MovieTranslation : Entity<MovieTranslation>
    {
        public int MovieMetadataId { get; set; }
        public string Title { get; set; }
        public string CleanTitle { get; set; }
        public string Overview { get; set; }
        public Language Language { get; set; }
        public string RegionalLanguage { get; set; }
        public SourceType SourceType { get; set; }
    }
}
