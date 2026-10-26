import React from 'react';
import Label from 'Components/Label';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import EditionLabel from 'Movie/Edition/EditionLabel'; // krzw(multi-edition)
import { MovieEdition } from 'Movie/Movie'; // krzw(multi-edition)
import styles from './SelectMovieRow.css';

interface SelectMovieRowProps {
  title: string;
  tmdbId: number;
  imdbId?: string;
  year: number;
  edition?: MovieEdition | null; // krzw(multi-edition)
}

function SelectMovieRow({
  title,
  year,
  tmdbId,
  imdbId,
  edition, // krzw(multi-edition)
}: SelectMovieRowProps) {
  return (
    <>
      <VirtualTableRowCell className={styles.title}>
        {title}
        <EditionLabel edition={edition} /> {/* krzw(multi-edition) */}
      </VirtualTableRowCell>

      <VirtualTableRowCell className={styles.year}>{year}</VirtualTableRowCell>

      <VirtualTableRowCell className={styles.imdbId}>
        {imdbId ? <Label>{imdbId}</Label> : null}
      </VirtualTableRowCell>

      <VirtualTableRowCell className={styles.tmdbId}>
        <Label>{tmdbId}</Label>
      </VirtualTableRowCell>
    </>
  );
}

export default SelectMovieRow;
