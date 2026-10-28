// krzw(edition-runtime-catalog): accept/reject matrix of the import-time runtime check (tag vs runtime, PAL, ambiguity, skips)
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.MediaFiles.MovieImport.Specifications;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Specifications
{
    [TestFixture]
    public class EditionRuntimeImportSpecificationFixture : CoreTest<EditionRuntimeImportSpecification>
    {
        private const int MetadataId = 42;

        private Movie _default;
        private Movie _extended;
        private List<EditionCatalogEntry> _catalog;

        [SetUp]
        public void Setup()
        {
            _default = new Movie { Id = 1, MovieMetadataId = MetadataId, Monitored = true, Edition = "", Title = "Fellowship" };
            _extended = new Movie
            {
                Id = 2,
                MovieMetadataId = MetadataId,
                Monitored = true,
                Edition = "Extended",
                EditionAliases = new List<string> { "Extended", "Extended Cut", "Extended Edition" },
                ExpectedRuntimeMinutes = 208,
                Title = "Fellowship"
            };

            _catalog = new List<EditionCatalogEntry>
            {
                Entry("Default", 178, EditionCatalogSource.Tmdb, 30),
                Entry("Theatrical", 178, EditionCatalogSource.Imdb, 80),
                Entry("Extended", 208, EditionCatalogSource.Imdb, 70)
            };

            Mocker.SetConstant<IEditionRuntimeImportEvaluator>(Mocker.Resolve<EditionRuntimeImportEvaluator>());

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRuntimeToleranceMinutes).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRehomeOnImport).Returns(true);

            Mocker.GetMock<IEditionCatalogService>()
                  .Setup(s => s.GetForMetadata(MetadataId))
                  .Returns(() => _catalog);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(It.IsAny<Movie>()))
                  .Returns<Movie>(m => new[] { _default, _extended }.Where(r => r.Id != m.Id).ToList());
        }

        private static EditionCatalogEntry Entry(string name, int minutes, EditionCatalogSource source, int confidence)
        {
            return new EditionCatalogEntry
            {
                MovieMetadataId = MetadataId,
                Name = name,
                RuntimeMinutes = minutes,
                Source = source,
                Confidence = confidence,
                Updated = DateTime.UtcNow
            };
        }

        private static LocalMovie GivenFile(Movie movie, string edition, double? minutes)
        {
            return new LocalMovie
            {
                Path = @"C:\Test\Downloads\Movie.Title.2001.1080p.BluRay.x264-GROUP\Movie.Title.2001.1080p.BluRay.x264-GROUP.mkv".AsOsAgnostic(),
                Movie = movie,
                Edition = edition,
                MediaInfo = minutes.HasValue ? new MediaInfoModel { RunTime = TimeSpan.FromMinutes(minutes.Value) } : null
            };
        }

        [TestCase("Extended", 208)]
        [TestCase("Extended Edition", 209.5)]
        [TestCase("Extended", 199.7)]
        [TestCase("Extended", 140)]
        [TestCase("Theatrical", 178)]
        [TestCase("Theatrical", 170.9)]
        public void should_accept_a_tagged_file_whose_runtime_does_not_name_another_cut(string tag, double minutes)
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, tag, minutes), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_a_tagged_extended_file_with_the_theatrical_runtime()
        {
            var result = Subject.IsSatisfiedBy(GivenFile(_extended, "Extended", 178.2), null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(ImportRejectionReason.EditionMismatch);
            result.Message.Should().Be("Tagged Extended (208 min) but runtime 178 min matches Theatrical (178 min)");
        }

        [Test]
        public void should_reject_a_tagged_extended_file_with_the_pal_theatrical_runtime()
        {
            // 178 min film at 25 fps
            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended", 170.9), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_tagged_theatrical_file_with_the_extended_runtime()
        {
            var result = Subject.IsSatisfiedBy(GivenFile(_default, "Theatrical", 208), null);

            result.Accepted.Should().BeFalse();
            result.Message.Should().Be("Tagged Theatrical (178 min) but runtime 208 min matches Extended (208 min)");
        }

        [Test]
        public void should_accept_a_tagged_file_when_the_runtime_matches_only_tmdb()
        {
            // Fast Five "Extended" 131 = TMDb: TMDb may be the extended cut, or the tag lies
            _catalog = new List<EditionCatalogEntry> { Entry("Default", 131, EditionCatalogSource.Tmdb, 30) };
            _extended.ExpectedRuntimeMinutes = null;

            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended", 131), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_the_runtime_is_ambiguous()
        {
            _catalog.Add(Entry("Director's Cut", 210, EditionCatalogSource.Imdb, 70));

            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended", 209), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(GivenFile(_default, null, 209), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_when_media_info_is_missing()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, "Extended", null), null).Accepted.Should().BeTrue();
            Subject.IsSatisfiedBy(GivenFile(_default, null, null), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_skip_existing_files()
        {
            var file = GivenFile(_extended, "Extended", 178);
            file.ExistingFile = true;

            Subject.IsSatisfiedBy(file, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_skip_rows_the_user_picked()
        {
            var file = GivenFile(_extended, "Extended", 178);
            file.EditionOverride = true;

            Subject.IsSatisfiedBy(file, null).Accepted.Should().BeTrue();
            Mocker.GetMock<IEditionCatalogService>().Verify(s => s.GetForMetadata(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public void should_accept_a_file_the_runtime_rehome_moved()
        {
            var file = GivenFile(_extended, null, 208);
            file.EditionRuntimeRehomed = true;

            Subject.IsSatisfiedBy(file, null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_reject_an_untagged_default_row_file_whose_runtime_names_a_sibling_row()
        {
            var result = Subject.IsSatisfiedBy(GivenFile(_default, null, 208.4), null);

            result.Accepted.Should().BeFalse();
            result.Reason.Should().Be(ImportRejectionReason.EditionMismatch);
            result.Message.Should().Be("Runtime 208 min matches the Extended edition of this movie");
        }

        [Test]
        public void should_treat_a_release_variant_tag_as_untagged()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, "IMAX", 208), null).Message.Should().Be("Runtime 208 min matches the Extended edition of this movie");
        }

        [Test]
        public void should_accept_a_default_row_file_with_a_tag_the_catalog_does_not_know()
        {
            Subject.IsSatisfiedBy(GivenFile(_default, "Fan Edit", 207), null).Accepted.Should().BeTrue();
        }

        [TestCase(178)]
        [TestCase(150)]
        [TestCase(260)]
        public void should_accept_an_untagged_default_row_file_whose_runtime_names_no_sibling_row(double minutes)
        {
            Subject.IsSatisfiedBy(GivenFile(_default, null, minutes), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_an_untagged_default_row_file_when_the_sibling_row_has_no_expected_runtime()
        {
            _extended.ExpectedRuntimeMinutes = null;

            Subject.IsSatisfiedBy(GivenFile(_default, null, 208), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_an_untagged_file_on_an_edition_row()
        {
            Subject.IsSatisfiedBy(GivenFile(_extended, null, 178), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_title_without_siblings_or_catalog()
        {
            _catalog = new List<EditionCatalogEntry>();
            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(It.IsAny<Movie>()))
                  .Returns(new List<Movie>());

            Subject.IsSatisfiedBy(GivenFile(_default, "Extended", 178), null).Accepted.Should().BeTrue();
        }
    }
}
