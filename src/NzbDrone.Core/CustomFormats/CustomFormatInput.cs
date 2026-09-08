using System.Collections.Generic;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Movies;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.CustomFormats
{
    public class CustomFormatInput
    {
        public ParsedMovieInfo MovieInfo { get; set; }
        public Movie Movie { get; set; }
        public long Size { get; set; }
        public IndexerFlags IndexerFlags { get; set; }
        public List<Language> Languages { get; set; }
        public List<string> AudioTitles { get; set; }  // krzw(audio-title)
        public string Filename { get; set; }

        public CustomFormatInput()
        {
            Languages = new List<Language>();
            AudioTitles = new List<string>();  // krzw(audio-title)
        }
    }
}
