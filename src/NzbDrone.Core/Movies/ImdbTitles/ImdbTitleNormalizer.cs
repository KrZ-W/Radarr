using System.Globalization;
using System.Text;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.ImdbTitles
{
    // krzw(imdb-title-provider): the "does the movie already have this title" key. NFD-decompose,
    // drop combining marks, lowercase, fold every run of non [a-z0-9] into one space, trim - the
    // same normalisation the external feeder used, so a dataset title differing from a stored title
    // only by accents, case or punctuation is treated as already present.
    public static class ImdbTitleNormalizer
    {
        public static string Normalize(string title)
        {
            if (title.IsNullOrWhiteSpace())
            {
                return string.Empty;
            }

            var decomposed = title.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            var pendingSpace = false;

            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                {
                    continue;
                }

                var lower = char.ToLowerInvariant(c);

                if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
                {
                    if (pendingSpace && builder.Length > 0)
                    {
                        builder.Append(' ');
                    }

                    pendingSpace = false;
                    builder.Append(lower);
                }
                else
                {
                    pendingSpace = true;
                }
            }

            return builder.ToString();
        }
    }
}
