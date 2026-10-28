// krzw(edition-runtime-catalog): edition catalog API types (GET /api/v3/edition/catalog) and small display helpers
import { LabelProps } from 'Components/Label';
import translate from 'Utilities/String/translate';

export type EditionCatalogSource =
  | 'tmdb'
  | 'wikidata'
  | 'imdb'
  | 'library'
  | 'user';

export interface EditionCatalogEntry {
  id: number;
  name: string;
  runtimeMinutes: number | null;
  source: EditionCatalogSource;
  confidence: number;
  attributes: string[];
  // Below the matching confidence (transfer labels, suspect TMDb runtime): not offered as a cut to pick
  informational?: boolean;
}

export interface EditionCatalogResponse {
  merged: EditionCatalogEntry[];
  entries: EditionCatalogEntry[];
}

const sourceTranslationKeys: Record<EditionCatalogSource, string> = {
  tmdb: 'EditionCatalogSourceTmdb',
  wikidata: 'EditionCatalogSourceWikidata',
  imdb: 'EditionCatalogSourceImdb',
  library: 'EditionCatalogSourceLibrary',
  user: 'EditionCatalogSourceUser',
};

export function getEditionCatalogSourceLabel(source?: string | null) {
  if (!source) {
    return '';
  }

  const key =
    sourceTranslationKeys[source.toLowerCase() as EditionCatalogSource];

  return key ? translate(key) : source;
}

export function getEditionCatalogSourceKind(
  source?: string | null
): NonNullable<LabelProps['kind']> {
  switch ((source ?? '').toLowerCase()) {
    case 'user':
      return 'primary';
    case 'library':
      return 'success';
    case 'imdb':
      return 'warning';
    case 'wikidata':
      return 'info';
    default:
      return 'default';
  }
}

export function formatRuntimeMinutes(minutes?: number | null) {
  if (minutes == null) {
    return '';
  }

  return translate('EditionRuntimeMinutesShort', { minutes });
}

// MediaInfo runTime is formatted by the API as "H:MM:SS" or "M:SS"; returns whole minutes (rounded) or null.
export function parseMediaInfoRuntime(runTime?: string | null) {
  if (!runTime) {
    return null;
  }

  const parts = runTime.split(':').map((p) => parseInt(p));

  if (!parts.length || parts.some((p) => isNaN(p))) {
    return null;
  }

  let seconds = 0;

  parts.forEach((p) => {
    seconds = seconds * 60 + p;
  });

  return seconds > 0 ? Math.round(seconds / 60) : null;
}

// Aliases offered when a catalog entry is picked: the preset's aliases plus the entry's raw attributes, lower-cased and de-duplicated.
export function mergeEditionAliases(...lists: (string[] | undefined)[]) {
  const seen = new Set<string>();
  const result: string[] = [];

  lists.forEach((list) => {
    (list ?? []).forEach((alias) => {
      const value = alias.trim().toLowerCase();

      if (value && !seen.has(value)) {
        seen.add(value);
        result.push(value);
      }
    });
  });

  return result;
}
