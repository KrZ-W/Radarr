// krzw(edition-runtime-catalog): labelled durations (P2047 qualified by P518) from Wikidata's SPARQL endpoint
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Movies.Editions.Catalog.Providers
{
    // Covers few titles but is CC0 and cheap: 250 ids per query, one retry then skip. Unlabelled durations are ignored
    // (IMDb and TMDb already give the theatrical one).
    public class WikidataRuntimeProvider : IEditionCatalogProvider
    {
        public const string Endpoint = "https://query.wikidata.org/sparql";
        public const int BatchSize = 250;

        private static readonly Regex ImdbIdRegex = new Regex(@"^tt\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // An item without an English label comes back as its Q-id
        private static readonly Regex EntityIdRegex = new Regex(@"^Q\d+$", RegexOptions.Compiled);

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly Logger _logger;

        public WikidataRuntimeProvider(IHttpClient httpClient, IConfigService configService, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _logger = logger;
        }

        public EditionCatalogSource Source => EditionCatalogSource.Wikidata;

        public bool Enabled => _configService.EditionCatalogWikidataEnabled;

        public static string UserAgent => $"KrZ-W-Radarr/{BuildInfo.Version} (https://github.com/KrZ-W/Radarr)";

        public Dictionary<int, List<EditionCatalogEntry>> Fetch(List<MovieMetadata> metadata)
        {
            var result = new Dictionary<int, List<EditionCatalogEntry>>();
            var byImdbId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var title in (metadata ?? new List<MovieMetadata>()).Where(m => m != null && m.Id > 0))
            {
                var imdbId = title.ImdbId?.Trim();

                if (imdbId.IsNullOrWhiteSpace() || !ImdbIdRegex.IsMatch(imdbId))
                {
                    result[title.Id] = new List<EditionCatalogEntry>();
                    continue;
                }

                if (!byImdbId.TryGetValue(imdbId, out var ids))
                {
                    ids = new List<int>();
                    byImdbId[imdbId] = ids;
                }

                ids.Add(title.Id);
            }

            foreach (var batch in byImdbId.Keys.Chunk(BatchSize))
            {
                var batchIds = batch.ToList();
                var entries = FetchBatch(batchIds);

                if (entries == null)
                {
                    continue;
                }

                foreach (var imdbId in batchIds)
                {
                    entries.TryGetValue(imdbId, out var found);

                    foreach (var metadataId in byImdbId[imdbId])
                    {
                        result[metadataId] = (found ?? new List<EditionCatalogEntry>()).Select(e => new EditionCatalogEntry
                        {
                            MovieMetadataId = metadataId,
                            Name = e.Name,
                            RuntimeMinutes = e.RuntimeMinutes,
                            Source = e.Source,
                            Confidence = e.Confidence,
                            Attributes = e.Attributes.ToList()
                        }).ToList();
                    }
                }
            }

            return result;
        }

        private Dictionary<string, List<EditionCatalogEntry>> FetchBatch(List<string> imdbIds)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var request = new HttpRequest(Endpoint + "?format=json&query=" + Uri.EscapeDataString(BuildQuery(imdbIds)));
                    request.Headers.Accept = "application/sparql-results+json";
                    request.Headers.Add("User-Agent", UserAgent);
                    request.RequestTimeout = TimeSpan.FromSeconds(60);
                    request.LogResponseContent = false;

                    var response = _httpClient.Get(request);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new HttpException(response);
                    }

                    return Parse(response.Content);
                }
                catch (Exception ex)
                {
                    if (attempt == 1)
                    {
                        _logger.Debug(ex, "Wikidata runtimes query for {0} titles failed, retrying once", imdbIds.Count);
                    }
                    else
                    {
                        _logger.Warn(ex, "Wikidata runtimes query for {0} titles failed twice, skipping them", imdbIds.Count);
                    }
                }
            }

            return null;
        }

        public static string BuildQuery(IEnumerable<string> imdbIds)
        {
            var values = string.Join(" ", imdbIds.Select(id => "\"" + id + "\""));

            // ps:P2047 is the bare amount; the unit comes from the full value (psv:) so hours or seconds are converted
            return "SELECT ?imdb ?dur ?unit ?qualLabel WHERE { VALUES ?imdb { " + values + " } " +
                   "?film wdt:P345 ?imdb . ?film p:P2047 ?st . ?st ps:P2047 ?dur . OPTIONAL { ?st pq:P518 ?qual . } " +
                   "OPTIONAL { ?st psv:P2047/wikibase:quantityUnit ?unit . } " +
                   "SERVICE wikibase:label { bd:serviceParam wikibase:language \"en\". } }";
        }

        // Keyed by IMDb id; only durations with an (English) qualifier label are kept
        public static Dictionary<string, List<EditionCatalogEntry>> Parse(string json)
        {
            var result = new Dictionary<string, List<EditionCatalogEntry>>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("results", out var results) ||
                !results.TryGetProperty("bindings", out var bindings) ||
                bindings.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("Wikidata response has no results.bindings list");
            }

            foreach (var binding in bindings.EnumerateArray())
            {
                var imdbId = GetValue(binding, "imdb");
                var duration = GetValue(binding, "dur");
                var label = GetValue(binding, "qualLabel");

                if (imdbId.IsNullOrWhiteSpace() || label.IsNullOrWhiteSpace() || EntityIdRegex.IsMatch(label.Trim()))
                {
                    continue;
                }

                if (!double.TryParse(duration, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    continue;
                }

                var factor = MinutesPerUnit(GetValue(binding, "unit"));

                if (!factor.HasValue)
                {
                    continue;
                }

                // Anything outside a feature length range after the unit conversion is a data error
                var minutes = (int)Math.Round(value * factor.Value, MidpointRounding.AwayFromZero);

                if (minutes <= 0 || minutes > 1000)
                {
                    continue;
                }

                var (name, confidence) = EditionLabelNormalizer.Map(label);

                if (!result.TryGetValue(imdbId, out var list))
                {
                    list = new List<EditionCatalogEntry>();
                    result[imdbId] = list;
                }

                if (list.Any(e => e.Name == name && e.RuntimeMinutes == minutes))
                {
                    continue;
                }

                list.Add(new EditionCatalogEntry
                {
                    Name = name,
                    RuntimeMinutes = minutes,
                    Source = EditionCatalogSource.Wikidata,
                    Confidence = confidence,
                    Attributes = new List<string> { label.Trim() }
                });
            }

            return result;
        }

        // Minutes per unit of a P2047 amount: minute (Q7727, also assumed when the unit is missing), hour (Q25235),
        // second (Q11574); null for any other unit
        public static double? MinutesPerUnit(string unitUri)
        {
            if (unitUri.IsNullOrWhiteSpace())
            {
                return 1;
            }

            var entity = unitUri.Trim().TrimEnd('/');
            entity = entity.Substring(entity.LastIndexOf('/') + 1);

            switch (entity)
            {
                case "Q7727":
                    return 1;
                case "Q25235":
                    return 60;
                case "Q11574":
                    return 1.0 / 60;
                default:
                    return null;
            }
        }

        private static string GetValue(JsonElement binding, string name)
        {
            return binding.ValueKind == JsonValueKind.Object &&
                   binding.TryGetProperty(name, out var element) &&
                   element.ValueKind == JsonValueKind.Object &&
                   element.TryGetProperty("value", out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
    }
}
