using FizzWare.NBuilder;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.MovieImport.Aggregation.Aggregators;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles.MovieImport.Aggregation.Aggregators
{
    // krzw(multi-edition)
    [TestFixture]
    public class AggregateEditionFixture : CoreTest<AggregateEdition>
    {
        private Movie _movie;

        [SetUp]
        public void Setup()
        {
            _movie = Builder<Movie>.CreateNew().Build();
        }

        private LocalMovie BuildLocalMovie(string fileTitle, string folderTitle, string downloadClientTitle, string path)
        {
            return new LocalMovie
            {
                FileMovieInfo = fileTitle == null ? null : Parser.Parser.ParseMovieTitle(fileTitle, false),
                FolderMovieInfo = folderTitle == null ? null : Parser.Parser.ParseMovieTitle(folderTitle, false),
                DownloadClientMovieInfo = downloadClientTitle == null ? null : Parser.Parser.ParseMovieTitle(downloadClientTitle, false),
                Path = path?.AsOsAgnostic(),
                Movie = _movie
            };
        }

        [Test]
        public void should_prefer_downloadclient()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.Theatrical.Cut.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.Extended.1080p.BluRay-Drone",
                                             "Movie.Title.2008.Directors.Cut.1080p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Movie.Title.2008\Movie.Title.2008.1080p.BluRay.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Directors Cut");
        }

        [Test]
        public void should_prefer_folder()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.Theatrical.Cut.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.Extended.1080p.BluRay-Drone",
                                             "Movie.Title.2008.1080p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Movie.Title.2008\Movie.Title.2008.1080p.BluRay.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Extended");
        }

        [Test]
        public void should_fallback_to_file()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.Theatrical.Cut.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.1080p.BluRay-Drone",
                                             "Movie.Title.2008.1080p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Movie.Title.2008\Movie.Title.2008.1080p.BluRay.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Theatrical Cut");
        }

        [Test]
        public void should_leave_edition_empty_when_nothing_carries_one()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.1080p.BluRay-Drone",
                                             "Movie.Title.2008.1080p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Movie.Title.2008\Movie.Title.2008.1080p.BluRay.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().BeNullOrEmpty();
        }

        [Test]
        public void should_pick_up_plex_tag_from_file_name_without_download_client()
        {
            var localMovie = BuildLocalMovie("Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv",
                                             "Waterworld (1995)",
                                             null,
                                             @"C:\Movies\Waterworld (1995)\Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Ulysses Cut");
        }

        [Test]
        public void should_pick_up_plex_tag_from_folder_name_without_download_client()
        {
            var localMovie = BuildLocalMovie("Waterworld (1995) Bluray-2160p.mkv",
                                             "Waterworld (1995) {edition-Ulysses Cut}",
                                             null,
                                             @"C:\Movies\Waterworld (1995) {edition-Ulysses Cut}\Waterworld (1995) Bluray-2160p.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Ulysses Cut");
        }

        [Test]
        public void should_prefer_plex_tag_on_file_over_download_client_title()
        {
            var localMovie = BuildLocalMovie("Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv",
                                             "Waterworld.1995.Extended.2160p.BluRay-Drone",
                                             "Waterworld.1995.Extended.2160p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Waterworld.1995.Extended.2160p.BluRay-Viva\Waterworld (1995) {edition-Ulysses Cut} Bluray-2160p.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Ulysses Cut");
        }

        [Test]
        public void should_prefer_plex_tag_on_parent_folder_over_download_client_title()
        {
            var localMovie = BuildLocalMovie("waterworld.2160p.mkv",
                                             "Waterworld (1995) {edition-Ulysses Cut}",
                                             "Waterworld.1995.Extended.2160p.BluRay-Viva",
                                             @"C:\Test\Unsorted Movies\Waterworld (1995) {edition-Ulysses Cut}\waterworld.2160p.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Ulysses Cut");
        }

        [Test]
        public void should_not_look_past_the_parent_folder_for_a_plex_tag()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.1080p.BluRay-Drone",
                                             "Movie.Title.2008.Extended.1080p.BluRay-Viva",
                                             @"C:\Test\{edition-Ulysses Cut}\Movie.Title.2008\Movie.Title.2008.1080p.BluRay.mkv");

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Extended");
        }

        [Test]
        public void should_not_throw_without_path()
        {
            var localMovie = BuildLocalMovie("Movie.Title.2008.1080p.BluRay-Wizzy",
                                             "Movie.Title.2008.1080p.BluRay-Drone",
                                             "Movie.Title.2008.Extended.1080p.BluRay-Viva",
                                             null);

            Subject.Aggregate(localMovie, null);

            localMovie.Edition.Should().Be("Extended");
        }
    }
}
