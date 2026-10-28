// krzw(multi-edition): add-edition form, POSTs /api/v3/movie with the default row's tmdbId plus an edition object
// krzw(edition-runtime-catalog): "Known editions" from the edition catalog pre-fill name, aliases and expected runtime
import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { Error } from 'App/State/AppSectionState';
import CheckInput from 'Components/Form/CheckInput';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Label from 'Components/Label'; // krzw(edition-runtime-catalog)
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link'; // krzw(edition-runtime-catalog)
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator'; // krzw(edition-runtime-catalog)
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import Movie from 'Movie/Movie';
import useMovie from 'Movie/useMovie';
import { updateItem } from 'Store/Actions/baseActions';
import createAllMoviesSelector from 'Store/Selectors/createAllMoviesSelector';
import selectSettings from 'Store/Selectors/selectSettings';
import { InputChanged } from 'typings/inputs';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
// krzw(edition-runtime-catalog)
import {
  EditionCatalogEntry,
  EditionCatalogResponse,
  formatRuntimeMinutes,
  getEditionCatalogSourceKind,
  getEditionCatalogSourceLabel,
  mergeEditionAliases,
} from './editionCatalog';
import editionPresets, {
  CUSTOM_EDITION_PRESET,
  findEditionPreset,
} from './editionPresets';
import styles from './AddEditionModalContent.css';

export interface AddEditionModalContentProps {
  movieId: number;
  onModalClose: () => void;
}

type AddEditionForm = {
  preset: string;
  name: string;
  aliases: string[];
  qualityProfileId: number;
  monitored: boolean;
  acceptUntagged: boolean;
  searchForMovie: boolean;
  expectedRuntimeMinutes: number | null; // krzw(edition-runtime-catalog)
};

const presetValues = [
  ...editionPresets.map((preset) => ({
    key: preset.name,
    value: preset.name,
  })),
  {
    key: CUSTOM_EDITION_PRESET,
    get value() {
      return translate('EditionPresetCustom');
    },
  },
];

function getInitialForm(movie: Movie, usedNames: string[]): AddEditionForm {
  const preset =
    editionPresets.find((p) => !usedNames.includes(p.name.toLowerCase())) ??
    editionPresets[0];

  return {
    preset: preset.name,
    name: preset.name,
    aliases: [...preset.aliases],
    qualityProfileId: movie.qualityProfileId,
    monitored: true,
    acceptUntagged: false,
    searchForMovie: true,
    expectedRuntimeMinutes: null, // krzw(edition-runtime-catalog)
  };
}

// krzw(edition-runtime-catalog) begin
interface KnownEditionItemProps {
  entry: EditionCatalogEntry;
  isUsed: boolean;
  isPicked: boolean;
  onPress: (entry: EditionCatalogEntry) => void;
}

function KnownEditionItem({
  entry,
  isUsed,
  isPicked,
  onPress,
}: KnownEditionItemProps) {
  const handlePress = useCallback(() => {
    onPress(entry);
  }, [entry, onPress]);

  return (
    <Link
      className={isPicked ? styles.knownEditionPicked : styles.knownEdition}
      title={
        isUsed
          ? translate('KnownEditionAlreadyAdded')
          : translate('KnownEditionPick')
      }
      onPress={handlePress}
    >
      <span className={isUsed ? styles.knownEditionNameUsed : undefined}>
        {entry.name}
      </span>

      {entry.runtimeMinutes == null ? null : (
        <span className={styles.knownEditionRuntime}>
          {formatRuntimeMinutes(entry.runtimeMinutes)}
        </span>
      )}

      <Label kind={getEditionCatalogSourceKind(entry.source)}>
        {getEditionCatalogSourceLabel(entry.source)}
      </Label>
    </Link>
  );
}
// krzw(edition-runtime-catalog) end

function AddEditionModalContent({
  movieId,
  onModalClose,
}: AddEditionModalContentProps) {
  const dispatch = useDispatch();
  const movie = useMovie(movieId);
  const allMovies = useSelector(createAllMoviesSelector());

  const usedNames = useMemo(() => {
    if (!movie) {
      return [];
    }

    return allMovies.reduce((acc: string[], m) => {
      if (m.tmdbId === movie.tmdbId && m.edition) {
        acc.push(m.edition.name.toLowerCase());
      }

      return acc;
    }, []);
  }, [allMovies, movie]);

  const [form, setForm] = useState<AddEditionForm>(() =>
    getInitialForm(movie!, usedNames)
  );
  const [isAdding, setIsAdding] = useState(false);
  // krzw(edition-runtime-catalog) begin: catalog entries for this title
  const [catalog, setCatalog] = useState<EditionCatalogResponse | null>(null);
  const [isCatalogFetching, setIsCatalogFetching] = useState(true);
  const [catalogError, setCatalogError] = useState(false);
  const catalogMovieId = movie?.editionParentId ?? movie?.id;
  const knownEditions = useMemo(
    () => (catalog?.merged ?? []).filter((entry) => !entry.informational),
    [catalog]
  );

  useEffect(() => {
    if (!catalogMovieId) {
      setIsCatalogFetching(false);
      return;
    }

    const { request, abortRequest } = createAjaxRequest({
      url: '/edition/catalog',
      data: { movieId: catalogMovieId },
    });

    request.done((data: EditionCatalogResponse) => {
      setCatalog(data);
      setIsCatalogFetching(false);
    });

    request.fail((xhr: { aborted?: boolean }) => {
      if (!xhr.aborted) {
        setCatalogError(true);
        setIsCatalogFetching(false);
      }
    });

    return abortRequest;
  }, [catalogMovieId]);

  const handleCatalogEntryPress = useCallback(
    (entry: EditionCatalogEntry) => {
      const preset = findEditionPreset(entry.name);
      const rawAttributes = (catalog?.entries ?? [])
        .filter((r) => r.name.toLowerCase() === entry.name.toLowerCase())
        .flatMap((r) => r.attributes ?? []);

      setForm((prev) => ({
        ...prev,
        preset: preset ? preset.name : CUSTOM_EDITION_PRESET,
        name: entry.name,
        aliases: mergeEditionAliases(
          preset?.aliases,
          entry.attributes,
          rawAttributes
        ),
        expectedRuntimeMinutes:
          entry.runtimeMinutes ?? prev.expectedRuntimeMinutes,
      }));
    },
    [catalog]
  );
  // krzw(edition-runtime-catalog) end
  const [addError, setAddError] = useState<Error | undefined>(undefined);

  const { settings, validationErrors, validationWarnings } = useMemo(() => {
    return selectSettings(form, {}, addError);
  }, [form, addError]);

  const handleInputChange = useCallback(({ name, value }: InputChanged) => {
    setForm((prev) => ({ ...prev, [name]: value }));
  }, []);

  const handlePresetChange = useCallback(({ value }: InputChanged<string>) => {
    const preset = findEditionPreset(value);

    setForm((prev) => ({
      ...prev,
      preset: value,
      name: preset ? preset.name : prev.name,
      aliases: preset ? [...preset.aliases] : prev.aliases,
    }));
  }, []);

  const handleNameChange = useCallback(({ value }: InputChanged<string>) => {
    setForm((prev) => ({
      ...prev,
      name: value,
      preset:
        findEditionPreset(prev.preset)?.name === value
          ? prev.preset
          : CUSTOM_EDITION_PRESET,
    }));
  }, []);

  const handleAddPress = useCallback(() => {
    if (!movie) {
      return;
    }

    setIsAdding(true);
    setAddError(undefined);

    const body = {
      tmdbId: movie.tmdbId,
      title: movie.title,
      year: movie.year,
      titleSlug: movie.titleSlug,
      qualityProfileId: form.qualityProfileId,
      monitored: form.monitored,
      minimumAvailability: movie.minimumAvailability,
      rootFolderPath: movie.rootFolderPath,
      path: movie.path,
      tags: movie.tags,
      addOptions: {
        monitor: form.monitored ? 'movieOnly' : 'none',
        searchForMovie: form.searchForMovie,
      },
      edition: {
        name: form.name.trim(),
        aliases: form.aliases,
        acceptUntagged: form.acceptUntagged,
        expectedRuntimeMinutes: form.expectedRuntimeMinutes, // krzw(edition-runtime-catalog)
      },
      expectedRuntimeMinutes: form.expectedRuntimeMinutes, // krzw(edition-runtime-catalog)
    };

    const { request } = createAjaxRequest({
      url: '/movie',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify(body),
    });

    request.done((data: Movie) => {
      dispatch(updateItem({ section: 'movies', ...data }));
      setIsAdding(false);
      onModalClose();
    });

    request.fail((xhr: Error) => {
      setIsAdding(false);
      setAddError(xhr);
    });
  }, [movie, form, dispatch, onModalClose]);

  if (!movie) {
    return null;
  }

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>
        {translate('AddEditionModalHeader', { title: movie.title })}
      </ModalHeader>

      <ModalBody>
        <Form
          validationErrors={validationErrors}
          validationWarnings={validationWarnings}
        >
          {/* krzw(edition-runtime-catalog) begin: known editions from the catalog */}
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('KnownEditions')}</FormLabel>

            <div className={styles.knownEditions}>
              {isCatalogFetching ? <LoadingIndicator size={20} /> : null}

              {!isCatalogFetching && catalogError ? (
                <div className={styles.knownEditionsMessage}>
                  {translate('KnownEditionsLoadError')}
                </div>
              ) : null}

              {!isCatalogFetching && !catalogError && !knownEditions.length ? (
                <div className={styles.knownEditionsMessage}>
                  {translate('KnownEditionsNone')}
                </div>
              ) : null}

              {knownEditions.map((entry) => {
                const isUsed = usedNames.includes(entry.name.toLowerCase());
                const isPicked =
                  form.name.toLowerCase() === entry.name.toLowerCase();

                return (
                  <KnownEditionItem
                    key={`${entry.id}-${entry.name}`}
                    entry={entry}
                    isUsed={isUsed}
                    isPicked={isPicked}
                    onPress={handleCatalogEntryPress}
                  />
                );
              })}
            </div>
          </FormGroup>
          {/* krzw(edition-runtime-catalog) end */}

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('EditionPreset')}</FormLabel>

            <FormInputGroup
              type={inputTypes.SELECT}
              name="preset"
              value={form.preset}
              values={presetValues}
              helpText={translate('EditionPresetHelpText')}
              onChange={handlePresetChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('EditionName')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT}
              name="name"
              value={form.name}
              helpText={translate('EditionNameHelpText')}
              errors={settings.name.errors}
              warnings={settings.name.warnings}
              onChange={handleNameChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('EditionAliases')}</FormLabel>

            <FormInputGroup
              type={inputTypes.TEXT_TAG}
              name="aliases"
              value={form.aliases}
              helpText={translate('EditionAliasesHelpText')}
              onChange={handleInputChange}
            />
          </FormGroup>

          {/* krzw(edition-runtime-catalog) */}
          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('EditionExpectedRuntime')}</FormLabel>

            <FormInputGroup
              type={inputTypes.NUMBER}
              name="expectedRuntimeMinutes"
              value={form.expectedRuntimeMinutes}
              unit={translate('Minutes')}
              min={1}
              max={1440}
              helpText={translate('EditionExpectedRuntimeHelpText')}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('QualityProfile')}</FormLabel>

            <FormInputGroup
              type={inputTypes.QUALITY_PROFILE_SELECT}
              name="qualityProfileId"
              value={form.qualityProfileId}
              errors={settings.qualityProfileId.errors}
              warnings={settings.qualityProfileId.warnings}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('Monitored')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="monitored"
              value={form.monitored}
              helpText={translate('MonitoredMovieHelpText')}
              onChange={handleInputChange}
            />
          </FormGroup>

          <FormGroup size={sizes.MEDIUM}>
            <FormLabel>{translate('AcceptUntaggedReleases')}</FormLabel>

            <FormInputGroup
              type={inputTypes.CHECK}
              name="acceptUntagged"
              value={form.acceptUntagged}
              helpText={translate('AcceptUntaggedReleasesHelpText')}
              onChange={handleInputChange}
            />
          </FormGroup>
        </Form>
      </ModalBody>

      <ModalFooter className={styles.modalFooter}>
        <label className={styles.searchForMissingMovieLabelContainer}>
          <span className={styles.searchForMissingMovieLabel}>
            {translate('StartSearchForMissingMovie')}
          </span>

          <CheckInput
            containerClassName={styles.searchForMissingMovieContainer}
            className={styles.searchForMissingMovieInput}
            name="searchForMovie"
            value={form.searchForMovie}
            onChange={handleInputChange}
          />
        </label>

        <Button onPress={onModalClose}>{translate('Cancel')}</Button>

        <SpinnerErrorButton
          kind={kinds.SUCCESS}
          error={addError}
          isSpinning={isAdding}
          onPress={handleAddPress}
        >
          {translate('AddEdition')}
        </SpinnerErrorButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default AddEditionModalContent;
