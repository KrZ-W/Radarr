using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.OrganizerTests
{
    // krzw(multi-edition)
    [TestFixture]
    public class FileNameValidationFixture : CoreTest
    {
        private TestValidator<NamingConfig> _validator;
        private NamingConfig _namingConfig;

        [SetUp]
        public void Setup()
        {
            _validator = new TestValidator<NamingConfig>
            {
                v => v.RuleFor(c => c.StandardMovieFormat).ValidMovieFormat(),
                v => v.RuleFor(c => c.MovieFolderFormat).ValidMovieFolderFormat()
            };

            _namingConfig = NamingConfig.Default;
        }

        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Name}}")]
        [TestCase("{Movie Title} ({Release Year}) {Edition Name}")]
        [TestCase("{Movie Title} ({Release Year}) [{Edition Name}]")]
        [TestCase("{Movie Title} ({Release Year}) {Edition.Name}")]
        [TestCase("{Movie Title} ({Release Year}) {edition name}")]
        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Tags}}")]
        public void should_reject_edition_tokens_in_movie_folder_format(string folderFormat)
        {
            _namingConfig.MovieFolderFormat = folderFormat;

            var result = _validator.Validate(_namingConfig);

            result.IsValid.Should().BeFalse();
            result.Errors.Should().ContainSingle(e => e.PropertyName == "MovieFolderFormat");
        }

        [Test]
        public void should_name_the_offending_token_in_the_folder_format_error()
        {
            _namingConfig.MovieFolderFormat = "{Movie Title} ({Release Year}) {edition-{Edition Name}}";

            var result = _validator.Validate(_namingConfig);

            result.Errors.Should().Contain(e => e.PropertyName == "MovieFolderFormat" && e.ErrorMessage.Contains("{Edition Name}"));
        }

        [TestCase("{Movie Title} ({Release Year})")]
        [TestCase("{Movie TitleThe} ({Release Year}) {imdb-{ImdbId}}")]
        [TestCase("{Movie CollectionThe}/{Movie Title} ({Release Year})")]
        public void should_accept_movie_folder_format_without_edition_tokens(string folderFormat)
        {
            _namingConfig.MovieFolderFormat = folderFormat;

            _validator.Validate(_namingConfig).IsValid.Should().BeTrue();
        }

        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full}")]
        [TestCase("{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full} {Custom Formats}")]
        [TestCase("{Movie Title} ({Release Year}) [{Edition Name:10}] {Quality Full}")]
        public void should_accept_edition_name_in_standard_movie_format(string movieFormat)
        {
            _namingConfig.StandardMovieFormat = movieFormat;

            _validator.Validate(_namingConfig).IsValid.Should().BeTrue();
        }

        [TestCase("{Movie Title} {Edition Name}", true)]
        [TestCase("{Movie Title} {edition-{Edition Name}}", true)]
        [TestCase("{Movie Title} {Edition Tags}", true)]
        [TestCase("{Movie Title} ({Release Year})", false)]
        public void deprecated_folder_tokens_regex_should_flag_edition_name_for_the_health_check(string folderFormat, bool expected)
        {
            FileNameValidation.DeprecatedMovieFolderTokensRegex.IsMatch(folderFormat).Should().Be(expected);
        }
    }
}
