import React, { SyntheticEvent, useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import { icons } from 'Helpers/Props';
import styles from './MovieIndexPosterSelect.css';

interface MovieIndexPosterSelectProps {
  movieId: number;
  titleSlug: string;
  link?: string; // krzw(multi-edition): edition rows link by id, see getMoviePath
}

function MovieIndexPosterSelect({
  movieId,
  titleSlug,
  link = `/movie/${titleSlug}`, // krzw(multi-edition)
}: MovieIndexPosterSelectProps) {
  const [selectState, selectDispatch] = useSelect();
  const isSelected = selectState.selectedState[movieId];

  const onSelectPress = useCallback(
    (event: SyntheticEvent<HTMLElement, PointerEvent>) => {
      if (event.nativeEvent.ctrlKey || event.nativeEvent.metaKey) {
        window.open(`${window.Radarr.urlBase}${link}`, '_blank'); // krzw(multi-edition)
        return;
      }

      const shiftKey = event.nativeEvent.shiftKey;

      selectDispatch({
        type: 'toggleSelected',
        id: movieId,
        isSelected: !isSelected,
        shiftKey,
      });
    },
    [movieId, link, isSelected, selectDispatch] // krzw(multi-edition)
  );

  return (
    <Link className={styles.checkButton} onPress={onSelectPress}>
      <span className={styles.checkContainer}>
        <Icon
          className={isSelected ? styles.selected : styles.unselected}
          name={isSelected ? icons.CHECK_CIRCLE : icons.CIRCLE_OUTLINE}
          size={20}
        />
      </span>
    </Link>
  );
}

export default MovieIndexPosterSelect;
