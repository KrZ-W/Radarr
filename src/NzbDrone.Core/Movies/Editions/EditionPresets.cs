// krzw(multi-edition): shipped edition presets (display name + match aliases) offered by the add-edition flow
using System.Collections.Generic;

namespace NzbDrone.Core.Movies.Editions
{
    public class EditionPreset
    {
        public EditionPreset(string name, params string[] aliases)
        {
            Name = name;
            Aliases = new List<string>(aliases);
        }

        public string Name { get; set; }
        public List<string> Aliases { get; set; }
    }

    public static class EditionPresets
    {
        // Aliases are compared through EditionNormalizer, so spelling variants only need to be listed
        // when the normalizer does not already fold them (it strips apostrophes, dots, dashes and brackets).
        public static IReadOnlyList<EditionPreset> All { get; } = new List<EditionPreset>
        {
            new EditionPreset("Theatrical", "theatrical", "theatrical cut", "theatrical edition", "theatrical version"),
            new EditionPreset("Extended", "extended", "extended cut", "extended edition", "extended version"),
            new EditionPreset("Director's Cut", "director's cut", "directors cut", "director's edition", "directors edition", "directors", "dc"),
            new EditionPreset("Unrated", "unrated", "unrated cut", "unrated edition", "unrated version"),
            new EditionPreset("Final Cut", "final cut"),
            new EditionPreset("Ulysses Cut", "ulysses cut"),
            new EditionPreset("IMAX", "imax", "imax edition"),
            new EditionPreset("Criterion", "criterion", "criterion collection", "criterion edition"),
            new EditionPreset("Remastered", "remastered", "remaster"),
            new EditionPreset("Special Edition", "special edition"),
            new EditionPreset("Ultimate Edition", "ultimate edition"),
            new EditionPreset("Ultimate Cut", "ultimate cut"),
            new EditionPreset("Open Matte", "open matte"),
            new EditionPreset("Uncut", "uncut", "uncut version"),
            new EditionPreset("Anniversary Edition", "anniversary edition", "anniversary"),
            new EditionPreset("Assembly Cut", "assembly cut"), // krzw(edition-file-reassign)
            new EditionPreset("Restored", "restored") // krzw(edition-file-reassign)
        }.AsReadOnly();
    }
}
