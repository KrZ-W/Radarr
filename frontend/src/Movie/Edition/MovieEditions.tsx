// krzw(multi-edition): editions box on the movie details page (default row and its edition rows with monitor toggles, file status and links)
// krzw(edition-file-reassign): "move file here" on a sibling row without a file moves the current row's file to it
// krzw(edition-runtime-catalog): expected runtime shown next to the edition name
import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import FieldSet from 'Components/FieldSet';
import Label from 'Components/Label';
import Button from 'Components/Link/Button';
import IconButton from 'Components/Link/IconButton';
import ConfirmModal from 'Components/Modal/ConfirmModal';
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
import { updateItem } from 'Store/Actions/baseActions';
import { toggleMovieMonitored } from 'Store/Actions/movieActions';
import { fetchMovieFiles } from 'Store/Actions/movieFileActions';
import createAllMoviesSelector from 'Store/Selectors/createAllMoviesSelector';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import translate from 'Utilities/String/translate';
import { formatRuntimeMinutes } from './editionCatalog'; // krzw(edition-runtime-catalog)
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
  canReceiveFile: boolean;
  onMoveFileHerePress: (target: Movie) => void;
}

function editionName(movie: Movie) {
  return movie.edition ? movie.edition.name : translate('DefaultEdition');
}

function MovieEditionRow({
  movie,
  isCurrent,
  canReceiveFile,
  onMoveFileHerePress,
}: MovieEditionRowProps) {
  const dispatch = useDispatch();

  const {
    id,
    title,
    monitored,
    hasFile,
    qualityProfileId,
    isSaving = false,
    expectedRuntimeMinutes, // krzw(edition-runtime-catalog)
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

  const handleMoveFileHerePress = useCallback(() => {
    onMoveFileHerePress(movie);
  }, [movie, onMoveFileHerePress]);

  const name = editionName(movie);

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
        {/* krzw(edition-runtime-catalog) */}
        {expectedRuntimeMinutes == null ? null : (
          <span
            className={styles.expectedRuntime}
            title={translate('EditionExpectedRuntime')}
          >
            {formatRuntimeMinutes(expectedRuntimeMinutes)}
          </span>
        )}
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
        {canReceiveFile ? (
          <IconButton
            name={icons.MOVIE_FILE}
            title={translate('MoveFileToEdition', { edition: name })}
            onPress={handleMoveFileHerePress}
          />
        ) : null}

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
  const dispatch = useDispatch();
  const movie = useMovie(movieId);
  const allMovies = useSelector(createAllMoviesSelector());
  const [moveTarget, setMoveTarget] = useState<Movie | null>(null);
  const [isMoving, setIsMoving] = useState(false);

  const handleMoveFileCancel = useCallback(() => {
    setMoveTarget(null);
  }, []);

  const handleMoveFileConfirm = useCallback(() => {
    if (!movie || !moveTarget) {
      return;
    }

    setIsMoving(true);

    const { request } = createAjaxRequest({
      url: '/edition/movefile',
      method: 'POST',
      dataType: 'json',
      contentType: 'application/json',
      data: JSON.stringify({
        movieFileId: movie.movieFileId,
        movieId: moveTarget.id,
      }),
    });

    request.always(() => {
      [movie.id, moveTarget.id].forEach((id) => {
        createAjaxRequest({ url: `/movie/${id}` }).request.done(
          (data: Movie) => {
            dispatch(updateItem({ section: 'movies', ...data }));
          }
        );
      });

      dispatch(fetchMovieFiles({ movieId: movie.id }));
      setIsMoving(false);
      setMoveTarget(null);
    });
  }, [movie, moveTarget, dispatch]);

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
                canReceiveFile={
                  row.id !== movieId && movie.hasFile && !row.hasFile
                }
                onMoveFileHerePress={setMoveTarget}
              />
            ))}
          </TableBody>
        </Table>
      </div>

      <ConfirmModal
        isOpen={!!moveTarget}
        kind={kinds.PRIMARY}
        title={translate('MoveFileToEditionTitle')}
        message={
          moveTarget
            ? translate('MoveFileToEditionMessage', {
                from: editionName(movie),
                to: editionName(moveTarget),
              })
            : ''
        }
        confirmLabel={translate('MoveFile')}
        isSpinning={isMoving}
        onConfirm={handleMoveFileConfirm}
        onCancel={handleMoveFileCancel}
      />

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
