namespace NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators.Augmenters.Language
{
    public enum Confidence
    {
        Default,
        Filename,
        Foldername,
        DownloadClientItem,
        MediaInfo,
        AudioProbe  // krzw(audio-language-verification): detector result outranks the tags
    }
}
