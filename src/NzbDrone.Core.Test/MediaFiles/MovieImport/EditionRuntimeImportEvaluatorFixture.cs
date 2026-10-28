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
        public void should_not_rehome_when_the_catalog_names_another_cut_at_that_runtime()
        {
            _catalog.Add(new EditionCatalogEntry { MovieMetadataId = MetadataId, Name = "Unrated", RuntimeMinutes = 207, Source = EditionCatalogSource.Imdb, Confidence = 70 });

            Subject.FindRehomeTarget(GivenFile(_default, 208)).Should().BeNull();
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
    }
}
