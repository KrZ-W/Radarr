using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.ParserTests
{
    [TestFixture]
    public class IsoLanguagesFixture : CoreTest
    {
        [TestCase("en")]
        [TestCase("eng")]
        [TestCase("en-US")]
        [TestCase("en-GB")]
        public void should_return_iso_language_for_English(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.English);
        }

        [TestCase("enus")]
        [TestCase("enusa")]
        [TestCase("wo")]
        public void unknown_or_invalid_code_should_return_null(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().Be(null);
        }

        [TestCase("fr")]
        [TestCase("fra")]
        [TestCase("fr-FR")]
        [TestCase("fr-CA")]
        [TestCase("fr-BE")]
        [TestCase("fr-CH")]
        public void should_return_french(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.French);
        }

        [TestCase("pt")]
        [TestCase("por")]
        [TestCase("pt-PT")]
        [TestCase("pt-AO")]
        public void should_return_portuguese(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Portuguese);
        }

        [TestCase("de")]
        [TestCase("deu")]
        [TestCase("de-DE")]
        [TestCase("de-AT")]
        [TestCase("de-AU")]
        [TestCase("de-CH")]
        public void should_return_german(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.German);
        }

        [TestCase("zh-CN")]
        [TestCase("zh-TW")]
        [TestCase("zh-HK")]
        public void should_return_chinese(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Chinese);
        }

        [TestCase("te")]
        [TestCase("tel")]
        [TestCase("te-IN")]
        public void should_return_telugu(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Telugu);
        }

        [TestCase("af")]
        [TestCase("afr")]
        [TestCase("af-ZA")]
        public void should_return_afrikaans(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Afrikaans);
        }

        [TestCase("mr")]
        [TestCase("mar")]
        [TestCase("mr-IN")]
        public void should_return_marathi(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Marathi);
        }

        [TestCase("tl")]
        [TestCase("tgl")]
        [TestCase("tl-PH")]
        public void should_return_tagalog(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Tagalog);
        }

        [TestCase("ur")]
        [TestCase("urd")]
        [TestCase("ur-PK")]
        public void should_return_urdu(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Urdu);
        }

        [TestCase("rm")]
        [TestCase("roh")]
        [TestCase("rm-CH")]
        public void should_return_romansh(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Romansh);
        }

        [TestCase("mn")]
        [TestCase("mon")]
        [TestCase("khk")]
        [TestCase("mvf")]
        [TestCase("mn-Cyrl")]
        public void should_return_mongolian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Mongolian);
        }

        [TestCase("bn")]
        [TestCase("ben")]
        [TestCase("bn-BD")]
        [TestCase("bn-IN")]
        public void should_return_bengali(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Bengali);
        }

        [TestCase("ka")]
        [TestCase("geo")]
        [TestCase("kat")]
        [TestCase("ka-GE")]
        public void should_return_georgian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Language.Should().Be(Language.Georgian);
        }

        // Regression: empty country code languages with unknown region (fallback to empty country entry)
        [TestCase("es")]
        [TestCase("es-AR")]
        [TestCase("es-CO")]
        public void should_return_spanish_for_unknown_regions(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Spanish);
        }

        [TestCase("es-MX")]
        public void should_return_spanish_latino(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.SpanishLatino);
        }

        [TestCase("it")]
        [TestCase("it-IT")]
        [TestCase("it-CH")]
        public void should_return_italian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Italian);
        }

        [TestCase("nl")]
        [TestCase("nl-BE")]
        [TestCase("nl-NL")]
        public void should_return_dutch(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Dutch);
        }

        [TestCase("ar")]
        [TestCase("ar-SA")]
        [TestCase("ar-EG")]
        public void should_return_arabic(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Arabic);
        }

        [TestCase("ja")]
        [TestCase("ja-JP")]
        public void should_return_japanese(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Japanese);
        }

        [TestCase("ru")]
        [TestCase("ru-RU")]
        public void should_return_russian(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Russian);
        }

        // Regression: non-empty country code languages with unknown region (fallback to first entry)
        [TestCase("pt-AO")]
        [TestCase("pt-MZ")]
        public void should_return_portuguese_for_unknown_regions(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Portuguese);
        }

        [TestCase("pt-BR")]
        public void should_return_portuguese_brazil(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.PortugueseBR);
        }

        [TestCase("zh-SG")]
        public void should_return_chinese_for_unknown_regions(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().NotBeNull();
            result.Language.Should().Be(Language.Chinese);
        }

        // Ensure truly invalid codes still return null
        [TestCase("xx")]
        [TestCase("xx-YY")]
        [TestCase("zz-ZZ")]
        public void unknown_language_code_with_region_should_return_null(string isoCode)
        {
            var result = IsoLanguages.Find(isoCode);
            result.Should().BeNull();
        }
    }
}
