// krzw(edition-runtime-catalog): Movies > Edition Audit page: per-file verdicts (tag vs runtime vs catalog), verdict filter, multi-select apply and CSV copy
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import Alert from 'Components/Alert';
import CheckInput from 'Components/Form/CheckInput';
import SelectInput from 'Components/Form/SelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { align, icons, kinds } from 'Helpers/Props';
import { findEditionPreset } from 'Movie/Edition/editionPresets';
import Movie from 'Movie/Movie';
import { executeCommand } from 'Store/Actions/commandActions';
import { fetchMovies } from 'Store/Actions/movieActions';
import createAllMoviesSelector from 'Store/Selectors/createAllMoviesSelector';
import createCommandExecutingSelector from 'Store/Selectors/createCommandExecutingSelector';
import { CheckInputChanged, InputChanged } from 'typings/inputs';
import { SelectStateInputProps } from 'typings/props';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import {
  buildEditionAuditCsv,
  EditionAuditApplyItem,
  EditionAuditApplyResult,
  EditionAuditFilter,
  EditionAuditItem,
  getSuggestedEditionName,
  isEditionAuditItemSelectable,
  matchesEditionAuditFilter,
  sortEditionAuditItems,
} from './editionAudit';
import EditionAuditCsvModal from './EditionAuditCsvModal';
import EditionAuditRow, { EditionChoice } from './EditionAuditRow';
import styles from './EditionAudit.css';

const REFRESH_EDITION_CATALOG = 'RefreshEditionCatalog';

const columns: Column[] = [
  {
    name: 'title',
    label: () => translate('MovieTitle'),
    isVisible: true,
  },
  {
    name: 'year',
    label: () => translate('Year'),
    isVisible: true,
  },
  {
    name: 'fileEdition',
    label: () => translate('EditionAuditFileEdition'),
    isVisible: true,
  },
  {
    name: 'fileRuntime',
    label: () => translate('EditionAuditFileRuntime'),
    isVisible: true,
  },
  {
    name: 'verdict',
    label: () => translate('EditionAuditVerdict'),
    isVisible: true,
  },
  {
    name: 'suggestion',
    label: () => translate('EditionAuditSuggestion'),
    isVisible: true,
  },
  {
    name: 'editionRow',
    label: () => translate('EditionAuditEditionRow'),
    isVisible: true,
  },
  {
    name: 'applyAs',
    label: () => translate('EditionAuditApplyAs'),
    isVisible: true,
  },
];

const filterValues = [
  {
    key: 'all',
    get value() {
      return translate('All');
    },
  },
  {
    key: 'needsAction',
    get value() {
      return translate('EditionAuditFilterNeedsAction');
    },
  },
  {
    key: 'tagOnly',
    get value() {
      return translate('EditionAuditVerdictTagOnly');
    },
  },
  {
    key: 'pal',
    get value() {
      return translate('EditionAuditVerdictPal');
    },
  },
  {
    key: 'consistent',
    get value() {
      return translate('EditionAuditVerdictConsistent');
    },
  },
];

interface ApplyOutcome {
  results: EditionAuditApplyResult[];
  error: boolean;
}

function EditionAudit() {
  const dispatch = useDispatch();
  const allMovies: Movie[] = useSelector(createAllMoviesSelector());
  const isRefreshingCatalog: boolean = useSelector(
    createCommandExecutingSelector(REFRESH_EDITION_CATALOG)
  );
  const wasRefreshingCatalog = usePrevious(isRefreshingCatalog);

  const [items, setItems] = useState<EditionAuditItem[]>([]);
  const [isFetching, setIsFetching] = useState(false);
  const [isPopulated, setIsPopulated] = useState(false);
  const [error, setError] = useState(false);
  const [fetchCounter, setFetchCounter] = useState(0);

  const [filter, setFilter] = useState<EditionAuditFilter>('needsAction');
  const [includeAll, setIncludeAll] = useState(false);
  const [selected, setSelected] = useState<Record<number, boolean>>({});
  const [choices, setChoices] = useState<Record<number, EditionChoice>>({});
  const [isApplying, setIsApplying] = useState(false);
  const [applyOutcome, setApplyOutcome] = useState<ApplyOutcome | null>(null);
  const [isCsvModalOpen, setIsCsvModalOpen] = useState(false);

  useEffect(() => {
    setIsFetching(true);
    setError(false);

    const { request, abortRequest } = createAjaxRequest({
      url: '/edition/audit',
      data: { includeAll },
    });

    request.done((data: EditionAuditItem[]) => {
      setItems([...data].sort(sortEditionAuditItems));
      setIsFetching(false);
      setIsPopulated(true);
    });

    request.fail((xhr: { aborted?: boolean }) => {
      if (!xhr.aborted) {
        setError(true);
        setIsFetching(false);
      }
    });

    return abortRequest;
  }, [includeAll, fetchCounter]);

  // The catalog changes the runtime verdicts, so refresh the audit once the command finishes.
  useEffect(() => {
    if (wasRefreshingCatalog && !isRefreshingCatalog) {
      setFetchCounter((c) => c + 1);
    }
  }, [isRefreshingCatalog, wasRefreshingCatalog]);

  const moviesById = useMemo(() => {
    return allMovies.reduce((acc: Record<number, Movie>, movie) => {
      acc[movie.id] = movie;
      return acc;
    }, {});
  }, [allMovies]);

  const visibleItems = useMemo(() => {
    return items.filter((item) => matchesEditionAuditFilter(item, filter));
  }, [items, filter]);

  const selectableItems = useMemo(() => {
    return visibleItems.filter(isEditionAuditItemSelectable);
  }, [visibleItems]);

  const selectedItems = useMemo(() => {
    return selectableItems.filter((item) => selected[item.movieFileId]);
  }, [selectableItems, selected]);

  const allSelected =
    selectableItems.length > 0 &&
    selectedItems.length === selectableItems.length;
  const allUnselected = selectedItems.length === 0;

  const csv = useMemo(() => {
    return isCsvModalOpen ? buildEditionAuditCsv(visibleItems) : '';
  }, [isCsvModalOpen, visibleItems]);

  const handleRefreshPress = useCallback(() => {
    setFetchCounter((c) => c + 1);
  }, []);

  const handleRefreshCatalogPress = useCallback(() => {
    dispatch(
      executeCommand({
        name: REFRESH_EDITION_CATALOG,
        force: true,
      })
    );
  }, [dispatch]);

  const handleFilterChange = useCallback(
    ({ value }: InputChanged<EditionAuditFilter>) => {
      setFilter(value);
    },
    []
  );

  const handleIncludeAllChange = useCallback(({ value }: CheckInputChanged) => {
    setIncludeAll(value);
  }, []);

  const handleSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) => {
      setSelected(
        selectableItems.reduce((acc: Record<number, boolean>, item) => {
          acc[item.movieFileId] = value;
          return acc;
        }, {})
      );
    },
    [selectableItems]
  );

  const handleSelectedChange = useCallback(
    ({ id, value }: SelectStateInputProps) => {
      setSelected((prev) => {
        if (!!prev[id as number] === value) {
          return prev;
        }

        return { ...prev, [id]: value };
      });
    },
    []
  );

  const handleChoiceChange = useCallback(
    (movieFileId: number, choice: EditionChoice) => {
      setChoices((prev) => ({ ...prev, [movieFileId]: choice }));
    },
    []
  );

  const handleApplyPress = useCallback(() => {
    const body: EditionAuditApplyItem[] = selectedItems
      .map((item) => {
        const choice = choices[item.movieFileId];
        const editionName = (
          choice ? choice.name : getSuggestedEditionName(item) ?? ''
        ).trim();
        const preset = findEditionPreset(editionName);

        const applyItem: EditionAuditApplyItem = {
          movieId: item.movieId,
          editionName,
          runtimeMinutes:
            item.runtimeSuggestedMinutes ?? item.fileRuntimeMinutes,
        };

        if (preset) {
          applyItem.aliases = [...preset.aliases];
        }

        return applyItem;
      })
      .filter((applyItem) => !!applyItem.editionName);

    if (!body.length) {
      return;
    }

    setIsApplying(true);
    setApplyOutcome(null);

    const { request } = createAjaxRequest({
      url: '/edition/audit/apply',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(body),
    });

    request.done((results: EditionAuditApplyResult[]) => {
      setApplyOutcome({ results, error: false });
    });

    request.fail(() => {
      setApplyOutcome({ results: [], error: true });
    });

    request.always(() => {
      setIsApplying(false);
      setSelected({});
      setChoices({});
      setFetchCounter((c) => c + 1);
      dispatch(fetchMovies());
    });
  }, [selectedItems, choices, dispatch]);

  const handleCsvPress = useCallback(() => {
    setIsCsvModalOpen(true);
  }, []);

  const handleCsvModalClose = useCallback(() => {
    setIsCsvModalOpen(false);
  }, []);

  const handleApplyOutcomeDismiss = useCallback(() => {
    setApplyOutcome(null);
  }, []);

  const failedResults = applyOutcome
    ? applyOutcome.results.filter((r) => !r.ok)
    : [];
  const okCount = applyOutcome
    ? applyOutcome.results.filter((r) => r.ok).length
    : 0;

  return (
    <PageContent title={translate('EditionAudit')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('Refresh')}
            iconName={icons.REFRESH}
            isSpinning={isFetching}
            onPress={handleRefreshPress}
          />

          <PageToolbarButton
            label={translate('EditionAuditRefreshCatalog')}
            iconName={icons.REFRESH}
            isSpinning={isRefreshingCatalog}
            onPress={handleRefreshCatalogPress}
          />

          <PageToolbarSeparator />

          <PageToolbarButton
            label={translate('EditionAuditApplySelected')}
            iconName={icons.CHECK}
            isDisabled={!selectedItems.length}
            isSpinning={isApplying}
            onPress={handleApplyPress}
          />
        </PageToolbarSection>

        <PageToolbarSection alignContent={align.RIGHT}>
          <PageToolbarButton
            label={translate('EditionAuditCopyCsv')}
            iconName={icons.CLIPBOARD}
            isDisabled={!visibleItems.length}
            onPress={handleCsvPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        <div className={styles.filters}>
          <label className={styles.filter}>
            <span className={styles.filterLabel}>{translate('Filter')}</span>

            <SelectInput
              className={styles.filterSelect}
              name="filter"
              value={filter}
              values={filterValues}
              onChange={handleFilterChange}
            />
          </label>

          <label className={styles.filter}>
            <CheckInput
              name="includeAll"
              value={includeAll}
              onChange={handleIncludeAllChange}
            />

            <span className={styles.filterLabel}>
              {translate('EditionAuditIncludeAll')}
            </span>
          </label>

          <span className={styles.count}>
            {translate('EditionAuditCount', {
              visible: visibleItems.length,
              total: items.length,
            })}
          </span>
        </div>

        {applyOutcome ? (
          <Alert
            kind={
              applyOutcome.error || failedResults.length
                ? kinds.WARNING
                : kinds.SUCCESS
            }
          >
            <div className={styles.results}>
              <div>
                {applyOutcome.error
                  ? translate('EditionAuditApplyError')
                  : translate('EditionAuditApplyResults', {
                      ok: okCount,
                      failed: failedResults.length,
                    })}
              </div>

              {applyOutcome.results.length ? (
                <ul>
                  {applyOutcome.results.map((result, index) => {
                    const movie = moviesById[result.movieId];

                    return (
                      <li key={`${result.movieId}-${index}`}>
                        {movie ? movie.title : `#${result.movieId}`}:{' '}
                        {result.ok
                          ? translate('EditionAuditApplyOk')
                          : result.error || translate('Unknown')}
                      </li>
                    );
                  })}
                </ul>
              ) : null}

              <button
                className={styles.dismiss}
                type="button"
                onClick={handleApplyOutcomeDismiss}
              >
                {translate('Close')}
              </button>
            </div>
          </Alert>
        ) : null}

        {isFetching && !isPopulated ? <LoadingIndicator /> : null}

        {!isFetching && error ? (
          <Alert kind={kinds.DANGER}>
            {translate('EditionAuditLoadError')}
          </Alert>
        ) : null}

        {isPopulated && !error && !visibleItems.length ? (
          <Alert kind={kinds.INFO}>{translate('EditionAuditNoItems')}</Alert>
        ) : null}

        {isPopulated && !error && visibleItems.length ? (
          <Table
            selectAll={true}
            allSelected={allSelected}
            allUnselected={allUnselected}
            columns={columns}
            onSelectAllChange={handleSelectAllChange}
          >
            <TableBody>
              {visibleItems.map((item) => {
                const movie = moviesById[item.movieId];
                const editionRow = item.editionRowId
                  ? moviesById[item.editionRowId]
                  : undefined;

                return (
                  <EditionAuditRow
                    key={item.movieFileId}
                    item={item}
                    titleSlug={movie?.titleSlug}
                    editionRowName={editionRow?.edition?.name}
                    isSelectable={isEditionAuditItemSelectable(item)}
                    isSelected={!!selected[item.movieFileId]}
                    choice={choices[item.movieFileId]}
                    onSelectedChange={handleSelectedChange}
                    onChoiceChange={handleChoiceChange}
                  />
                );
              })}
            </TableBody>
          </Table>
        ) : null}
      </PageContentBody>

      <EditionAuditCsvModal
        isOpen={isCsvModalOpen}
        csv={csv}
        onModalClose={handleCsvModalClose}
      />
    </PageContent>
  );
}

export default EditionAudit;
