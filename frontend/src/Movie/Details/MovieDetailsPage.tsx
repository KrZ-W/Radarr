import React, { useEffect, useMemo } from 'react';
import { useSelector } from 'react-redux';
import { useHistory, useParams } from 'react-router';
import NotFound from 'Components/NotFound';
import usePrevious from 'Helpers/Hooks/usePrevious';
import createAllMoviesSelector from 'Store/Selectors/createAllMoviesSelector';
import translate from 'Utilities/String/translate';
import MovieDetails from './MovieDetails';

function MovieDetailsPage() {
  const allMovies = useSelector(createAllMoviesSelector());
  // krzw(multi-edition) begin: /movie/:titleSlug resolves to the default row, /movie/:titleSlug/edition/:editionId to an edition row
  const { titleSlug, editionId } = useParams<{
    titleSlug: string;
    editionId?: string;
  }>();
  const history = useHistory();

  const movieIndex = useMemo(() => {
    const candidates = allMovies.reduce((acc: number[], movie, index) => {
      if (movie.titleSlug === titleSlug) {
        acc.push(index);
      }

      return acc;
    }, []);

    if (editionId) {
      const id = parseInt(editionId);

      return candidates.find((index) => allMovies[index].id === id) ?? -1;
    }

    return (
      candidates.find((index) => !allMovies[index].edition) ??
      candidates[0] ??
      -1
    );
  }, [allMovies, titleSlug, editionId]);
  // krzw(multi-edition) end

  const previousIndex = usePrevious(movieIndex);

  useEffect(() => {
    if (
      movieIndex === -1 &&
      previousIndex !== -1 &&
      previousIndex !== undefined
    ) {
      history.push(`${window.Radarr.urlBase}/`);
    }
  }, [movieIndex, previousIndex, history]);

  if (movieIndex === -1) {
    return <NotFound message={translate('MovieCannotBeFound')} />;
  }

  return <MovieDetails movieId={allMovies[movieIndex].id} />;
}

export default MovieDetailsPage;
