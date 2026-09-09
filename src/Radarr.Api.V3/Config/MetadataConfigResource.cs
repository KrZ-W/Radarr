using NzbDrone.Core.Configuration;
using NzbDrone.Core.MetadataSource.SkyHook.Resource;
using Radarr.Http.REST;

namespace Radarr.Api.V3.Config
{
    public class MetadataConfigResource : RestResource
    {
        public TMDbCountryCode CertificationCountry { get; set; }

        // krzw(imdb-title-provider)
        public bool ImdbTitleProviderEnabled { get; set; }
        public string ImdbTitleProviderRegions { get; set; }
        public string ImdbTitleProviderLanguages { get; set; }
        public int ImdbTitleProviderRefreshInterval { get; set; }
    }

    public static class MetadataConfigResourceMapper
    {
        public static MetadataConfigResource ToResource(IConfigService model)
        {
            return new MetadataConfigResource
            {
                CertificationCountry = model.CertificationCountry,

                // krzw(imdb-title-provider)
                ImdbTitleProviderEnabled = model.ImdbTitleProviderEnabled,
                ImdbTitleProviderRegions = model.ImdbTitleProviderRegions,
                ImdbTitleProviderLanguages = model.ImdbTitleProviderLanguages,
                ImdbTitleProviderRefreshInterval = model.ImdbTitleProviderRefreshInterval,
            };
        }
    }
}
