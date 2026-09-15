using System;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.MediaFiles.GrabbedReleaseTitles
{
    /// <summary>
    /// krzw(grabbed-release-title)
    /// Some trackers publish a whole description block as the release title, e.g.
    /// "Movie.2004.1080p-GRP (…)\n\t\n\tTaille: 4 GB Seeders: 27 …". Only the first line is the
    /// release name; the rest is noise that would poison custom format matching. Applied at both
    /// import capture and backfill so the stored column is always comparable.
    /// </summary>
    public static class GrabbedReleaseTitleSanitizer
    {
        private static readonly Regex WhitespaceRunRegex = new Regex(@"\s+", RegexOptions.Compiled);
        private static readonly char[] LineSeparators = { '\r', '\n' };

        /// <summary>
        /// Returns the first non-empty line, trimmed, with internal runs of whitespace collapsed to a
        /// single space. Returns null when there is nothing left.
        /// </summary>
        public static string Sanitize(string title)
        {
            if (title == null)
            {
                return null;
            }

            var lines = title.Split(LineSeparators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var collapsed = WhitespaceRunRegex.Replace(line, " ").Trim();

                if (collapsed.Length > 0)
                {
                    return collapsed;
                }
            }

            return null;
        }
    }
}
