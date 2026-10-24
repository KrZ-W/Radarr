import ModelBase from 'App/ModelBase';
import Language from 'Language/Language';
import { MovieFile } from 'MovieFile/MovieFile';
import Quality from 'Quality/Quality';

export type MovieMonitor = 'movieOnly' | 'movieAndCollection' | 'none';

export type MovieStatus =
  | 'tba'
  | 'announced'
  | 'inCinemas'
  | 'released'
  | 'deleted';

export type MovieAvailability = 'announced' | 'inCinemas' | 'released';

export type CoverType = 'poster' | 'fanart' | 'headshot';

export interface Image {
  coverType: CoverType;
  url: string;
  remoteUrl: string;
}

export interface Collection {
  tmdbId: number;
  title: string;
}

export interface Statistics {
  movieFileCount: number;
  releaseGroups: string[];
  movieFileQualities: Quality[];
  sizeOnDisk: number;
}

export interface RatingValues {
  votes: number;
  value: number;
}

export interface Ratings {
  imdb: RatingValues;
  tmdb: RatingValues;
  metacritic: RatingValues;
  rottenTomatoes: RatingValues;
  trakt: RatingValues;
}

export interface AlternativeTitle extends ModelBase {
  sourceType: string;
  title: string;
}

export interface MovieAddOptions {
  monitor: MovieMonitor;
  searchForMovie: boolean;
}

// krzw(multi-edition) begin: edition rows share one MovieMetadata and folder with the default row
export interface MovieEdition {
  name: string;
  aliases: string[];
  acceptUntagged: boolean;
}

export interface MovieEditionSummary {
  id: number;
  name: string;
  monitored: boolean;
  hasFile: boolean;
  qualityProfileId: number;
  movieFileId: number;
}
// krzw(multi-edition) end

interface Movie extends ModelBase {
  tmdbId: number;
  imdbId?: string;
  sortTitle: string;
  overview: string;
  youTubeTrailerId?: string;
  monitored: boolean;
  status: MovieStatus;
  title: string;
  titleSlug: string;
  originalTitle: string;
  originalLanguage: Language;
  collection: Collection;
  alternateTitles: AlternativeTitle[];
  studio: string;
  qualityProfileId: number;
  added: string;
  year: number;
  inCinemas?: string;
  physicalRelease?: string;
  digitalRelease?: string;
  releaseDate?: string;
  rootFolderPath: string;
  runtime: number;
  minimumAvailability: MovieAvailability;
  path: string;
  genres: string[];
  keywords: string[];
  ratings: Ratings;
  popularity: number;
  certification: string;
  statistics?: Statistics;
  tags: number[];
  images: Image[];
  movieFileId: number;
  movieFile?: MovieFile;
  hasFile: boolean;
  grabbed?: boolean;
  lastSearchTime?: string;
  isAvailable: boolean;
  isSaving?: boolean;
  addOptions: MovieAddOptions;
  edition?: MovieEdition | null; // krzw(multi-edition): null on the default row
  editionParentId?: number | null; // krzw(multi-edition): id of the default row, null on default rows
  editions?: MovieEditionSummary[]; // krzw(multi-edition): only populated on default rows
}

export default Movie;
