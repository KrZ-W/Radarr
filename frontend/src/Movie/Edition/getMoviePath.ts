// krzw(multi-edition): details-page path for any movie row (edition rows share the default row's titleSlug)
import { MovieEdition } from 'Movie/Movie';

export interface MoviePathSource {
  titleSlug: string;
  id?: number;
  edition?: MovieEdition | null;
}

export default function getMoviePath({
  titleSlug,
  id,
  edition,
}: MoviePathSource) {
  if (edition && id) {
    return `/movie/${titleSlug}/edition/${id}`;
  }

  return `/movie/${titleSlug}`;
}
