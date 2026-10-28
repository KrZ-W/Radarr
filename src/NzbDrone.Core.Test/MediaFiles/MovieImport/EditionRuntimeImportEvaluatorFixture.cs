// krzw(edition-runtime-catalog): runtime re-home target selection (setting on/off, unique/ambiguous/no match)
using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.MediaFiles.MediaInfo;
using NzbDrone.Core.MediaFiles.MovieImport;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Movies.Editions.Catalog;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport
{
    [TestFixture]
    public class EditionRuntimeImportEvaluatorFixture : CoreTest<EditionRuntimeImportEvaluator>
    {
        private const int MetadataId = 7;

        private Movie _default;
        private Movie _extended;
        private Movie _directorsCut;
        private List<Movie> _rows;
        private List<EditionCatalogEntry> _catalog;

        [SetUp]
        public void Setup()
        {
            _default = new Movie { Id = 1, MovieMetadataId = MetadataId, Monitored = true, Edition = "" };
            _extended = new Movie { Id = 2, MovieMetadataId = MetadataId, Monitored = true, Edition = "Extended", ExpectedRuntimeMinutes = 208 };
            _directorsCut = new Movie { Id = 3, MovieMetadataId = MetadataId, Monitored = true, Edition = "Director's Cut" };
            _rows = new List<Movie> { _default, _extended, _directorsCut };

            _catalog = new List<EditionCatalogEntry>
            {
                new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Default", RuntimeMinutes = 178, Source = EditionCatalogSource.Tmdb, Confidence = 30 }
            };

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRuntimeToleranceMinutes).Returns(3);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRehomeOnImport).Returns(true);

            Mocker.GetMock<IEditionCatalogService>()
                  .Setup(s => s.GetForMetadata(MetadataId))
                  .Returns(() => _catalog);

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetEditionSiblings(It.IsAny<Movie>()))
                  .Returns<Movie>(m => _rows.Where(r => r.Id != m.Id).ToList());

            Mocker.GetMock<IMovieService>()
                  .Setup(s => s.GetDefaultEdition(MetadataId))
                  .Returns(() => _default);
        }

        private static LocalMovie GivenFile(Movie movie, double minutes, string edition = null)
        {
            return new LocalMovie
            {
                Path = "/downloads/Movie.2001.mkv",
                Movie = movie,
                Edition = edition,
                MediaInfo = new MediaInfoModel { RunTime = TimeSpan.FromMinutes(minutes) }
            };
        }

        [TestCase(208)]
        [TestCase(210.5)]
        [TestCase(199.7)]
        public void should_rehome_an_untagged_file_whose_runtime_matches_one_sibling_row(double minutes)
        {
            Subject.FindRehomeTarget(GivenFile(_default, minutes)).Should().BeSameAs(_extended);
        }

        [Test]
        public void should_not_rehome_when_the_setting_is_off()
        {
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRehomeOnImport).Returns(false);

            Subject.FindRehomeTarget(GivenFile(_default, 208)).Should().BeNull();
            Subject.Evaluate(GivenFile(_default, 208)).RuntimeSibling.Should().BeSameAs(_extended);
        }

        [Test]
        public void should_not_rehome_to_an_unmonitored_row()
        {
            _extended.Monitored = false;

            Subject.FindRehomeTarget(GivenFile(_default, 208)).Should().BeNull();
            Subject.Evaluate(GivenFile(_default, 208)).RuntimeSibling.Should().BeSameAs(_extended);
        }

        [Test]
        public void should_not_rehome_when_two_sibling_rows_match()
        {
            _directorsCut.ExpectedRuntimeMinutes = 210;

            Subject.FindRehomeTarget(GivenFile(_default, 209)).Should().BeNull();
        }

        [Test]
        public void should_rehome_by_the_rows_expected_runtime_even_when_the_catalog_names_another_cut_at_that_runtime()
        {
            _catalog.Add(new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Unrated", RuntimeMinutes = 207, Source = EditionCatalogSource.Imdb, Confidence = 70 });

            Subject.FindRehomeTarget(GivenFile(_default, 208)).Should().BeSameAs(_extended);
        }

        // Waterworld: default row at 135 (expected, or the catalog's Theatrical), a "Ulysses Cut" row expecting 176,
        // and IMDb listing the long cut as "Director's Cut" 176
        private Movie GivenWaterworld(bool defaultExpected)
        {
            var ulysses = new Movie { Id = 4, MovieMetadataId = MetadataId, Monitored = true, Edition = "Ulysses Cut", ExpectedRuntimeMinutes = 176 };

            _default.ExpectedRuntimeMinutes = defaultExpected ? 135 : null;
            _rows = new List<Movie> { _default, ulysses };
            _catalog = new List<EditionCatalogEntry>
            {
                new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Default", RuntimeMinutes = 135, Source = EditionCatalogSource.Tmdb, Confidence = 30 },
                new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Theatrical", RuntimeMinutes = 135, Source = EditionCatalogSource.Imdb, Confidence = 80 },
                new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Director's Cut", RuntimeMinutes = 176, Source = EditionCatalogSource.Imdb, Confidence = 70 }
            };

            return ulysses;
        }

        [TestCase(true, 176)]
        [TestCase(false, 176)]
        [TestCase(true, 169)]
        public void should_rehome_to_the_row_expecting_the_runtime_whatever_the_catalog_calls_that_cut(bool defaultExpected, double minutes)
        {
            // 169 min = 176 at 25 fps
            var ulysses = GivenWaterworld(defaultExpected);

            Subject.FindRehomeTarget(GivenFile(_default, minutes)).Should().BeSameAs(ulysses);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void should_keep_a_file_with_the_default_rows_runtime_on_the_default_row(bool defaultExpected)
        {
            GivenWaterworld(defaultExpected);

            Subject.FindRehomeTarget(GivenFile(_default, 135)).Should().BeNull();
            Subject.Evaluate(GivenFile(_default, 135)).RuntimeSibling.Should().BeNull();
        }

        [Test]
        public void should_report_the_sibling_without_rehoming_when_the_setting_is_off()
        {
            var ulysses = GivenWaterworld(true);
            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRehomeOnImport).Returns(false);

            Subject.FindRehomeTarget(GivenFile(_default, 176)).Should().BeNull();
            Subject.Evaluate(GivenFile(_default, 176)).RuntimeSibling.Should().BeSameAs(ulysses);
        }

        [Test]
        public void should_neither_rehome_nor_report_when_two_sibling_rows_expect_the_runtime()
        {
            GivenWaterworld(true);
            _rows.Add(new Movie { Id = 5, MovieMetadataId = MetadataId, Monitored = true, Edition = "Director's Cut", ExpectedRuntimeMinutes = 176 });

            Subject.FindRehomeTarget(GivenFile(_default, 176)).Should().BeNull();
            Subject.Evaluate(GivenFile(_default, 176)).RuntimeSibling.Should().BeNull();
        }

        [Test]
        public void should_neither_rehome_nor_report_when_a_row_without_expected_runtime_matches_by_catalog_name_too()
        {
            GivenWaterworld(true);
            _rows.Add(new Movie { Id = 5, MovieMetadataId = MetadataId, Monitored = true, Edition = "Director's Cut" });

            Subject.Evaluate(GivenFile(_default, 176)).RuntimeSibling.Should().BeNull();
        }

        [Test]
        public void should_rehome_to_a_row_without_expected_runtime_through_the_catalog_entry_of_its_name()
        {
            _catalog.Add(new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Director's Cut", RuntimeMinutes = 190, Source = EditionCatalogSource.Imdb, Confidence = 70 });

            Subject.FindRehomeTarget(GivenFile(_default, 191)).Should().BeSameAs(_directorsCut);
        }

        [Test]
        public void should_rehome_when_the_catalog_spells_the_same_cut_differently()
        {
            _catalog.Add(new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Extended Edition", RuntimeMinutes = 213, Source = EditionCatalogSource.Imdb, Confidence = 60 });

            Subject.FindRehomeTarget(GivenFile(_default, 210.5)).Should().BeSameAs(_extended);
        }

        [TestCase(178)]
        [TestCase(150)]
        [TestCase(240)]
        public void should_not_rehome_without_a_sibling_match(double minutes)
        {
            Subject.FindRehomeTarget(GivenFile(_default, minutes)).Should().BeNull();
        }

        [Test]
        public void should_not_rehome_when_the_default_row_expects_that_runtime()
        {
            _default.ExpectedRuntimeMinutes = 207;

            Subject.FindRehomeTarget(GivenFile(_default, 208)).Should().BeNull();
        }

        [Test]
        public void should_not_rehome_a_tagged_file()
        {
            Subject.FindRehomeTarget(GivenFile(_default, 208, "Director's Cut")).Should().BeNull();
        }

        [Test]
        public void should_not_rehome_a_file_already_on_an_edition_row()
        {
            Subject.FindRehomeTarget(GivenFile(_directorsCut, 208)).Should().BeNull();
        }

        [Test]
        public void should_not_rehome_a_row_the_user_picked_or_an_existing_file()
        {
            var picked = GivenFile(_default, 208);
            picked.EditionOverride = true;

            var existing = GivenFile(_default, 208);
            existing.ExistingFile = true;

            Subject.FindRehomeTarget(picked).Should().BeNull();
            Subject.FindRehomeTarget(existing).Should().BeNull();
        }

        [Test]
        public void should_not_rehome_without_media_info()
        {
            var file = GivenFile(_default, 208);
            file.MediaInfo = null;

            Subject.FindRehomeTarget(file).Should().BeNull();
        }

        [Test]
        public void should_confirm_the_row_an_untagged_file_was_rehomed_to()
        {
            var file = GivenFile(_extended, 208);

            Subject.IsRehomeTarget(file).Should().BeTrue();
            file.Movie.Should().BeSameAs(_extended);
        }

        [Test]
        public void should_not_confirm_a_row_the_runtime_does_not_take_the_file_to()
        {
            Subject.IsRehomeTarget(GivenFile(_extended, 178)).Should().BeFalse();
            Subject.IsRehomeTarget(GivenFile(_directorsCut, 208)).Should().BeFalse();
        }

        [Test]
        public void should_not_confirm_a_tagged_file_a_default_row_or_with_the_setting_off()
        {
            Subject.IsRehomeTarget(GivenFile(_extended, 208, "Director's Cut")).Should().BeFalse();
            Subject.IsRehomeTarget(GivenFile(_default, 208)).Should().BeFalse();

            Mocker.GetMock<IConfigService>().SetupGet(s => s.EditionRehomeOnImport).Returns(false);

            Subject.IsRehomeTarget(GivenFile(_extended, 208)).Should().BeFalse();
        }

        [Test]
        public void should_query_siblings_and_catalog_once_for_the_rehome_step_and_the_import_spec()
        {
            var file = GivenFile(_default, 178);

            Subject.FindRehomeTarget(file).Should().BeNull();
            Subject.Evaluate(file).Should().NotBeNull();
            Subject.Evaluate(file).RuntimeSibling.Should().BeNull();

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<Movie>()), Times.Once());
            Mocker.GetMock<IEditionCatalogService>().Verify(v => v.GetForMetadata(MetadataId), Times.Once());
        }

        [Test]
        public void should_evaluate_again_when_the_file_moves_to_another_row()
        {
            var file = GivenFile(_default, 208);

            Subject.Evaluate(file).RuntimeSibling.Should().BeSameAs(_extended);

            file.Movie = _extended;

            Subject.Evaluate(file).RuntimeSibling.Should().BeNull();

            Mocker.GetMock<IMovieService>().Verify(v => v.GetEditionSiblings(It.IsAny<Movie>()), Times.Exactly(2));
        }
    }
}
