// krzw(multi-edition): editions box on the movie details page (default row and its edition rows with monitor toggles, file status and links)
import React, { useCallback, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import FieldSet from 'Components/FieldSet';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import MonitorToggleButton from 'Components/MonitorToggleButton';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons, kinds } from 'Helpers/Props';
import Movie from 'Movie/Movie';
import useMovie from 'Movie/useMovie';
import QualityProfileName from 'Settings/Profiles/Quality/QualityProfileName';
import { toggleMovieMonitored } from 'Store/Actions/movieActions';
import createAllMoviesSelector from 'Store/Selectors/createAllMoviesSelector';
import translate from 'Utilities/String/translate';
import getMoviePath from './getMoviePath';
import styles from './MovieEditions.css';

const columns: Column[] = [
  {
    name: 'monitored',
    label: '',
    isVisible: true,
  },
  {
    name: 'edition',
    label: () => translate('Edition'),
    isVisible: true,
  },
  {
    name: 'status',
    label: () => translate('Status'),
    isVisible: true,
  },
  {
    name: 'qualityProfileId',
    label: () => translate('QualityProfile'),
    isVisible: true,
  },
  {
    name: 'actions',
    label: '',
    isVisible: true,
  },
];

function sortEditionRows(a: Movie, b: Movie) {
  if (!a.edition) {
    return -1;
  }

  if (!b.edition) {
    return 1;
  }

  return a.edition.name.localeCompare(b.edition.name);
}

interface MovieEditionRowProps {
  movie: Movie;
  isCurrent: boolean;
}

function MovieEditionRow({ movie, isCurrent }: MovieEditionRowProps) {
  const dispatch = useDispatch();

  const {
    id,
    title,
    monitored,
    hasFile,
    qualityProfileId,
    edition,
    isSaving = false,
  } = movie;

  const handleMonitorTogglePress = useCallback(
    (value: boolean) => {
      dispatch(
        toggleMovieMonitored({
          movieId: id,
          monitored: value,
        })
      );
    },
    [id, dispatch]
  );

  const name = edition ? edition.name : translate('DefaultEdition');

  return (
    <TableRow>
      <TableRowCell className={styles.monitored}>
        <MonitorToggleButton
          monitored={monitored}
          isSaving={isSaving}
          size={20}
          onPress={handleMonitorTogglePress}
        />
      </TableRowCell>

      <TableRowCell>
        <span className={isCurrent ? styles.current : undefined}>{name}</span>
      </TableRowCell>

      <TableRowCell>
        <Label kind={hasFile ? kinds.SUCCESS : kinds.DANGER}>
          {hasFile ? translate('Downloaded') : translate('Missing')}
        </Label>
      </TableRowCell>

      <TableRowCell>
        <QualityProfileName qualityProfileId={qualityProfileId} />
      </TableRowCell>

      <TableRowCell className={styles.actions}>
        {isCurrent ? null : (
          <IconButton
            name={icons.ARROW_RIGHT}
            title={translate('MovieDetailsGoTo', {
              title: `${title} [${name}]`,
            })}
            to={getMoviePath(movie)}
          />
        )}
      </TableRowCell>
    </TableRow>
  );
}

interface MovieEditionsProps {
  movieId: number;
  onAddEditionPress: () => void;
}

function MovieEditions({ movieId, onAddEditionPress }: MovieEditionsProps) {
  const movie = useMovie(movieId);
  const allMovies = useSelector(createAllMoviesSelector());

  const rows = useMemo(() => {
    if (!movie) {
      return [];
    }

    return allMovies
      .filter((m) => m.tmdbId === movie.tmdbId)
      .sort(sortEditionRows);
  }, [allMovies, movie]);

  if (!movie) {
    return null;
  }

  const isEdition = !!movie.edition;

  if (!isEdition && rows.length < 2) {
    return null;
  }

  return (
    <FieldSet legend={translate('Editions')}>
      <div className={styles.container}>
        <Table columns={columns}>
          <TableBody>
            {rows.map((row) => (
              <MovieEditionRow
                key={row.id}
                movie={row}
                isCurrent={row.id === movieId}
              />
            ))}
          </TableBody>
        </Table>
      </div>

      {isEdition ? null : (
        <div className={styles.addButtonContainer}>
          <Button kind={kinds.PRIMARY} onPress={onAddEditionPress}>
            {translate('AddEdition')}
          </Button>
        </div>
      )}
    </FieldSet>
  );
}

export default MovieEditions;
