// krzw(edition-runtime-catalog): per-cut runtimes from IMDb's GraphQL endpoint (the one imdb.com itself uses)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Movies.Editions.Catalog.Providers
{
    // The endpoint is unofficial and answers 403 without the headers imdb.com sends. Batched (50 ids per call), spaced
    // (1.5 s), one retry per batch then skip; after 3 failed batches in a row the rest of the run is skipped so a blocked
    // endpoint costs a few calls, not the whole library. The count carries over between Fetch calls (the refresh calls
    // once per chunk) for BlockedBackoff after the last failure. Cached entries of skipped titles are kept by the caller.
    public class ImdbRuntimeProvider : IEditionCatalogProvider
    {
        public const string Url = "https://caching.graphql.imdb.com/";
        public const int BatchSize = 50;
        public const int MaxConsecutiveFailedBatches = 3;
        public const string BrowserUserAgent = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36";
        public const string Query = "query Q($ids:[ID!]!){ titles(ids:$ids){ id titleText{text} runtime{seconds} runtimes(first:20){ edges{ node{ seconds attributes{ text } } } } } }";

        public static readonly TimeSpan BlockedBackoff = TimeSpan.FromHours(1);
        private static readonly Regex ImdbIdRegex = new Regex(@"^tt\d+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private readonly IHttpClient _httpClient;
        private readonly IConfigService _configService;
        private readonly IImdbRuntimeProviderStatus _status;
        private readonly Logger _logger;
        private readonly object _callLock = new object();
        private DateTime _lastCall = DateTime.MinValue;

        public ImdbRuntimeProvider(IHttpClient httpClient, IConfigService configService, IImdbRuntimeProviderStatus status, Logger logger)
        {
            _httpClient = httpClient;
            _configService = configService;
            _status = status;
            _logger = logger;
        }

        public EditionCatalogSource Source => EditionCatalogSource.Imdb;

        public bool Enabled => _configService.EditionCatalogImdbEnabled;

        // Settable so tests do not wait
        public TimeSpan CallSpacing { get; set; } = TimeSpan.FromSeconds(1.5);

        public Dictionary<int, List<EditionCatalogEntry>> Fetch(List<MovieMetadata> metadata)
        {
            var result = new Dictionary<int, List<EditionCatalogEntry>>();
            var byImdbId = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

            foreach (var title in (metadata ?? new List<MovieMetadata>()).Where(m => m != null && m.Id > 0))
            {
                var imdbId = title.ImdbId?.Trim();

                if (imdbId.IsNullOrWhiteSpace() || !ImdbIdRegex.IsMatch(imdbId))
                {
                    // Nothing to ask IMDb: answered, with no entries
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

            // Failures of the previous chunks of this refresh (or of a refresh within the backoff) count too
            var lastFailure = _status.LastFailure;
            var consecutiveFailures = lastFailure.HasValue && DateTime.UtcNow - lastFailure.Value < BlockedBackoff ? _status.ConsecutiveFailedBatches : 0;

            foreach (var batch in byImdbId.Keys.Chunk(BatchSize))
            {
                if (consecutiveFailures >= MaxConsecutiveFailedBatches)
                {
                    _logger.Warn("IMDb runtimes: {0} batches failed in a row, skipping the remaining titles of this refresh", consecutiveFailures);
                    break;
                }

                var titles = FetchBatch(batch.ToList());

                if (titles == null)
                {
                    consecutiveFailures++;
                    _status.RecordBatch(false);
                    continue;
                }

                consecutiveFailures = 0;
                _status.RecordBatch(true);

                foreach (var imdbId in batch)
                {
                    // A title IMDb left out or returned as null (partial GraphQL error) is not answered: its cached
                    // entries are kept and it is retried on the next refresh
                    if (!titles.TryGetValue(imdbId, out var entries))
                    {
                        continue;
                    }

                    foreach (var metadataId in byImdbId[imdbId])
                    {
                        result[metadataId] = entries.Select(e => WithMetadata(e, metadataId)).ToList();
                    }
                }
            }

            return result;
        }

        // Entries per requested IMDb id, or null when the batch failed twice
        private Dictionary<string, List<EditionCatalogEntry>> FetchBatch(List<string> imdbIds)
        {
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                try
                {
                    var content = Post(imdbIds);
                    return Parse(content, imdbIds);
                }
                catch (Exception ex)
                {
                    if (attempt == 1)
                    {
                        _logger.Debug(ex, "IMDb runtimes request for {0} titles failed, retrying once", imdbIds.Count);
                    }
                    else
                    {
                        _logger.Warn(ex, "IMDb runtimes request for {0} titles failed twice, skipping them ({1}...)", imdbIds.Count, imdbIds.First());
                    }
                }
            }

            return null;
        }

        private string Post(List<string> imdbIds)
        {
            lock (_callLock)
            {
                var wait = _lastCall + CallSpacing - DateTime.UtcNow;

                if (wait > TimeSpan.Zero)
                {
                    Thread.Sleep(wait);
                }

                try
                {
                    var request = new HttpRequest(Url);
                    request.Method = System.Net.Http.HttpMethod.Post;
                    request.Headers.ContentType = "application/json";
                    request.Headers.Accept = "application/graphql+json, application/json";
                    request.Headers.Add("Origin", "https://www.imdb.com");
                    request.Headers.Add("Referer", "https://www.imdb.com/");
                    request.Headers.Add("x-imdb-client-name", "imdb-web-next-localized");
                    request.Headers.Add("x-imdb-user-country", "CA");
                    request.Headers.Add("x-imdb-user-language", "en-CA");
                    request.Headers.Add("User-Agent", BrowserUserAgent);
                    request.RequestTimeout = TimeSpan.FromSeconds(30);
                    request.LogResponseContent = false;
                    request.SetContent(BuildBody(imdbIds));

                    var response = _httpClient.Execute(request);

                    if (response.StatusCode != HttpStatusCode.OK)
                    {
                        throw new HttpException(response);
                    }

                    return response.Content;
                }
                finally
                {
                    _lastCall = DateTime.UtcNow;
                }
            }
        }

        public static string BuildBody(IEnumerable<string> imdbIds)
        {
            return JsonSerializer.Serialize(new
            {
                query = Query,
                variables = new { ids = imdbIds.ToArray() }
            });
        }

        // Keyed by the requested id. A title IMDb returns under another id (merged titles) is matched by position, only
        // when the list has one slot per requested id; null slots are left out.
        // Throws when the response carries no titles list (GraphQL error, HTML block page...).
        public static Dictionary<string, List<EditionCatalogEntry>> Parse(string json, IList<string> requestedIds)
        {
            var result = new Dictionary<string, List<EditionCatalogEntry>>(StringComparer.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object ||
                !data.TryGetProperty("titles", out var titles) ||
                titles.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("IMDb response has no data.titles list");
            }

            var requested = new HashSet<string>(requestedIds ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var positional = requestedIds != null && titles.GetArrayLength() == requestedIds.Count;
            var index = -1;

            foreach (var title in titles.EnumerateArray())
            {
                index++;

                if (title.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var id = GetString(title, "id");

                if (id == null || !requested.Contains(id))
                {
                    if (!positional)
                    {
                        continue;
                    }

                    id = requestedIds[index];
                }

                result[id] = BuildEntries(title);
            }

            return result;
        }

        // The first unlabelled runtime is the theatrical reference; later unlabelled ones are ignored. Labelled ones go
        // through EditionLabelNormalizer and keep their raw labels as attributes.
        public static List<EditionCatalogEntry> BuildEntries(JsonElement title)
        {
            var entries = new List<EditionCatalogEntry>();
            var theatricalSeen = false;
            var hasEdges = false;

            if (title.TryGetProperty("runtimes", out var runtimes) &&
                runtimes.ValueKind == JsonValueKind.Object &&
                runtimes.TryGetProperty("edges", out var edges) &&
                edges.ValueKind == JsonValueKind.Array)
            {
                foreach (var edge in edges.EnumerateArray())
                {
                    if (edge.ValueKind != JsonValueKind.Object ||
                        !edge.TryGetProperty("node", out var node) ||
                        node.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var minutes = ToMinutes(node);

                    if (minutes <= 0)
                    {
                        continue;
                    }

                    hasEdges = true;

                    var labels = new List<string>();

                    if (node.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Array)
                    {
                        labels = attributes.EnumerateArray()
                                           .Select(a => a.ValueKind == JsonValueKind.Object ? GetString(a, "text") : null)
                                           .Where(t => t.IsNotNullOrWhiteSpace())
                                           .Select(t => t.Trim())
                                           .ToList();
                    }

                    if (labels.Count == 0)
                    {
                        if (theatricalSeen)
                        {
                            continue;
                        }

                        theatricalSeen = true;
                    }

                    var (name, confidence) = EditionLabelNormalizer.Map(labels.Count == 0 ? null : string.Join(" ", labels));

                    entries.Add(new EditionCatalogEntry
                    {
                        Name = name,
                        RuntimeMinutes = minutes,
                        Source = EditionCatalogSource.Imdb,
                        Confidence = confidence,
                        Attributes = labels
                    });
                }
            }

            // No per-cut list: the main runtime is the theatrical one
            if (!hasEdges && title.TryGetProperty("runtime", out var runtime) && runtime.ValueKind == JsonValueKind.Object)
            {
                var minutes = ToMinutes(runtime);

                if (minutes > 0)
                {
                    entries.Add(new EditionCatalogEntry
                    {
                        Name = EditionLabelNormalizer.TheatricalName,
                        RuntimeMinutes = minutes,
                        Source = EditionCatalogSource.Imdb,
                        Confidence = EditionLabelNormalizer.UnlabelledTheatricalConfidence
                    });
                }
            }

            return entries;
        }

        private static int ToMinutes(JsonElement element)
        {
            if (!element.TryGetProperty("seconds", out var seconds) || seconds.ValueKind != JsonValueKind.Number || !seconds.TryGetDouble(out var value))
            {
                return 0;
            }

            return (int)Math.Round(value / 60.0, MidpointRounding.AwayFromZero);
        }

        private static string GetString(JsonElement element, string property)
        {
            return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        }

        private static EditionCatalogEntry WithMetadata(EditionCatalogEntry entry, int metadataId)
        {
            return new EditionCatalogEntry
            {
                MovieMetadataId = metadataId,
                Name = entry.Name,
                RuntimeMinutes = entry.RuntimeMinutes,
                Source = entry.Source,
                Confidence = entry.Confidence,
                Attributes = entry.Attributes.ToList(),
                Updated = entry.Updated
            };
        }
    }
}
