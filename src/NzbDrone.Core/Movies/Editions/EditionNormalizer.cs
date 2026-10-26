// krzw(multi-edition): canonical lower-case form of edition names and aliases used for every edition comparison
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions
{
    public static class EditionNormalizer
    {
        // Apostrophes vanish ("director's" -> "directors"); dots, dashes, underscores and brackets become spaces.
        private static readonly Regex ApostropheRegex = new Regex(@"['’‘`]", RegexOptions.Compiled);
        private static readonly Regex SeparatorRegex = new Regex(@"[\.\-_\[\]\(\)\{\}]", RegexOptions.Compiled);
        private static readonly Regex WhitespaceRegex = new Regex(@"\s+", RegexOptions.Compiled);

        public static string Normalize(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return "";
            }

            var result = value.ToLowerInvariant();

            result = ApostropheRegex.Replace(result, "");
            result = SeparatorRegex.Replace(result, " ");
            result = WhitespaceRegex.Replace(result, " ");

            return result.Trim();
        }
    }
}
