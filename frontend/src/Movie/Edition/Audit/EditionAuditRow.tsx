// krzw(edition-runtime-catalog): one audit row (a default-row file): file tag and runtime, verdict badge, catalog suggestion and the edition to apply
import React, { useCallback, useMemo } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import {
  formatRuntimeMinutes,
  getEditionCatalogSourceKind,
  getEditionCatalogSourceLabel,
} from 'Movie/Edition/editionCatalog';
import editionPresets from 'Movie/Edition/editionPresets';
import MovieTitleLink from 'Movie/MovieTitleLink';
import { InputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import translate from 'Utilities/String/translate';
import {
  EditionAuditItem,
  getSuggestedEditionName,
  getVerdictKind,
  getVerdictLabel,
} from './editionAudit';
import styles from './EditionAudit.css';

export const CUSTOM_EDITION_CHOICE = '__custom__';

export interface EditionChoice {
  isCustom: boolean;
  name: string;
}

interface EditionAuditRowProps {
  item: EditionAuditItem;
  titleSlug?: string;
  editionRowName?: string | null;
  isSelectable: boolean;
  isSelected: boolean;
  choice?: EditionChoice;
  onSelectedChange: (options: SelectStateInputProps) => void;
  onChoiceChange: (movieFileId: number, choice: EditionChoice) => void;
}

function EditionAuditRow({
  item,
  titleSlug,
  editionRowName,
  isSelectable,
  isSelected,
  choice,
  onSelectedChange,
  onChoiceChange,
}: EditionAuditRowProps) {
  const {
    movieId,
    movieFileId,
    title,
    year,
    relativePath,
    fileEdition,
    fileRuntimeMinutes,
    isPal,
    verdict,
    runtimeSuggestedMinutes,
    catalogSource,
    editionRowId,
    editionRowHasFile,
  } = item;

  const suggestion = getSuggestedEditionName(item);
  const chosenName = choice ? choice.name : suggestion ?? '';
  const isCustom = choice?.isCustom ?? false;

  const choiceValues = useMemo(() => {
    const names = editionPresets.map((p) => p.name);

    if (suggestion && !names.includes(suggestion)) {
      names.unshift(suggestion);
    }

    return [
      ...names.map((name) => ({ key: name, value: name })),
      {
        key: CUSTOM_EDITION_CHOICE,
        get value() {
          return translate('EditionPresetCustom');
        },
      },
    ];
  }, [suggestion]);

  const handleSelectChange = useCallback(
    ({ value }: InputChanged<string>) => {
      if (value === CUSTOM_EDITION_CHOICE) {
        onChoiceChange(movieFileId, { isCustom: true, name: chosenName });
      } else {
        onChoiceChange(movieFileId, { isCustom: false, name: value });
      }
    },
    [movieFileId, chosenName, onChoiceChange]
  );

  const handleTextChange = useCallback(
    ({ value }: InputChanged<string>) => {
      onChoiceChange(movieFileId, { isCustom: true, name: value });
    },
    [movieFileId, onChoiceChange]
  );

  return (
    <TableRow>
      {isSelectable ? (
        <TableSelectCell
          id={movieFileId}
          isSelected={isSelected}
          onSelectedChange={onSelectedChange}
        />
      ) : (
        <TableRowCell className={styles.select} />
      )}

      <TableRowCell className={styles.title} title={relativePath}>
        {titleSlug ? (
          <MovieTitleLink
            titleSlug={titleSlug}
            title={title}
            movieId={movieId}
          />
        ) : (
          title
        )}
      </TableRowCell>

      <TableRowCell className={styles.year}>{year || ''}</TableRowCell>

      <TableRowCell className={styles.fileEdition}>
        {fileEdition ? <Label kind={kinds.INFO}>{fileEdition}</Label> : '-'}
      </TableRowCell>

      <TableRowCell className={styles.runtime}>
        {formatRuntimeMinutes(fileRuntimeMinutes)}
        {isPal ? (
          <Label
            className={styles.pal}
            kind={kinds.DEFAULT}
            title={translate('EditionAuditPalHint')}
          >
            {translate('EditionAuditVerdictPal')}
          </Label>
        ) : null}
      </TableRowCell>

      <TableRowCell className={styles.verdict}>
        <Label kind={getVerdictKind(verdict)}>{getVerdictLabel(verdict)}</Label>
      </TableRowCell>

      <TableRowCell className={styles.suggestion}>
        {suggestion ? (
          <>
            <span>{suggestion}</span>
            {runtimeSuggestedMinutes == null ? null : (
              <span className={styles.minutes}>
                {formatRuntimeMinutes(runtimeSuggestedMinutes)}
              </span>
            )}
            {catalogSource ? (
              <Label kind={getEditionCatalogSourceKind(catalogSource)}>
                {getEditionCatalogSourceLabel(catalogSource)}
              </Label>
            ) : null}
          </>
        ) : (
          '-'
        )}
      </TableRowCell>

      <TableRowCell className={styles.editionRow}>
        {editionRowId ? (
          <>
            <span>{editionRowName || suggestion || `#${editionRowId}`}</span>
            <Label kind={editionRowHasFile ? kinds.SUCCESS : kinds.DANGER}>
              {editionRowHasFile
                ? translate('Downloaded')
                : translate('Missing')}
            </Label>
          </>
        ) : (
          '-'
        )}
      </TableRowCell>

      <TableRowCell className={styles.applyAs}>
        {isSelectable ? (
          <div className={styles.applyAsInputs}>
            <SelectInput
              className={styles.applyAsSelect}
              name="editionChoice"
              value={isCustom ? CUSTOM_EDITION_CHOICE : chosenName}
              values={choiceValues}
              onChange={handleSelectChange}
            />

            {isCustom ? (
              <TextInput
                className={styles.applyAsText}
                name="editionName"
                value={chosenName}
                onChange={handleTextChange}
              />
            ) : null}
          </div>
        ) : null}
      </TableRowCell>
    </TableRow>
  );
}

export default EditionAuditRow;
