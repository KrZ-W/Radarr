// krzw(edition-runtime-catalog): edition audit API types (GET /api/v3/edition/audit, POST /api/v3/edition/audit/apply), verdict filters and CSV export
import { LabelProps } from 'Components/Label';
import translate from 'Utilities/String/translate';

export type EditionAuditVerdict =
  | 'consistent'
  | 'tagOnly'
  | 'runtimeOnly'
  | 'conflict'
  | 'pal'
  | 'checkIdentification'
  | 'unknown';

export interface EditionAuditItem {
  movieId: number;
  title: string;
  year: number;
  movieFileId: number;
  relativePath: string;
  fileEdition: string | null;
  suggestedEdition: string | null;
  kind: string | null;
  ambiguous: boolean;
  editionRowId: number | null;
  editionRowHasFile: boolean;
  fileRuntimeMinutes: number | null;
  isPal: boolean;
  runtimeMatchKind: string | null;
  runtimeSuggestedEdition: string | null;
  runtimeSuggestedMinutes: number | null;
  catalogSource: string | null;
  verdict: EditionAuditVerdict;
}

export interface EditionAuditApplyItem {
  movieId: number;
  editionName: string;
  runtimeMinutes?: number | null;
  aliases?: string[];
}

export interface EditionAuditApplyResult {
  movieId: number;
  editionMovieId: number | null;
  movieFileId: number | null;
  ok: boolean;
  error: string | null;
}

export type EditionAuditFilter =
  | 'all'
  | 'needsAction'
  | 'tagOnly'
  | 'pal'
  | 'consistent';

const needsActionVerdicts: EditionAuditVerdict[] = [
  'runtimeOnly',
  'conflict',
  'checkIdentification',
];

export function matchesEditionAuditFilter(
  item: EditionAuditItem,
  filter: EditionAuditFilter
) {
  switch (filter) {
    case 'needsAction':
      return needsActionVerdicts.includes(item.verdict);
    case 'tagOnly':
    case 'pal':
    case 'consistent':
      return item.verdict === filter;
    default:
      return true;
  }
}

const verdictTranslationKeys: Record<EditionAuditVerdict, string> = {
  consistent: 'EditionAuditVerdictConsistent',
  tagOnly: 'EditionAuditVerdictTagOnly',
  runtimeOnly: 'EditionAuditVerdictRuntimeOnly',
  conflict: 'EditionAuditVerdictConflict',
  pal: 'EditionAuditVerdictPal',
  checkIdentification: 'EditionAuditVerdictCheckIdentification',
  unknown: 'EditionAuditVerdictUnknown',
};

export function getVerdictLabel(verdict: EditionAuditVerdict) {
  const key = verdictTranslationKeys[verdict];

  return key ? translate(key) : verdict;
}

export function getVerdictKind(
  verdict: EditionAuditVerdict
): NonNullable<LabelProps['kind']> {
  switch (verdict) {
    case 'conflict':
    case 'checkIdentification':
      return 'danger';
    case 'runtimeOnly':
      return 'warning';
    case 'tagOnly':
      return 'info';
    default:
      return 'default';
  }
}

const verdictOrder: EditionAuditVerdict[] = [
  'conflict',
  'checkIdentification',
  'runtimeOnly',
  'tagOnly',
  'pal',
  'unknown',
  'consistent',
];

export function sortEditionAuditItems(
  a: EditionAuditItem,
  b: EditionAuditItem
) {
  const verdictDiff =
    verdictOrder.indexOf(a.verdict) - verdictOrder.indexOf(b.verdict);

  if (verdictDiff !== 0) {
    return verdictDiff;
  }

  return (a.title ?? '').localeCompare(b.title ?? '');
}

export function getSuggestedEditionName(item: EditionAuditItem) {
  return item.runtimeSuggestedEdition || item.suggestedEdition || null;
}

export function isEditionAuditItemSelectable(item: EditionAuditItem) {
  return (
    (item.verdict === 'runtimeOnly' || item.verdict === 'conflict') &&
    !!getSuggestedEditionName(item)
  );
}

function csvCell(value: unknown) {
  if (value == null) {
    return '';
  }

  const text = String(value);

  return /[",\r\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

const csvColumns: (keyof EditionAuditItem)[] = [
  'movieId',
  'title',
  'year',
  'movieFileId',
  'relativePath',
  'fileEdition',
  'fileRuntimeMinutes',
  'isPal',
  'verdict',
  'suggestedEdition',
  'runtimeSuggestedEdition',
  'runtimeSuggestedMinutes',
  'runtimeMatchKind',
  'catalogSource',
  'editionRowId',
  'editionRowHasFile',
];

export function buildEditionAuditCsv(items: EditionAuditItem[]) {
  const lines = [csvColumns.join(',')];

  items.forEach((item) => {
    lines.push(csvColumns.map((column) => csvCell(item[column])).join(','));
  });

  return lines.join('\n');
}
