import React from 'react';
import Link, { LinkProps } from 'Components/Link/Link';
import EditionLabel from 'Movie/Edition/EditionLabel'; // krzw(multi-edition)
import getMoviePath from 'Movie/Edition/getMoviePath'; // krzw(multi-edition)
import { MovieEdition } from 'Movie/Movie'; // krzw(multi-edition)

interface MovieTitleLinkProps extends LinkProps {
  titleSlug: string;
  title: string;
  year?: number;
  movieId?: number; // krzw(multi-edition): needed to link edition rows, which share the titleSlug
  edition?: MovieEdition | null; // krzw(multi-edition)
}

function MovieTitleLink({
  titleSlug,
  title,
  year = 0,
  movieId, // krzw(multi-edition)
  edition, // krzw(multi-edition)
  ...otherProps
}: MovieTitleLinkProps) {
  const link = getMoviePath({ titleSlug, id: movieId, edition }); // krzw(multi-edition)

  return (
    // krzw(multi-edition) begin: edition badge after the title
    <>
      <Link to={link} title={title} {...otherProps}>
        {title}
        {year > 0 ? ` (${year})` : ''}
      </Link>

      <EditionLabel edition={edition} />
    </>
    // krzw(multi-edition) end
  );
}

export default MovieTitleLink;
