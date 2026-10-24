// krzw(multi-edition): edition presets offered by the add-edition modal (mirrors NzbDrone.Core.Movies.Editions.EditionPresets)

export interface EditionPreset {
  name: string;
  aliases: string[];
}

export const CUSTOM_EDITION_PRESET = 'custom';

const editionPresets: EditionPreset[] = [
  {
    name: 'Theatrical',
    aliases: [
      'theatrical',
      'theatrical cut',
      'theatrical edition',
      'theatrical version',
    ],
  },
  {
    name: 'Extended',
    aliases: [
      'extended',
      'extended cut',
      'extended edition',
      'extended version',
    ],
  },
  {
    name: "Director's Cut",
    aliases: [
      "director's cut",
      'directors cut',
      "director's edition",
      'directors edition',
      'dc',
    ],
  },
  {
    name: 'Unrated',
    aliases: ['unrated', 'unrated cut', 'unrated edition', 'unrated version'],
  },
  { name: 'Final Cut', aliases: ['final cut'] },
  { name: 'Ulysses Cut', aliases: ['ulysses cut'] },
  { name: 'IMAX', aliases: ['imax', 'imax edition'] },
  {
    name: 'Criterion',
    aliases: ['criterion', 'criterion collection', 'criterion edition'],
  },
  { name: 'Remastered', aliases: ['remastered', 'remaster'] },
  { name: 'Special Edition', aliases: ['special edition'] },
  { name: 'Ultimate Edition', aliases: ['ultimate edition'] },
  { name: 'Ultimate Cut', aliases: ['ultimate cut'] },
  { name: 'Open Matte', aliases: ['open matte'] },
  { name: 'Uncut', aliases: ['uncut', 'uncut version'] },
  {
    name: 'Anniversary Edition',
    aliases: ['anniversary edition', 'anniversary'],
  },
];

export function findEditionPreset(name: string): EditionPreset | undefined {
  return editionPresets.find((preset) => preset.name === name);
}

export default editionPresets;
