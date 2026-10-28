// krzw(edition-runtime-catalog): Wikidata runtime provider (SPARQL parsing, labelled durations only, failure handling)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Movies.Editions.Catalog.Providers;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MovieTests.EditionTests.CatalogTests.ProviderTests
{
    [TestFixture]
    public class WikidataRuntimeProviderFixture : CoreTest<WikidataRuntimeProvider>
    {
        private const string Sample = @"{
  ""head"": { ""vars"": [ ""imdb"", ""dur"", ""qualLabel"" ] },
  ""results"": { ""bindings"": [
    { ""imdb"": { ""type"": ""literal"", ""value"": ""tt0120737"" },
      ""dur"": { ""datatype"": ""http://www.w3.org/2001/XMLSchema#decimal"", ""type"": ""literal"", ""value"": ""178"" } },
    { ""imdb"": { ""type"": ""literal"", ""value"": ""tt0120737"" },
      ""dur"": { ""datatype"": ""http://www.w3.org/2001/XMLSchema#decimal"", ""type"": ""literal"", ""value"": ""228"" },
      ""qualLabel"": { ""xml:lang"": ""en"", ""type"": ""literal"", ""value"": ""extended cut"" } },
    { ""imdb"": { ""type"": ""literal"", ""value"": ""tt0120737"" },
      ""dur"": { ""datatype"": ""http://www.w3.org/2001/XMLSchema#decimal"", ""type"": ""literal"", ""value"": ""178.0"" },
      ""qualLabel"": { ""xml:lang"": ""en"", ""type"": ""literal"", ""value"": ""theatrical version"" } },
    { ""imdb"": { ""type"": ""literal"", ""value"": ""tt0083658"" },
      ""dur"": { ""datatype"": ""http://www.w3.org/2001/XMLSchema#decimal"", ""type"": ""literal"", ""value"": ""117.5"" },
      ""qualLabel"": { ""xml:lang"": ""en"", ""type"": ""literal"", ""value"": ""final cut"" } },
    { ""imdb"": { ""type"": ""literal"", ""value"": ""tt0083658"" },
      ""dur"": { ""datatype"": ""http://www.w3.org/2001/XMLSchema#decimal"", ""type"": ""literal"", ""value"": ""113"" },
      ""qualLabel"": { ""type"": ""literal"", ""value"": ""Q123456"" } }
  ] }
}";

        private List<HttpRequest> _requests;

        [SetUp]
        public void Setup()
        {
            _requests = new List<HttpRequest>();
        }

        private void GivenResponse(Func<HttpRequest, HttpResponse> response)
        {
            Mocker.GetMock<IHttpClient>()
                  .Setup(c => c.Get(It.IsAny<HttpRequest>()))
                  .Returns((HttpRequest r) =>
                  {
                      _requests.Add(r);
                      return response(r);
                  });
        }

        private static MovieMetadata Title(int id, string imdbId)
        {
            return new MovieMetadata { Id = id, ImdbId = imdbId, Title = imdbId };
        }

        [Test]
        public void should_parse_labelled_durations_only()
        {
            var result = WikidataRuntimeProvider.Parse(Sample);

            result["tt0120737"].Select(e => (e.Name, e.RuntimeMinutes)).Should().Equal(("Extended", 228), ("Theatrical", 178));
            result["tt0120737"].Should().OnlyContain(e => e.Source == EditionCatalogSource.Wikidata);
            result["tt0120737"][0].Attributes.Should().Equal("extended cut");
            result["tt0120737"][1].Confidence.Should().Be(EditionLabelNormalizer.UnlabelledTheatricalConfidence);

            // 117.5 rounds up; a qualifier without an English label (Q-id) is ignored
            result["tt0083658"].Should().ContainSingle();
            result["tt0083658"][0].Name.Should().Be("Final Cut");
            result["tt0083658"][0].RuntimeMinutes.Should().Be(118);
        }

        [Test]
        public void should_convert_hours_and_seconds_and_skip_other_units()
        {
            const string json = @"{ ""results"": { ""bindings"": [
    { ""imdb"": { ""value"": ""tt1"" }, ""dur"": { ""value"": ""2.2"" }, ""unit"": { ""value"": ""http://www.wikidata.org/entity/Q25235"" },
      ""qualLabel"": { ""value"": ""director's cut"" } },
    { ""imdb"": { ""value"": ""tt1"" }, ""dur"": { ""value"": ""7920"" }, ""unit"": { ""value"": ""http://www.wikidata.org/entity/Q11574"" },
      ""qualLabel"": { ""value"": ""extended cut"" } },
    { ""imdb"": { ""value"": ""tt1"" }, ""dur"": { ""value"": ""140"" }, ""unit"": { ""value"": ""http://www.wikidata.org/entity/Q7727"" },
      ""qualLabel"": { ""value"": ""theatrical version"" } },
    { ""imdb"": { ""value"": ""tt1"" }, ""dur"": { ""value"": ""3"" }, ""unit"": { ""value"": ""http://www.wikidata.org/entity/Q577"" },
      ""qualLabel"": { ""value"": ""final cut"" } }
  ] } }";

            var result = WikidataRuntimeProvider.Parse(json);

            result["tt1"].Select(e => (e.Name, e.RuntimeMinutes)).Should().BeEquivalentTo(new[] { ("Director's Cut", 132), ("Extended", 132), ("Theatrical", 140) });
        }

        [Test]
        public void should_build_the_contract_query()
        {
            var query = WikidataRuntimeProvider.BuildQuery(new[] { "tt1", "tt2" });

            query.Should().Contain("VALUES ?imdb { \"tt1\" \"tt2\" }");
            query.Should().Contain("?film wdt:P345 ?imdb . ?film p:P2047 ?st . ?st ps:P2047 ?dur . OPTIONAL { ?st pq:P518 ?qual . }");
            query.Should().Contain("wikibase:language \"en\"");
            query.Should().Contain("?st psv:P2047/wikibase:quantityUnit ?unit");
        }

        [Test]
        public void should_fetch_with_contact_user_agent_and_answer_every_title()
        {
            GivenResponse(r => new HttpResponse(r, new HttpHeader(), Sample, HttpStatusCode.OK));

            var result = Subject.Fetch(new List<MovieMetadata> { Title(1, "tt0120737"), Title(2, "tt0000001"), Title(3, null) });

            result.Keys.Should().BeEquivalentTo(new[] { 1, 2, 3 });
            result[1].Should().HaveCount(2).And.OnlyContain(e => e.MovieMetadataId == 1);
            result[2].Should().BeEmpty();

            var request = _requests.Single();
            request.Url.FullUri.Should().StartWith("https://query.wikidata.org/sparql?format=json&query=");
            request.Headers["User-Agent"].Should().StartWith("KrZ-W-Radarr/").And.EndWith("(https://github.com/KrZ-W/Radarr)");
        }

        [Test]
        public void should_batch_250_ids_per_query()
        {
            GivenResponse(r => new HttpResponse(r, new HttpHeader(), "{\"results\":{\"bindings\":[]}}", HttpStatusCode.OK));

            Subject.Fetch(Enumerable.Range(1, 260).Select(i => Title(i, $"tt{1000000 + i}")).ToList());

            _requests.Should().HaveCount(2);
        }

        [Test]
        public void should_retry_once_then_leave_titles_out()
        {
            GivenResponse(r => new HttpResponse(r, new HttpHeader(), "busy", HttpStatusCode.TooManyRequests));

            var result = Subject.Fetch(new List<MovieMetadata> { Title(1, "tt0120737") });

            _requests.Should().HaveCount(2);
            result.Should().BeEmpty();

            ExceptionVerification.ExpectedWarns(1);
        }
    }
}
