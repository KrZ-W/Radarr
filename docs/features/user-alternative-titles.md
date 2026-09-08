# User Alternative Titles

> **Status:** stable · **Since:** `v6.2.1.10461+krzw.4` (alt titles), `krzw.5` (translations) · **Surface:** API (`POST /api/v3/alttitle/user/import`, `POST /api/v3/translation/user/import`)

## What it does

Makes manually added alternative titles **survive metadata refreshes**, and adds a
bulk **import endpoint** that upserts titles from a curated JSON dataset as
`SourceType=User` rows. Once present, user titles behave exactly like TMDB titles
everywhere titles are matched: indexer search, release parsing, and file import
identification.

## Why it exists

TMDB is missing roughly 2,000 French/Quebec alternative titles for this library, so
French release names often failed to match their movie. Adding titles by hand was
futile: `RefreshMovieService` → `AlternativeTitleService.UpdateTitles` wiped and
rebuilt the entire alternative-titles set from TMDB on every refresh, destroying any
manually added rows. The `SourceType` enum (`Tmdb`/`Mappings`/`User`/`Indexer`)
existed upstream but nothing filtered on it.

## Behavior

### Refresh preservation

`UpdateTitles` now only reconciles `SourceType=Tmdb` rows against the fresh TMDB
data. Rows with any other source are preserved untouched — they are never deleted,
never updated, and can no longer be silently converted back to `Tmdb` when TMDB later
adds the same title (the incoming TMDB duplicate is skipped; the user row wins).

### Import endpoint

`POST /api/v3/alttitle/user/import` accepts the curated dataset format:

```json
[
  {
    "tmdbId": 194,
    "imdbId": "tt0211915",
    "movieTitle": "Amélie",
    "year": 2001,
    "missingFrenchTitles": [
      { "title": "Amélie de Montmartre", "region": "CA" }
    ]
  }
]
```

and returns a summary:

```json
{
  "moviesProcessed": 1,
  "titlesAdded": 1,
  "titlesSkipped": 0,
  "moviesNotFound": []
}
```

Rules:

- Movies are resolved by `tmdbId`, falling back to `imdbId`, against **library movies
  only** (no titles are attached to collection/import-list metadata rows). Unmatched
  entries are listed in `moviesNotFound` as `Title (Year) [tmdb:ID]`.
- **Idempotent:** titles already present for the movie (any source) are skipped, so
  the full dataset can be re-imported safely after adding new movies.
- A title equal to the movie's main title is skipped, as are titles already owned by
  a *different* movie (release-to-movie matching is by clean title globally, same
  guard as the TMDB refresh path).
- `region` is accepted for dataset compatibility but **not stored** — the
  `AlternativeTitles` table has no region column and adding one would require a
  migration.

## Usage

```bash
curl -X POST "http://<host>:7878/api/v3/alttitle/user/import" \
  -H "X-Api-Key: <api-key>" \
  -H "Content-Type: application/json" \
  -d @curated.json
```

Verify per movie with `GET /api/v3/alttitle?movieId=<id>` — user rows report
`"sourceType": "user"`.

## Edge cases

- Deleting a movie still deletes **all** its alternative titles, user rows included.
- User titles are per-`MovieMetadata`; re-adding a previously removed movie requires
  re-importing (idempotency makes a full-dataset re-run the easy answer).
- The endpoint does not update or delete existing user rows; to correct a bad title,
  remove the row and re-import.

## Phase 1b: regional titles as user translations

Alt titles make releases *parse and import*, but production search
(`RegionalTranslationSearchMode=OnePerRegion`) builds queries from
**MovieTranslations** only — `AllTitles` mode (which folds in alt titles) is
unusable against rate-limited indexers. Phase 1b makes curated regional titles
first-class translations so the existing search machinery emits them with zero
search-code changes.

### What it adds

- `MovieTranslations.SourceType` column (migration 244; `0=Tmdb`, same enum as alt
  titles). The translations refresh now reconciles only TMDB-sourced rows: user rows
  survive refreshes, and an incoming TMDB title duplicating a preserved row is
  dropped (the user row wins) — identical semantics to the alt-titles preservation.
- `POST /api/v3/translation/user/import` — same envelope and summary response as
  the alt-titles importer; title entries use **standard identifiers**:
  `{title, language?, region?}` with `language` an ISO 639-1 code (defaults to `fr`
  for the curated dataset) and `region` an ISO 3166-1 alpha-2 code. ISO 639-2 codes
  (`fra`) and full tags (`fr-CA`) are accepted and canonicalised. The stored
  `RegionalLanguage` tag is built uniformly — `{language}` or
  `{language}-{region}`, lowercase two-letter language, the same shape SkyHook
  stores for TMDB rows —
  so any language's titles can be imported (`{language: "de", region: "AT"}` →
  German, `de-at`). Unknown language codes are skipped and counted. One row per
  title.

  **Dataset note:** entries should carry `region` only when the region matters for
  search. `region: "CA"` → `fr-ca` (matches a `fr-CA` variants entry); an entry
  with **no region** → bare `fr` (always searched). A France entry written as
  `region: "FR"` produces `fr-fr`, which the variants filter drops unless `fr-FR`
  is in Regional Translation Variants — for the curated file, strip the region
  from France rows (or add `fr-FR` to the variants list).
- Guards: tmdbId→imdbId resolution, library movies only, idempotent (skips titles
  already in the movie's translations, any source), main-title skip, and the global
  cross-movie clean-title guard via `FindByTitleCandidates` (sweeps movie titles,
  alt titles, and translations — the parser maps releases across all three).

### Interplay with the alt-titles importer

Both endpoints stay. Import the same dataset into both: alt titles cover
parse/import matching for regionless strings; translations make the regional
titles searchable. A title already present as the movie's *alt title* is **not**
skipped by the translation importer — that overlap is intended, since prod
already carries the dataset as alt titles.

### Known edges

- If TMDB later ships its own `fr-CA` translation with a *different* title for a
  movie, `OnePerRegion` picks by DB row order (the TMDB row, inserted first, wins
  the region slot). The curated set targets titles TMDB lacks, so this stays
  theoretical; revisit if it bites.
- User translations participate in the deterministic translation pick for renaming
  and NFO metadata: with `fr-CA` in Regional Translation Variants, a user QC title
  can become `{Movie TranslatedTitle}`. That is first-class-translation semantics,
  not a bug.

## Source

Commit: `86aa71646`. Key files:
`Movies/AlternativeTitles/AlternativeTitleService.cs` (refresh preservation +
`UpsertUserTitles`), `Radarr.Api.V3/Movies/AlternativeTitleController.cs` (endpoint),
`Radarr.Api.V3/Movies/UserAlternativeTitleImportResource.cs` (DTOs).

Phase 1b commits: `4f2c02074`, `76dc748a5`. Key files:
`Movies/Translations/MovieTranslationService.cs` (preservation +
`UpsertUserTranslations`), `Radarr.Api.V3/Movies/UserTranslationController.cs`
(endpoint + `UserTranslationMapper`),
`Datastore/Migration/244_add_source_type_to_movie_translations.cs`.

Review fixes: `f2ec81c7e` (shared `UserTitleImportGuard` across both importers),
`57350200f` (canonical language code in the stored tag).
