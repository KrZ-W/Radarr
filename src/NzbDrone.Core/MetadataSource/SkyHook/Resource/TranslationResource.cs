using Newtonsoft.Json;  // krzw(regional-translations)

namespace NzbDrone.Core.MetadataSource.SkyHook.Resource
{
    public class TranslationResource
    {
        public string Title { get; set; }
        public string Overview { get; set; }
        public string Language { get; set; }

        // krzw(regional-translations): extra TMDB fields (currently unreferenced)
        [JsonProperty("iso_639_1")]
        public string Iso6391 { get; set; }

        [JsonProperty("iso_3166_1")]
        public string Iso31661 { get; set; }

        [JsonProperty("data")]
        public TranslationDataResource Data { get; set; }
    }

    // krzw(regional-translations)
    public class TranslationDataResource
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("overview")]
        public string Overview { get; set; }
    }
}
