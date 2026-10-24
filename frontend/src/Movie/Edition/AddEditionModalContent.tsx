// krzw(multi-edition): add-edition form, POSTs /api/v3/movie with the default row's tmdbId plus an edition object
import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { Error } from 'App/State/AppSectionState';
import CheckInput from 'Components/Form/CheckInput';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
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
  };
}

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
      },
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
