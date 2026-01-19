namespace NzbDrone.Core.Configuration
{
    public enum RegionalTranslationSearchMode
    {
        Standard,      // One title per Language (original behavior)
        OnePerRegion,  // One title per RegionalLanguage
        AllTitles      // All unique CleanTitles
    }
}
