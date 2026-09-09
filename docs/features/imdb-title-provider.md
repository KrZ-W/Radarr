# IMDb Title Provider

> **Status:** stable · **Since:** `v6.3.0.10514+krzw.5` · **Surface:** Settings → Metadata → *IMDb Title Provider*, scheduled task `ImdbTitleDatasetRefresh` (also `POST /api/v3/command {"name":"ImdbTitleDatasetRefresh"}`), health check

## What it does

Downloads IMDb's public *title.akas* dataset on a schedule, keeps the rows for the
regions/languages you care about in a local SQLite index, and adds the titles your
movies are missing as **user alternative titles and user translations** through the
[User Alternative Titles & Translations](user-alternative-titles.md) pipeline. A movie
added while the feature is on gets its titles within seconds; the whole library is
re-checked after every dataset refresh.

This replaces the external Python feeder (`build_index.py` / `title_sync.py` /
`title_webhook.py`) that used to do the same job from outside the container.

## Why it exists

TMDB lacks roughly 2,000 French/Quebec titles for this library; IMDb has most of them.
The curated-dataset import endpoints solved the *storage* side (user rows survive
refreshes and feed parsing/search), but producing the dataset still meant running
scripts against a 2 GB dump, a webhook receiver for new movies and a cron entry. With
the provider built in, the only moving part is a setting.

## Behavior

### Dataset refresh (scheduled task)

- Runs every *Refresh Interval* days (default 7, minimum 1) while the feature is
  enabled; the task shows in *System → Tasks* and can be run manually. Disabling the
  feature sets the task interval to 0 (never scheduled) on the next settings save.
- Downloads `https://datasets.imdbws.com/title.akas.tsv.gz` to
  `<AppData>/imdb-akas.db.download` with `If-None-Match` / `If-Modified-Since` taken
  from the previous build. A `304 Not Modified` (or an unchanged `ETag`) skips the
  rebuild. Changing *Regions* or *Languages* forces a full download.
- Streams the gzip TSV line by line (the full dump is never held in memory) and writes
  only the kept rows to `<AppData>/imdb-akas.db.tmp`, then renames it over
  `<AppData>/imdb-akas.db`. A failed download or parse leaves the previous index in
  place and is logged as an error.
- A row is kept when `region ∈ Regions` **or** `language ∈ Languages`, unless its
  `attributes` contain `literal` (literal translations are never release names). No
  other attribute-based exclusion. Row counts (scanned / kept) are logged at Info.
- After the task (downloaded or not) every library movie with an IMDb id is synced.

### Applying titles

For one movie:

1. Read its rows from the index.
2. Normalise every title the way the reference feeder did (NFD, strip combining marks,
   lowercase, non-alphanumerics folded to single spaces) and drop rows whose
   normalised title equals the movie's title, original title, or an existing row of
   the *target table* (alt titles for the alt-title request, translations for the
   translation request). Duplicate dataset rows collapse to the first one.
3. Build one `UserTitleImportRequest` per target and hand it to
   `IUserTitleImportService.ImportAlternativeTitles` / `ImportTranslations`. Nothing is
   called when nothing is missing. All guards (cross-movie clean-title guard,
   idempotency, unknown language) stay in the import service.

Entry fields:

- `language`: the row's language when the dataset has one, otherwise the **first
  configured language** (`fr` by default; region rows are mostly language-less).
- `region`: kept **only when `{language}-{region}` is listed in *Regional Translation
  Variants*** (Media Management). With the default `fr-CA,…` list a Quebec row stores
  `fr-ca` and a France row stores bare `fr`, which is exactly the "Dataset note" rule
  from the user-titles page — a `fr-fr` row would otherwise be dropped by the variants
  filter and never searched.

Triggers:

| When | Scope |
|---|---|
| `ImdbTitleDatasetRefresh` task finished | all library movies with an IMDb id |
| `MovieAddedEvent` | that movie (before its first TMDB refresh; the refresh then keeps the user rows and skips TMDB duplicates) |
| `MovieUpdatedEvent` (end of a movie's metadata refresh) | that movie |

Every trigger is a no-op while the feature is disabled or the index does not exist.

### Health check

While enabled: **warning** when `imdb-akas.db` is missing/unreadable ("run the task"),
or when it was built more than *2 × Refresh Interval* days ago (a scheduled refresh has
been failing). Re-evaluated on settings save, after every task run and on the regular
health check schedule.

## Usage

1. *Settings → Media Management → Regional Translation Variants* must contain the tags
   you want regional titles stored under (`fr-CA` is in the default list).
2. *Settings → Metadata → IMDb Title Provider*: tick **Enable**, keep `CA,FR` / `fr` or
   adjust, save.
3. *System → Tasks → Imdb Title Dataset Refresh → run*. The first run downloads
   ~300 MB and takes a few minutes; watch the log for `IMDb akas dataset indexed` and
   `IMDb titles applied to library`.
4. Verify on a movie: `GET /api/v3/alttitle?movieId=<id>` shows the new rows with
   `"sourceType": "user"`, and `GET /api/v3/parse?title=<French release name>` resolves
   to it.

From then on nothing else is needed. To stop: untick **Enable** — the task stops being
scheduled, the event hooks become no-ops, and existing user rows stay (remove them
through the alt-title/translation endpoints if you want them gone).

## Edge cases

- **A row with a language that is not configured** (e.g. `region=CA, language=en`)
  is still kept because of its region and imported under *its own* language (`en`,
  region kept only if `en-CA` is a variant). The reference feeder tagged every row
  as French; this is more accurate but means non-French Canadian titles also land
  as alt titles. Narrow *Regions* if you do not want that.
- **Region rows without a language** are attributed to the first configured language.
  With `Regions=CA,FR` and `Languages=fr` that is the intended reading; with several
  languages configured, put the one that dominates your regions first.
- **Which duplicate wins:** two dataset rows normalising to the same text keep the
  first in dataset order (usually the lowest `ordering`), even if a later one carries
  a region you would prefer.
- The index is a cache, not part of `radarr.db`: it is not backed up, not migrated and
  can be deleted at any time (the next task run rebuilds it, the health check flags it
  meanwhile).
- A movie whose IMDb id is empty is skipped entirely; there is no TMDB→IMDb lookup here.
- Nothing is ever *removed* by this feature. A title that disappears from IMDb, or a
  row you delete by hand, is not re-added unless IMDb still lists it.

## Dataset licence

The IMDb datasets are made available by IMDb for **personal and non-commercial use**
only (see <https://developer.imdb.com/non-commercial-datasets/>). This fork downloads
the dataset at runtime on the operator's own instance, stores only a filtered index
locally and **never redistributes** any part of it; the container image contains no
IMDb data. Using this feature means accepting IMDb's terms yourself.

## Architecture

| Piece | Responsibility |
|---|---|
| `Movies/ImdbTitles/ImdbAkasFilter` | which rows to keep (regions ∪ languages, minus `literal`), list parsing, default language, filter signature |
| `Movies/ImdbTitles/ImdbAkasTsvParser` | streaming gzip/TSV reader → `ImdbAkasRow`, `\N` → null, row counts |
| `Movies/ImdbTitles/ImdbAkasDatabase` | the SQLite index (`titles` + `meta` tables), atomic `.tmp` → live rename, per-movie lookup |
| `Movies/ImdbTitles/ImdbTitleDatasetRefreshService` | the task: conditional download, parse, build, event, library sync |
| `Movies/ImdbTitles/ImdbTitleSyncService` | per-movie candidate building (normalisation, region rule, dedup), event handlers, calls into `IUserTitleImportService` |
| `Movies/ImdbTitles/ImdbTitleNormalizer` | the "already has" key |
| `HealthCheck/Checks/ImdbTitleDatasetCheck` | missing / stale index warning |
| `Jobs/TaskManager` (marked hunk) | scheduled-task registration and interval update on settings save |
| `Configuration/ConfigService`, `Radarr.Api.V3/Config/MetadataConfig*`, `frontend/src/Settings/Metadata/Options/MetadataOptions.js` | the four settings |

Nothing in TMDB fetching, the import endpoints or release matching is touched; the
feature is purely a producer of `UserTitleImportRequest`s.

## Relationship to the Sonarr fork

The Sonarr fork has the same provider keyed on the series IMDb id and feeding the
[user scene-mapping importer](https://github.com/KrZ-W/Sonarr/blob/personal/all-features-main/docs/features/user-scene-mappings.md)
instead (Sonarr searches by scene mapping, so no translation step exists there).

## Upstream

No upstream Radarr issue asks for IMDb-sourced titles; the closest are the alternate-title
threads listed under [User Alternative Titles](user-alternative-titles.md#upstream)
([Radarr#11443](https://github.com/Radarr/Radarr/issues/11443),
[Radarr#11373](https://github.com/Radarr/Radarr/issues/11373)). Upstream sources titles
from TMDB only. Not submitted upstream.

## Source

Branch `feature/imdb-title-provider-master`. Key files: `src/NzbDrone.Core/Movies/ImdbTitles/*`,
`src/NzbDrone.Core/HealthCheck/Checks/ImdbTitleDatasetCheck.cs`,
`src/NzbDrone.Core/Jobs/TaskManager.cs`, `src/Radarr.Api.V3/Config/MetadataConfigController.cs`,
`frontend/src/Settings/Metadata/Options/MetadataOptions.js`. Tests:
`NzbDrone.Core.Test/MovieTests/ImdbTitleTests/*`,
`NzbDrone.Core.Test/HealthCheck/Checks/ImdbTitleDatasetCheckFixture.cs`.
