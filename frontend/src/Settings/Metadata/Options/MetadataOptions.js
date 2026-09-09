import PropTypes from 'prop-types';
import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

// Note: Do Not Translate Certification Countries

export const certificationCountryOptions = [
  { key: 'us', value: 'United States' },
  { key: 'au', value: 'Australia' },
  { key: 'br', value: 'Brazil' },
  { key: 'ca', value: 'Canada' },
  { key: 'fr', value: 'France' },
  { key: 'de', value: 'Germany' },
  { key: 'gb', value: 'Great Britain' },
  { key: 'in', value: 'India' },
  { key: 'ie', value: 'Ireland' },
  { key: 'it', value: 'Italy' },
  { key: 'nz', value: 'New Zealand' },
  { key: 'ro', value: 'Romania' },
  { key: 'es', value: 'Spain' }
];

function MetadataOptions(props) {
  const {
    isFetching,
    error,
    settings,
    hasSettings,
    onInputChange
  } = props;

  return (
    <>
      <FieldSet legend={translate('Options')}>
        {
          isFetching &&
            <LoadingIndicator />
        }

        {
          !isFetching && error &&
            <Alert kind={kinds.DANGER}>
              {translate('UnableToLoadIndexerOptions')}
            </Alert>
        }

        {
          hasSettings && !isFetching && !error &&
            <Form>
              <FormGroup>
                <FormLabel>{translate('CertificationCountry')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.SELECT}
                  name="certificationCountry"
                  values={certificationCountryOptions}
                  onChange={onInputChange}
                  helpText={translate('CertificationCountryHelpText')}
                  {...settings.certificationCountry}
                />
              </FormGroup>
            </Form>
        }
      </FieldSet>

      {/* krzw(imdb-title-provider): provider settings, stored alongside the metadata options */}
      <FieldSet legend={translate('ImdbTitleProvider')}>
        {
          hasSettings && !isFetching && !error &&
            <Form>
              <FormGroup>
                <FormLabel>{translate('ImdbTitleProviderEnabled')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name="imdbTitleProviderEnabled"
                  helpText={translate('ImdbTitleProviderEnabledHelpText')}
                  onChange={onInputChange}
                  {...settings.imdbTitleProviderEnabled}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('ImdbTitleProviderRegions')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="imdbTitleProviderRegions"
                  helpText={translate('ImdbTitleProviderRegionsHelpText')}
                  onChange={onInputChange}
                  {...settings.imdbTitleProviderRegions}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('ImdbTitleProviderLanguages')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.TEXT}
                  name="imdbTitleProviderLanguages"
                  helpText={translate('ImdbTitleProviderLanguagesHelpText')}
                  onChange={onInputChange}
                  {...settings.imdbTitleProviderLanguages}
                />
              </FormGroup>

              <FormGroup>
                <FormLabel>{translate('ImdbTitleProviderRefreshInterval')}</FormLabel>

                <FormInputGroup
                  type={inputTypes.NUMBER}
                  name="imdbTitleProviderRefreshInterval"
                  min={1}
                  unit={translate('Days')}
                  helpText={translate('ImdbTitleProviderRefreshIntervalHelpText')}
                  onChange={onInputChange}
                  {...settings.imdbTitleProviderRefreshInterval}
                />
              </FormGroup>
            </Form>
        }
      </FieldSet>
    </>
  );
}

MetadataOptions.propTypes = {
  isFetching: PropTypes.bool.isRequired,
  error: PropTypes.object,
  settings: PropTypes.object.isRequired,
  hasSettings: PropTypes.bool.isRequired,
  onInputChange: PropTypes.func.isRequired
};

export default MetadataOptions;
