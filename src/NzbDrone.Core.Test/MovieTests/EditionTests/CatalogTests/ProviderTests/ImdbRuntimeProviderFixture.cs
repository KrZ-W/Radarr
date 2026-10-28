// krzw(edition-runtime-catalog): IMDb runtime provider (parsing, batching, retry and skip)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests.ProviderTests
{
    [TestFixture]
    public class ImdbRuntimeProviderFixture : CoreTest<ImdbRuntimeProvider>
    {
        private const string EmptyResponse = "{\"data\":{\"titles\":[]}}";

        private string _sample;
        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _sample = ReadAllText("Files/Editions/imdb-runtimes-sample.json");
            _requests = new List<HttpRequest>();

            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(true);
        }

        private void GivenResponses(params Func<HttpRequest, HttpResponse>[] responses)
        {
            var queue = new Queue<Func<HttpRequest, HttpResponse>>(responses);

            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Execute(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest r) =>
                  {
                      _requests.Add(r);
                      var next = queue.Count > 1 ? queue.Dequeue() : queue.Peek();
                      return next(r);
                  });

            Subject.CallSpacing = TimeSpan.Zero;
        }

        private static Func<HttpRequest, HttpResponse> Ok(string content)
        {
            return r => new HttpResponse(r, new HttpHeader(), content, HttpStatusCode.OK);
        }

        // Every requested title, found, with no runtimes
        private static Func<HttpRequest, HttpResponse> Echo()
        {
            return r =>
            {
                using var body = System.Text.Json.JsonDocument.Parse(Encoding.UTF8.GetString(r.ContentData));
                var titles = body.RootElement.GetProperty("variables").GetProperty("ids").EnumerateArray()
                                 .Select(id => new { id = id.GetString(), runtimes = new { edges = Array.Empty<object>() } })
                                 .ToArray();

                return new HttpResponse(r, new HttpHeader(), System.Text.Json.JsonSerializer.Serialize(new { data = new { titles } }), HttpStatusCode.OK);
            };
        }

        private static Func<HttpRequest, HttpResponse> Status(HttpStatusCode status)
        {
            return r => new HttpResponse(r, new HttpHeader(), "blocked", status);
        }

        private static Func<HttpRequest, HttpResponse> Throws()
        {
            return r => throw new WebException("connection reset");
        }

        private static MovieMetadata Title(int id, string imdbId)
        {
            return new MovieMetadata { Id = id, ImdbId = imdbId, Title = imdbId, Runtime = 120 };
        }

        private static List<MovieMetadata> Titles(int count)
        {
            return Enumerable.Range(1, count).Select(i => Title(i, $"tt{1000000 + i}")).ToList();
        }

        private Dictionary<string, List<EditionCatalogEntry>> ParseSample()
        {
            return ImdbRuntimeProvider.Parse(_sample, new[] { "tt0114898", "tt0120737", "tt0084726", "tt0320661", "tt0070047", "tt0083658" });
        }

        [Test]
        public void should_parse_waterworld_theatrical_and_directors_cut()
        {
            var entries = ParseSample()["tt0114898"];

            entries.Should().HaveCount(2);
            entries[0].Name.Should().Be("Theatrical");
            entries[0].RuntimeMinutes.Should().Be(135);
            entries[0].Confidence.Should().Be(80);
            entries[0].Source.Should().Be(EditionCatalogSource.Imdb);
            entries[1].Name.Should().Be("Director's Cut");
            entries[1].RuntimeMinutes.Should().Be(176);
            entries[1].Confidence.Should().Be(EditionLabelNormalizer.CutConfidence);
            entries[1].Attributes.Should().Equal("director's cut");
        }

        [Test]
        public void should_parse_fellowship_with_both_extended_releases_and_raw_labels()
        {
            var entries = ParseSample()["tt0120737"];

            entries.Select(e => e.RuntimeMinutes).Should().Equal(178, 208, 228, 171);

            entries[0].Name.Should().Be("Theatrical");

            entries[1].Name.Should().Be("Extended");
            entries[1].Attributes.Should().Equal("Special DVD Extended Edition");
            entries[1].Confidence.Should().Be(EditionLabelNormalizer.EmbellishedCutConfidence);

            entries[2].Name.Should().Be("Extended");
            entries[2].Attributes.Should().Equal("Blu Ray Extended Edition");

            entries[3].Name.Should().Be("DVD Widescreen Edition");
            entries[3].Confidence.Should().Be(EditionLabelNormalizer.TransferConfidence);
        }

        [Test]
        public void should_parse_kingdom_of_heaven_cuts()
        {
            var entries = ParseSample()["tt0320661"];

            entries.Select(e => e.RuntimeMinutes).Should().Equal(144, 190, 194);
            entries.Select(e => e.Name).Should().Equal("Theatrical", "Director's Cut", "Director's Cut");
            entries[1].Confidence.Should().Be(EditionLabelNormalizer.CutConfidence);
            entries[2].Confidence.Should().Be(EditionLabelNormalizer.EmbellishedCutConfidence);
            entries[2].Attributes.Should().Equal("director's cut roadshow");
        }

        [Test]
        public void should_parse_every_requested_title_of_the_sample()
        {
            ParseSample().Keys.Should().BeEquivalentTo("tt0114898", "tt0120737", "tt0084726", "tt0320661", "tt0070047", "tt0083658");
        }

        [Test]
        public void should_keep_only_the_first_unlabelled_runtime_as_theatrical()
        {
            var json = "{\"data\":{\"titles\":[{\"id\":\"tt1\",\"runtime\":{\"seconds\":6000},\"runtimes\":{\"edges\":[" +
                       "{\"node\":{\"seconds\":6000,\"attributes\":[]}},{\"node\":{\"seconds\":5400,\"attributes\":[]}}]}}]}}";

            var entries = ImdbRuntimeProvider.Parse(json, new[] { "tt1" })["tt1"];

            entries.Should().ContainSingle();
            entries[0].RuntimeMinutes.Should().Be(100);
        }

        [Test]
        public void should_use_main_runtime_when_no_runtime_list()
        {
            var json = "{\"data\":{\"titles\":[{\"id\":\"tt1\",\"runtime\":{\"seconds\":5430},\"runtimes\":{\"edges\":[]}}]}}";

            var entries = ImdbRuntimeProvider.Parse(json, new[] { "tt1" })["tt1"];

            entries.Should().ContainSingle();
            entries[0].Name.Should().Be("Theatrical");
            entries[0].RuntimeMinutes.Should().Be(91);
        }

        [Test]
        public void should_match_a_merged_title_by_position_and_skip_nulls()
        {
            var json = "{\"data\":{\"titles\":[null,{\"id\":\"tt9\",\"runtime\":{\"seconds\":6000},\"runtimes\":null}]}}";

            var result = ImdbRuntimeProvider.Parse(json, new[] { "tt1", "tt2" });

            result.Keys.Should().BeEquivalentTo("tt2");
            result["tt2"].Single().RuntimeMinutes.Should().Be(100);
        }

        [Test]
        public void should_not_match_by_position_when_imdb_left_a_title_out()
        {
            // tt1 dropped, tt2 returned under its merged id at index 0: position says nothing
            var json = "{\"data\":{\"titles\":[{\"id\":\"tt9\",\"runtime\":{\"seconds\":6000},\"runtimes\":null}]}}";

            var result = ImdbRuntimeProvider.Parse(json, new[] { "tt1", "tt2" });

            result.Should().BeEmpty();
        }

        [Test]
        public void should_leave_out_a_title_returned_as_null_in_a_successful_batch()
        {
            GivenResponses(Ok("{\"data\":{\"titles\":[null,{\"id\":\"tt1000002\",\"runtime\":{\"seconds\":6000},\"runtimes\":null}]},\"errors\":[{\"message\":\"partial\"}]}"));

            var result = Subject.Fetch(Titles(2));

            // Title 1 is not answered: its cached IMDb entries stay
            result.Keys.Should().BeEquivalentTo(new[] { 2 });
            result[2].Single().RuntimeMinutes.Should().Be(100);
        }

        [Test]
        public void should_not_call_imdb_again_in_the_next_chunk_after_3_recent_failed_batches()
        {
            GivenResponses(Ok(_sample));

            var status = Mocker.GetMock<IImdbRuntimeProviderStatus>();
            status.SetupGet(s => s.ConsecutiveFailedBatches).Returns(3);
            status.SetupGet(s => s.LastFailure).Returns(DateTime.UtcNow.AddMinutes(-5));

            var result = Subject.Fetch(Titles(10));

            _requests.Should().BeEmpty();
            result.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_try_imdb_again_once_the_backoff_has_passed()
        {
            GivenResponses(Echo());

            var status = Mocker.GetMock<IImdbRuntimeProviderStatus>();
            status.SetupGet(s => s.ConsecutiveFailedBatches).Returns(3);
            status.SetupGet(s => s.LastFailure).Returns(DateTime.UtcNow - ImdbRuntimeProvider.BlockedBackoff - TimeSpan.FromMinutes(1));

            var result = Subject.Fetch(Titles(10));

            _requests.Should().HaveCount(1);
            result.Should().HaveCount(10);
        }

        [Test]
        public void should_throw_on_response_without_titles()
        {
            Assert.Throws<FormatException>(() => ImdbRuntimeProvider.Parse("{\"errors\":[{\"message\":\"nope\"}]}", new[] { "tt1" }));
        }

        [Test]
        public void should_send_the_imdb_site_headers_and_query()
        {
            GivenResponses(Ok(_sample));

            var result = Subject.Fetch(new List<MovieMetadata> { Title(11, "tt0114898"), Title(12, "tt0120737") });

            result[11].Should().HaveCount(2);
            result[11].Should().OnlyContain(e => e.MovieMetadataId == 11);
            result[12].Should().HaveCount(4);

            var request = _requests.Single();
            request.Method.Should().Be(System.Net.Http.HttpMethod.Post);
            request.Url.FullUri.Should().Be("https://caching.graphql.imdb.com/");
            request.Headers.ContentType.Should().Be("application/json");
            request.Headers.Accept.Should().Be("application/graphql+json, application/json");
            request.Headers["Origin"].Should().Be("https://www.imdb.com");
            request.Headers["Referer"].Should().Be("https://www.imdb.com/");
            request.Headers["x-imdb-client-name"].Should().Be("imdb-web-next-localized");
            request.Headers["x-imdb-user-country"].Should().Be("CA");
            request.Headers["x-imdb-user-language"].Should().Be("en-CA");
            request.Headers["User-Agent"].Should().Contain("Chrome/");

            var body = Encoding.UTF8.GetString(request.ContentData);
            body.Should().Contain("runtimes(first:20)");
            body.Should().Contain("\"ids\":[\"tt0114898\",\"tt0120737\"]");
        }

        [Test]
        public void should_batch_120_ids_in_3_calls_of_at_most_50()
        {
            GivenResponses(Echo());

            var result = Subject.Fetch(Titles(120));

            _requests.Should().HaveCount(3);
            _requests.Select(r => Encoding.UTF8.GetString(r.ContentData).Split("\"tt").Length - 1).Should().Equal(50, 50, 20);

            // Answered titles with nothing on IMDb are present with no entries (their old entries get replaced)
            result.Should().HaveCount(120);
            result.Values.Should().OnlyContain(l => l.Count == 0);

            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(true), Times.Exactly(3));
        }

        [Test]
        public void should_answer_titles_without_imdb_id_without_calling_imdb()
        {
            GivenResponses(Ok(EmptyResponse));

            var result = Subject.Fetch(new List<MovieMetadata> { Title(1, null), Title(2, "nm123") });

            result.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
            _requests.Should().BeEmpty();
        }

        [Test]
        public void should_retry_a_failed_batch_once()
        {
            GivenResponses(Throws(), Ok(_sample));

            var result = Subject.Fetch(new List<MovieMetadata> { Title(11, "tt0114898") });

            _requests.Should().HaveCount(2);
            result[11].Should().HaveCount(2);
            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(true), Times.Once());
            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(false), Times.Never());
        }

        [Test]
        public void should_leave_out_titles_of_a_batch_that_failed_twice()
        {
            GivenResponses(Status(HttpStatusCode.Forbidden), Status(HttpStatusCode.Forbidden), Echo());

            var result = Subject.Fetch(Titles(60));

            _requests.Should().HaveCount(3);
            result.Keys.Should().BeEquivalentTo(Enumerable.Range(51, 10));
            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(false), Times.Once());
            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(true), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_treat_an_unparsable_answer_as_a_failure()
        {
            GivenResponses(Ok("<html>blocked</html>"));

            var result = Subject.Fetch(Titles(1));

            _requests.Should().HaveCount(2);
            result.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_stop_after_3_failed_batches_in_a_row()
        {
            GivenResponses(Throws());

            var result = Subject.Fetch(Titles(250));

            // 3 batches x (call + retry); batches 4 and 5 are not attempted
            _requests.Should().HaveCount(6);
            result.Should().BeEmpty();
            Mocker.GetMock<IImdbRuntimeProviderStatus>().Verify(s => s.RecordBatch(false), Times.Exactly(3));

            ExceptionVerification.ExpectedWarns(4);
        }

        [Test]
        public void should_follow_the_imdb_setting()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.EditionCatalogImdbEnabled).Returns(false);

            Subject.Enabled.Should().BeFalse();
            Subject.Source.Should().Be(EditionCatalogSource.Imdb);
        }

        [Test]
        [Explicit]
        [Category("IntegrationTest")]
        public void smoke_real_imdb_call_with_two_ids()
        {
            UseRealHttp();

            Subject.CallSpacing = TimeSpan.Zero;

            var result = Subject.Fetch(new List<MovieMetadata> { Title(1, "tt0114898"), Title(2, "tt0120737") });

            TestContext.Progress.WriteLine(string.Join(" | ", result.SelectMany(r => r.Value).Select(e => $"{e.MovieMetadataId}: {e} [{string.Join(", ", e.Attributes)}]")));

            result.Keys.Should().BeEquivalentTo(new[] { 1, 2 });
            result[1].Should().Contain(e => e.Name == "Theatrical");
            result[2].Should().Contain(e => e.Name == "Extended");
        }
    }
}
