import React, { useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { EnhancedSelectInputValue } from 'Components/Form/Select/EnhancedSelectInput';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import useShowAdvancedSettings from 'Helpers/Hooks/useShowAdvancedSettings';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import RootFolders from 'RootFolder/RootFolders';
import SettingsToolbar from 'Settings/SettingsToolbar';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  fetchMediaManagementSettings,
  saveMediaManagementSettings,
  saveNamingSettings,
  setMediaManagementSettingsValue,
} from 'Store/Actions/settingsActions';
import createSettingsSectionSelector from 'Store/Selectors/createSettingsSectionSelector';
import useIsWindows from 'System/useIsWindows';
import { InputChanged } from 'typings/inputs';
import isEmpty from 'Utilities/Object/isEmpty';
import translate from 'Utilities/String/translate';
import Naming from './Naming/Naming';
import AddRootFolder from './RootFolder/AddRootFolder';

const SECTION = 'mediaManagement';

const rescanAfterRefreshOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'always',
    get value() {
      return translate('Always');
    },
  },
  {
    key: 'afterManual',
    get value() {
      return translate('AfterManualRefresh');
    },
  },
  {
    key: 'never',
    get value() {
      return translate('Never');
    },
  },
];

const downloadPropersAndRepacksOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'preferAndUpgrade',
    get value() {
      return translate('PreferAndUpgrade');
    },
  },
  {
    key: 'doNotUpgrade',
    get value() {
      return translate('DoNotUpgradeAutomatically');
    },
  },
  {
    key: 'doNotPrefer',
    get value() {
      return translate('DoNotPrefer');
    },
  },
];

const fileDateOptions: EnhancedSelectInputValue<string>[] = [
  {
    key: 'none',
    get value() {
      return translate('None');
    },
  },
  {
    key: 'cinemas',
    get value() {
      return translate('InCinemasDate');
    },
  },
  {
    key: 'release',
    get value() {
      return translate('PhysicalReleaseDate');
    },
  },
];

function MediaManagement() {
  const dispatch = useDispatch();
  const showAdvancedSettings = useShowAdvancedSettings();
  const hasNamingPendingChanges = !isEmpty(
    useSelector((state: AppState) => state.settings.naming.pendingChanges)
  );
  const isWindows = useIsWindows();
  const {
    isFetching,
    isPopulated,
    isSaving,
    error,
    settings,
    hasSettings,
    hasPendingChanges,
    validationErrors,
    validationWarnings,
  } = useSelector(createSettingsSectionSelector(SECTION));

  const handleSavePress = useCallback(() => {
    dispatch(saveMediaManagementSettings());
    dispatch(saveNamingSettings());
  }, [dispatch]);

  const handleInputChange = useCallback(
    (change: InputChanged) => {
      // @ts-expect-error - actions are not typed
      dispatch(setMediaManagementSettingsValue(change));
    },
    [dispatch]
  );

  useEffect(() => {
    dispatch(fetchMediaManagementSettings());

    return () => {
      dispatch(clearPendingChanges({ section: `settings.${SECTION}` }));
    };
  }, [dispatch]);

  return (
    <PageContent title={translate('MediaManagementSettings')}>
      <SettingsToolbar
        isSaving={isSaving}
        hasPendingChanges={hasNamingPendingChanges || hasPendingChanges}
        onSavePress={handleSavePress}
      />

      <PageContentBody>
        <Naming />

        {isFetching ? (
          <FieldSet legend={translate('NamingSettings')}>
            <LoadingIndicator />
          </FieldSet>
        ) : null}

        {!isFetching && error ? (
          <FieldSet legend={translate('NamingSettings')}>
            <Alert kind={kinds.DANGER}>
              {translate('MediaManagementSettingsLoadError')}
            </Alert>
          </FieldSet>
        ) : null}

        {hasSettings && isPopulated && !error ? (
          <Form
            id="mediaManagementSettings"
            validationErrors={validationErrors}
            validationWarnings={validationWarnings}
          >
            {showAdvancedSettings ? (
              <FieldSet legend={translate('Folders')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('CreateEmptyMovieFolders')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    isDisabled={
                      settings.deleteEmptyFolders.value &&
                      !settings.createEmptyMovieFolders.value
                    }
                    name="createEmptyMovieFolders"
                    helpText={translate('CreateEmptyMovieFoldersHelpText')}
                    onChange={handleInputChange}
                    {...settings.createEmptyMovieFolders}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('DeleteEmptyFolders')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    isDisabled={
                      settings.createEmptyMovieFolders.value &&
                      !settings.deleteEmptyFolders.value
                    }
                    name="deleteEmptyFolders"
                    helpText={translate('DeleteEmptyMovieFoldersHelpText')}
                    onChange={handleInputChange}
                    {...settings.deleteEmptyFolders}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}

            {showAdvancedSettings ? (
              <FieldSet legend={translate('Importing')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('SkipFreeSpaceCheck')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="skipFreeSpaceCheckWhenImporting"
                    helpText={translate('SkipFreeSpaceCheckHelpText')}
                    onChange={handleInputChange}
                    {...settings.skipFreeSpaceCheckWhenImporting}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('MinimumFreeSpace')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    unit="MB"
                    name="minimumFreeSpaceWhenImporting"
                    helpText={translate('MinimumFreeSpaceHelpText')}
                    onChange={handleInputChange}
                    {...settings.minimumFreeSpaceWhenImporting}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>
                    {translate('UseHardlinksInsteadOfCopy')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="copyUsingHardlinks"
                    helpText={translate('CopyUsingHardlinksMovieHelpText')}
                    helpTextWarning={translate(
                      'CopyUsingHardlinksHelpTextWarning'
                    )}
                    onChange={handleInputChange}
                    {...settings.copyUsingHardlinks}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('ImportUsingScript')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="useScriptImport"
                    helpText={translate('ImportUsingScriptHelpText')}
                    onChange={handleInputChange}
                    {...settings.useScriptImport}
                  />
                </FormGroup>

                {settings.useScriptImport.value ? (
                  <FormGroup
                    advancedSettings={showAdvancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('ImportScriptPath')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.PATH}
                      includeFiles={true}
                      name="scriptImportPath"
                      helpText={translate('ImportScriptPathHelpText')}
                      onChange={handleInputChange}
                      {...settings.scriptImportPath}
                    />
                  </FormGroup>
                ) : null}

                <FormGroup size={sizes.MEDIUM}>
                  <FormLabel>{translate('ImportExtraFiles')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="importExtraFiles"
                    helpText={translate('ImportExtraFilesMovieHelpText')}
                    onChange={handleInputChange}
                    {...settings.importExtraFiles}
                  />
                </FormGroup>

                {settings.importExtraFiles.value ? (
                  <FormGroup
                    advancedSettings={showAdvancedSettings}
                    isAdvanced={true}
                  >
                    <FormLabel>{translate('ImportExtraFiles')}</FormLabel>

                    <FormInputGroup
                      type={inputTypes.TEXT}
                      name="extraFileExtensions"
                      helpTexts={[
                        translate('ExtraFileExtensionsHelpText'),
                        translate('ExtraFileExtensionsHelpTextsExamples'),
                      ]}
                      onChange={handleInputChange}
                      {...settings.extraFileExtensions}
                    />
                  </FormGroup>
                ) : null}
              </FieldSet>
            ) : null}

            <FieldSet legend={translate('FileManagement')}>
              <FormGroup size={sizes.MEDIUM}>
                <FormLabel>{translate('UnmonitorDeletedMovies')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="autoUnmonitorPreviouslyDownloadedMovies"
                  helpText={translate('UnmonitorDeletedMoviesHelpText')}
                  onChange={handleInputChange}
                  {...settings.autoUnmonitorPreviouslyDownloadedMovies}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
                size={sizes.MEDIUM}
              >
                <FormLabel>{translate('DownloadPropersAndRepacks')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="downloadPropersAndRepacks"
                  helpTexts={[
                    translate('DownloadPropersAndRepacksHelpText'),
                    translate('DownloadPropersAndRepacksHelpTextCustomFormat'),
                  ]}
                  helpTextWarning={
                    settings.downloadPropersAndRepacks.value === 'doNotPrefer'
                      ? translate('DownloadPropersAndRepacksHelpTextWarning')
                      : undefined
                  }
                  values={downloadPropersAndRepacksOptions}
                  onChange={handleInputChange}
                  {...settings.downloadPropersAndRepacks}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
                size={sizes.MEDIUM}
              >
                <FormLabel>{translate('AnalyseVideoFiles')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="enableMediaInfo"
                  helpText={translate('AnalyseVideoFilesHelpText')}
                  onChange={handleInputChange}
                  {...settings.enableMediaInfo}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>
                  {translate('RescanMovieFolderAfterRefresh')}
                </FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="rescanAfterRefresh"
                  helpText={translate('RescanAfterRefreshMovieHelpText')}
                  helpTextWarning={translate(
                    'RescanAfterRefreshHelpTextWarning'
                  )}
                  values={rescanAfterRefreshOptions}
                  onChange={handleInputChange}
                  {...settings.rescanAfterRefresh}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('ChangeFileDate')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="fileDate"
                  helpText={translate('ChangeFileDateHelpText')}
                  values={fileDateOptions}
                  onChange={handleInputChange}
                  {...settings.fileDate}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('RecyclingBin')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.PATH}
                  name="recycleBin"
                  helpText={translate('RecyclingBinHelpText')}
                  includeFiles={false}
                  onChange={handleInputChange}
                  {...settings.recycleBin}
                />
              </FormGroup>

              <FormGroup
                advancedSettings={showAdvancedSettings}
                isAdvanced={true}
              >
                <FormLabel>{translate('RecyclingBinCleanup')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="recycleBinCleanupDays"
                  helpText={translate('RecyclingBinCleanupHelpText')}
                  helpTextWarning={translate(
                    'RecyclingBinCleanupHelpTextWarning'
                  )}
                  min={0}
                  onChange={handleInputChange}
                  {...settings.recycleBinCleanupDays}
                />
              </FormGroup>
            </FieldSet>

            {/* krzw(regional-translations) */}
            {showAdvancedSettings ? (
              <FieldSet legend={translate('RegionalTranslations')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('RegionalTranslationSearchMode')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="regionalTranslationSearchMode"
                    helpText={translate(
                      'RegionalTranslationSearchModeHelpText'
                    )}
                    values={[
                      {
                        key: 'standard',
                        get value() {
                          return translate(
                            'RegionalTranslationSearchModeStandard'
                          );
                        },
                      },
                      {
                        key: 'onePerRegion',
                        get value() {
                          return translate(
                            'RegionalTranslationSearchModeOnePerRegion'
                          );
                        },
                      },
                      {
                        key: 'allTitles',
                        get value() {
                          return translate(
                            'RegionalTranslationSearchModeAllTitles'
                          );
                        },
                      },
                    ]}
                    onChange={handleInputChange}
                    {...settings.regionalTranslationSearchMode}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('RegionalTranslationVariants')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="regionalTranslationVariants"
                    helpText={translate('RegionalTranslationVariantsHelpText')}
                    onChange={handleInputChange}
                    {...settings.regionalTranslationVariants}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}

            {/* krzw(audio-language-verification) */}
            {showAdvancedSettings ? (
              <FieldSet legend={translate('AudioLanguageVerification')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationEnabled')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="audioLanguageVerificationEnabled"
                    helpText={translate(
                      'AudioLanguageVerificationEnabledHelpText'
                    )}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationEnabled}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationEndpoint')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="audioLanguageVerificationEndpoint"
                    helpText={translate(
                      'AudioLanguageVerificationEndpointHelpText'
                    )}
                    placeholder="http://whisper:9000"
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationEndpoint}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationConfidenceThreshold')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="audioLanguageVerificationConfidenceThreshold"
                    helpText={translate(
                      'AudioLanguageVerificationConfidenceThresholdHelpText'
                    )}
                    isFloat={true}
                    min={0}
                    max={1}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationConfidenceThreshold}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationClipOffset')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="audioLanguageVerificationClipOffset"
                    unit={translate('Seconds')}
                    helpText={translate(
                      'AudioLanguageVerificationClipOffsetHelpText'
                    )}
                    min={0}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationClipOffset}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationClipLength')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="audioLanguageVerificationClipLength"
                    unit={translate('Seconds')}
                    helpText={translate(
                      'AudioLanguageVerificationClipLengthHelpText'
                    )}
                    min={1}
                    max={600}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationClipLength}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationVerifyTagged')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="audioLanguageVerificationVerifyTagged"
                    helpText={translate(
                      'AudioLanguageVerificationVerifyTaggedHelpText'
                    )}
                    values={[
                      {
                        key: 'never',
                        get value() {
                          return translate('Never');
                        },
                      },
                      {
                        key: 'forReleaseGroups',
                        get value() {
                          return translate(
                            'AudioLanguageVerificationVerifyTaggedForReleaseGroups'
                          );
                        },
                      },
                      {
                        key: 'always',
                        get value() {
                          return translate('Always');
                        },
                      },
                    ]}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationVerifyTagged}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationVerifyTaggedGroups')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="audioLanguageVerificationVerifyTaggedGroups"
                    helpText={translate(
                      'AudioLanguageVerificationVerifyTaggedGroupsHelpText'
                    )}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationVerifyTaggedGroups}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioLanguageVerificationTimeout')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.NUMBER}
                    name="audioLanguageVerificationTimeout"
                    unit={translate('Seconds')}
                    helpText={translate(
                      'AudioLanguageVerificationTimeoutHelpText'
                    )}
                    min={1}
                    max={3600}
                    onChange={handleInputChange}
                    {...settings.audioLanguageVerificationTimeout}
                  />
                </FormGroup>

                {/* krzw(audio-track-retag) */}
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('AudioTrackRetagEnabled')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="audioTrackRetagEnabled"
                    helpText={translate('AudioTrackRetagEnabledHelpText')}
                    onChange={handleInputChange}
                    {...settings.audioTrackRetagEnabled}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioTrackRetagHardlinkMode')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="audioTrackRetagHardlinkMode"
                    helpText={translate('AudioTrackRetagHardlinkModeHelpText')}
                    values={[
                      {
                        key: 'skip',
                        get value() {
                          return translate('AudioTrackRetagHardlinkModeSkip');
                        },
                      },
                      {
                        key: 'copyThenRetag',
                        get value() {
                          return translate(
                            'AudioTrackRetagHardlinkModeCopyThenRetag'
                          );
                        },
                      },
                      {
                        key: 'retagInPlace',
                        get value() {
                          return translate(
                            'AudioTrackRetagHardlinkModeRetagInPlace'
                          );
                        },
                      },
                    ]}
                    onChange={handleInputChange}
                    {...settings.audioTrackRetagHardlinkMode}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>
                    {translate('AudioTrackRetagNonMkvMode')}
                  </FormLabel>

                  <FormInputGroup
                    type={inputTypes.SELECT}
                    name="audioTrackRetagNonMkvMode"
                    helpText={translate('AudioTrackRetagNonMkvModeHelpText')}
                    values={[
                      {
                        key: 'skip',
                        get value() {
                          return translate('AudioTrackRetagNonMkvModeSkip');
                        },
                      },
                      {
                        key: 'remuxToMkv',
                        get value() {
                          return translate(
                            'AudioTrackRetagNonMkvModeRemuxToMkv'
                          );
                        },
                      },
                    ]}
                    onChange={handleInputChange}
                    {...settings.audioTrackRetagNonMkvMode}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}

            {showAdvancedSettings && !isWindows ? (
              <FieldSet legend={translate('Permissions')}>
                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                  size={sizes.MEDIUM}
                >
                  <FormLabel>{translate('SetPermissions')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.CHECK}
                    name="setPermissionsLinux"
                    helpText={translate('SetPermissionsLinuxHelpText')}
                    helpTextWarning={translate(
                      'SetPermissionsLinuxHelpTextWarning'
                    )}
                    onChange={handleInputChange}
                    {...settings.setPermissionsLinux}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>{translate('ChmodFolder')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.UMASK}
                    name="chmodFolder"
                    helpText={translate('ChmodFolderHelpText')}
                    helpTextWarning={translate('ChmodFolderHelpTextWarning')}
                    onChange={handleInputChange}
                    {...settings.chmodFolder}
                  />
                </FormGroup>

                <FormGroup
                  advancedSettings={showAdvancedSettings}
                  isAdvanced={true}
                >
                  <FormLabel>{translate('ChownGroup')}</FormLabel>

                  <FormInputGroup
                    type={inputTypes.TEXT}
                    name="chownGroup"
                    helpText={translate('ChownGroupHelpText')}
                    helpTextWarning={translate('ChownGroupHelpTextWarning')}
                    onChange={handleInputChange}
                    {...settings.chownGroup}
                  />
                </FormGroup>
              </FieldSet>
            ) : null}
          </Form>
        ) : null}

        <FieldSet legend={translate('RootFolders')}>
          <RootFolders />
          <AddRootFolder />
        </FieldSet>
      </PageContentBody>
    </PageContent>
  );
}

export default MediaManagement;
