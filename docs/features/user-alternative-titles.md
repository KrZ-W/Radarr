# User Alternative Titles

> **Status:** stable · **Since:** unreleased (next release on `6.2.1.10461` base) · **Surface:** API (`POST /api/v3/alttitle/user/import`)

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
      { "title": "Amélie de Montmartre", "region": "QC" }
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

## Source

Commit: `09477768b`. Key files:
`Movies/AlternativeTitles/AlternativeTitleService.cs` (refresh preservation +
`UpsertUserTitles`), `Radarr.Api.V3/Movies/AlternativeTitleController.cs` (endpoint),
`Radarr.Api.V3/Movies/UserAlternativeTitleImportResource.cs` (DTOs).
