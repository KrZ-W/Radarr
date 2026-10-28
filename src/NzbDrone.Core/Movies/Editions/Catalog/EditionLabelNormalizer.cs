// krzw(edition-runtime-catalog): maps IMDb / Wikidata runtime labels to edition preset names with a confidence
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Movies.Editions.Catalog
{
    public static class EditionLabelNormalizer
    {
        // The first unlabelled IMDb runtime is the theatrical reference
        public const string TheatricalName = "Theatrical";
        public const int UnlabelledTheatricalConfidence = 80;

        // A label that is exactly a cut name ("director's cut", "extended edition")
        public const int CutConfidence = 70;

        // A cut name with extra words ("director's cut roadshow", "Blu Ray Extended Edition"): same preset, a notch lower,
        // so a source that lists both keeps the plain one under the preset name
        public const int EmbellishedCutConfidence = 60;

        // "original", "original cut": usually the theatrical release, less certain than an explicit "theatrical"
        public const int OriginalConfidence = 60;

        // Transfer and market labels (tv, dvd, video, approximate, country names, "cut", "edited"...): kept under their raw
        // text and never used for a verdict (below EditionRuntime.MinimumConfidence)
        public const int TransferConfidence = 20;

        private static readonly Regex DirectorsCut = Word(@"directors?\s+(cut|edition|version)|directors");
        private static readonly Regex FinalCut = Word(@"final\s+cut");
        private static readonly Regex Workprint = Word(@"work\s?print");
        private static readonly Regex Extended = Word(@"extended");
        private static readonly Regex Uncut = Word(@"uncut");
        private static readonly Regex Unrated = Word(@"unrated");
        private static readonly Regex SpecialEdition = Word(@"special\s+edition");
        private static readonly Regex Theatrical = Word(@"theatrical");

        // Labels whose whole text, after normalisation, names the cut exactly
        private static readonly Regex ExactCut = new Regex(
            @"^(unrated\s+)?(directors\s+(cut|edition|version)|extended(\s+(edition|cut|version))?|unrated(\s+(edition|cut|version))?|uncut(\s+version)?|special\s+edition|final\s+cut|workprint(\s+version)?|theatrical(\s+(edition|cut|version|release))?)$",
            RegexOptions.Compiled);

        public static (string PresetName, int Confidence) Map(string imdbOrWikidataLabel)
        {
            if (imdbOrWikidataLabel.IsNullOrWhiteSpace())
            {
                return (TheatricalName, UnlabelledTheatricalConfidence);
            }

            var raw = imdbOrWikidataLabel.Trim();
            var label = EditionNormalizer.Normalize(raw);
            var exact = ExactCut.IsMatch(label);
            var cutConfidence = exact ? CutConfidence : EmbellishedCutConfidence;

            // Order matters: "unrated director's cut" is a Director's Cut, "special DVD extended edition" an Extended
            if (DirectorsCut.IsMatch(label))
            {
                return ("Director's Cut", cutConfidence);
            }

            if (FinalCut.IsMatch(label))
            {
                return ("Final Cut", cutConfidence);
            }

            if (Workprint.IsMatch(label))
            {
                return ("Workprint", cutConfidence);
            }

            if (Extended.IsMatch(label))
            {
                return ("Extended", cutConfidence);
            }

            if (Uncut.IsMatch(label))
            {
                return ("Uncut", cutConfidence);
            }

            if (Unrated.IsMatch(label))
            {
                return ("Unrated", cutConfidence);
            }

            if (SpecialEdition.IsMatch(label))
            {
                return ("Special Edition", cutConfidence);
            }

            if (Theatrical.IsMatch(label))
            {
                return (TheatricalName, exact ? UnlabelledTheatricalConfidence : EmbellishedCutConfidence);
            }

            if (label == "original" || label == "original cut" || label == "original version" || label == "original release")
            {
                return (TheatricalName, OriginalConfidence);
            }

            // tv, dvd, video, blu-ray, approximate, country names, "cut", "edited", "heavily cut", "re-release",
            // "international", "québec", "original <market>"... : raw text, never a verdict
            return (raw, TransferConfidence);
        }

        private static Regex Word(string pattern)
        {
            return new Regex(@"\b(" + pattern + @")\b", RegexOptions.Compiled);
        }
    }
}
