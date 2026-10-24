// krzw(multi-edition): badge rendered next to a movie title when the row is an edition
import classNames from 'classnames';
import React from 'react';
import Label from 'Components/Label';
import { kinds, sizes } from 'Helpers/Props';
import { Size } from 'Helpers/Props/sizes';
import { MovieEdition } from 'Movie/Movie';
import translate from 'Utilities/String/translate';
import styles from './EditionLabel.css';

interface EditionLabelProps {
  className?: string;
  edition?: MovieEdition | null;
  size?: Extract<Size, 'small' | 'medium' | 'large'>;
}

function EditionLabel({
  className,
  edition,
  size = sizes.SMALL,
}: EditionLabelProps) {
  if (!edition || !edition.name) {
    return null;
  }

  return (
    <Label
      className={classNames(styles.editionLabel, className)}
      kind={kinds.INFO}
      size={size}
      title={translate('Edition')}
    >
      {edition.name}
    </Label>
  );
}

export default EditionLabel;
