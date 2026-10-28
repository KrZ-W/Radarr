# Multi-Edition Movies

> **Status:** pilot (slice 1) · **Since:** `v6.4.4.10685+krzw.2` · **Surface:** movie page (*Editions* box, *Add Edition*, move file to edition), edit-movie modal, naming token `{Edition Name}`, `/api/v3/movie`, `/api/v3/calendar`, `/api/v3/edition` (audit and move file since `v6.4.4.10685+krzw.3`) · **Design:** [design/multi-edition.md](../design/multi-edition.md)

## What it does

Lets one movie hold several **editions**: the theatrical cut *and* the Extended cut, the
Director's Cut, a Criterion release, and so on. Each edition is a Movie row of its own, with
its own monitored flag, quality profile, file, history, queue entries and searches, so each
cut is grabbed, upgraded and tracked independently. All editions of a title share the
default row's TMDb metadata, its root folder and its **movie folder**: the files sit side by
side in one directory, which is what Plex expects for its `{edition-Name}` tag.

The row a movie had before this feature is its **default edition** (`edition: null` in the
API, "Default" in the UI). Nothing changes for a movie until you add an edition to it: the
default edition keeps today's behaviour and accepts any release, tagged or not.

### Presets

The add-edition dialog offers these names, each with the release spellings that map to it
(all comparisons ignore case, apostrophes, dots, dashes, underscores and brackets):

Theatrical · Extended (`extended cut`, `extended edition`) · Director's Cut
(`director's edition`, `directors cut`, `dc`) · Unrated · Final Cut · Ulysses Cut · IMAX ·
Criterion (`criterion collection`) · Remastered · Special Edition · Ultimate Edition ·
Ultimate Cut · Open Matte · Uncut · Anniversary Edition · Assembly Cut · Restored
(Director's Cut also matches a bare `directors`)

Pick *Custom* for anything else (32 characters at most, letters or digits required). Aliases
are free text; the name itself always counts as an alias.

## How to add an edition

**UI.** Open the movie, press **Add Edition** (toolbar, or the button under the *Editions*
box once a first edition exists). Choose a preset or a custom name, adjust the aliases, pick
the quality profile (defaults to the parent's), monitored, **Accept releases without an
edition tag** (off by default, see below) and whether to search right away. The new row
appears in the *Editions* box with its monitor toggle, status and quality profile, and as a
card of its own in the movie index, marked with an edition badge. Its page is
`/movie/<tmdbId>/edition/<id>`.

**API.** `POST /api/v3/movie` with the movie's `tmdbId`, the usual `qualityProfileId`,
`monitored`, `minimumAvailability`, `tags`, `addOptions`, plus

```json
"edition": { "name": "Extended", "aliases": ["extended cut", "extended edition"], "acceptUntagged": false }
```

The movie must already be in the library (its default row is looked up by `tmdbId`);
`path` and `rootFolderPath` in the request are ignored and copied from the default row. The
answer is the new row (`201`). A duplicate name or alias of an existing edition of the same
title is a validation error.

Editing: `PUT /api/v3/movie/{id}` on an edition row may change `edition.name`,
`edition.aliases`, `edition.acceptUntagged`, monitored, profile and tags, but **not the path**
(validation error: the path follows the default edition). The edit-movie modal shows the
edition fields and a read-only path for such rows. Moving the default edition (root folder
change, path edit, bulk move) moves the shared folder once and the edition rows follow.

Deleting: an edition row deletes only itself and, with *delete files*, only its own movie
file and the subtitles / extras linked to it (recycle bin); the folder stays with the other
editions. The **default row cannot be deleted while it still has editions** (`400 Cannot
delete ...: its Extended edition(s) must be deleted first`); delete the editions first, or
select the whole family in the bulk editor.

## How releases and files find their edition

1. The **edition is parsed** from the release title (existing parser, now also `Ulysses
   Cut`, `Criterion`, `Criterion Collection`) or, for files, from a Plex `{edition-Name}` tag
   in the file or folder name (highest priority), else from the download-client title,
   folder name and file name as before.
2. **Routing.** A release of a title with editions goes to the edition whose name or alias it
   carries (a row named exactly wins over one matched by containment, so `Extended
   Remastered` reaches an *Extended Remastered* row next to an *Extended* one, while plain
   `Extended Remastered` without such a row still reaches *Extended*). An unknown or missing
   edition goes to the default row. Rows are chosen whether or not they are monitored, so an
   unmonitored edition's releases are rejected there instead of leaking to the default row.
3. **Gate.** On the chosen row the usual profile, cutoff and custom-format rules apply, plus
   an edition gate: the default row refuses a release whose edition belongs to a sibling
   edition (`Release edition 'Extended' belongs to the Extended edition of this movie`); an
   edition row accepts only releases naming it, or untagged releases when *Accept releases
   without an edition tag* is on (`Release has no edition tag and the Extended edition does
   not accept untagged releases`, `Release edition 'Unrated' does not match the Extended
   edition`). The same gate runs at import.
4. **A download stays with the row it was grabbed for.** The grab history binds the download
   to its edition even when the torrent, folder or file name drops the edition word, and even
   when the client title carries none: the queue shows it under that edition and the file
   imports there. Only a file that explicitly names a *sibling* edition is still refused.
5. **Searches** (missing, cutoff unmet, interactive) run per row with the shared titles, so
   searching the Extended row returns everything and the gate keeps what fits. During a
   search for an edition every release of the title is judged for that row.
6. **Manual import.** The row you pick wins (the edition gate trusts a user pick). Without a
   pick, folders and files route by their names; a folder holding both cuts is split per file
   by the `{edition-X}` tag or edition words; an untagged folder of a tracked download goes to
   the row it was grabbed for. Only changing the movie counts as a pick: editing a row's
   quality, languages, release group or flags re-evaluates it with the preselected row and
   keeps its edition verdict (the reprocess request carries `editionOverride`).

### Shared folder

Each edition only ever sees its own file: the other editions' files are neither listed as
unmapped in *Manage Files* nor re-imported by a rescan, and the import gate refuses a sibling's
tagged file on rescans too. Extras (subtitles, `-thumb` images, `.nfo`) attach to the movie
file whose name they start with; folder-level extras (`poster.jpg`, `fanart.jpg`) stay with the
default edition. Upgrades compare only against the same row's file.

## Existing files: audit and move to an edition

Upgrading changes nothing on disk: every existing movie becomes a default row and keeps its
file, whatever cut that file is. Two tools sort that out after the fact.

**Audit.** `GET /api/v3/edition/audit` lists every default-row file that carries an edition,
read from a Plex `{edition-X}` tag in the file name, else from the edition parsed from the
release name at import. Each item has `kind`:

| Kind | Meaning | Usual action |
|---|---|---|
| `cut` | Extended, Unrated, Director's Cut, Uncut, Final Cut, Special/Ultimate/Anniversary, Assembly Cut, Ulysses Cut | add that edition and move the file to it, or leave it if this cut is the one you want as default |
| `theatrical` | the file already is the theatrical cut | none |
| `variant` | Remastered, IMAX, Restored, Open Matte, Criterion: a release of the same cut | usually none |
| `unrecognised` | an edition string no preset matches | look at it |

`suggestedEdition` names the preset, `ambiguous` is set when several presets match
(`Extended Director's Cut`), and `editionRowId` / `editionRowHasFile` point at an edition row
that already claims the file. Export as CSV:

```bash
curl -s -H "X-Api-Key: $KEY" http://radarr:7878/api/v3/edition/audit | jq -r \
  '(["kind","suggestedEdition","ambiguous","fileEdition","title","year","movieId","movieFileId"]|@csv),
   (.[]|[.kind,.suggestedEdition,.ambiguous,.fileEdition,.title,.year,.movieId,.movieFileId]|@csv)'
```

**Move a file to an edition.** On the movie page, the *Editions* box shows a file icon on every
other row of the title that has no file: it moves the current row's file there. The API is
`POST /api/v3/edition/movefile` with `{"movieFileId": 2405, "movieId": 3272}`. The move:

- repoints the file and its subtitles, metadata and other extras to the target row;
- clears the source row's file (it shows as missing, and is searched if monitored) and sets
  the target row's;
- queues a rename of that file when renaming is enabled, so it gains or loses its
  `{edition-…}` tag (extras are renamed with it);
- keeps history where it was: past grabs and imports stay on the source row.

It is refused (`400`) when the target belongs to another movie, already has a file, or already
owns this file. Moving back to the default row works the same way.

## Runtime and the edition catalog

Tags only say what the release name claimed. The runtime of the file says which cut it is,
once Radarr knows how long each cut of the title runs. That knowledge is the **edition
catalog**: per title, a list of `{name, runtimeMinutes, source, confidence}` entries.

### Where the catalog comes from

| Source | What it holds | Confidence |
|---|---|---|
| `user` | entries typed through the API (`POST /api/v3/edition/catalog`) or imported with `source: "user"` | 100 |
| `library` | confirmed on this instance: the audit's *Apply* (90), an import into an edition row whose cut the catalog did not know (60, only when the file matches the row's expected runtime, if one is set), or imported from another instance (90) | 60–90 |
| `imdb` | every runtime IMDb lists for the title. The first unlabelled one is *Theatrical* (80); labelled ones go through a label table (*director's cut* → Director's Cut, *extended cut* → Extended, *unrated*, *uncut*, *final cut*, *special edition*, *workprint*...; 70, or 60 when the label has extra words, e.g. *Blu Ray Extended Edition*). Transfer and market labels (*TV*, *DVD*, *Germany*, *edited*, *re-release*...) are kept at 20 and never decide anything | 20–80 |
| `wikidata` | durations (P2047) qualified with *applies to part* (P518), e.g. *extended cut*; unlabelled durations are ignored; hours and seconds are converted | 60–80 |
| `tmdb` | the movie's TMDb runtime as entry *Default* (30), or 0 with the attribute `suspect` when it differs from IMDb's theatrical runtime by more than 50 % | 0 / 30 |

Merging ranks `user` > `library` > `imdb` > `wikidata` > `tmdb`, then confidence. Two entries
with the same cut name within the tolerance collapse into the stronger one; the same name
further apart stays twice (two releases of one cut, e.g. Fellowship's Extended at 208 and
228 min). This also holds within one source: a refresh, an import or a user entry stores the
same name again when its runtime is further than the tolerance from the stored one, and
updates the stored one otherwise. Matching counts such a cut once, so a 228-min file is a
unique *Extended* match although Extended is also known at 208. Entries below confidence 30
are shown but never take part in matching. An edition row's **expected runtime** (`expectedRuntimeMinutes`,
set in Add Edition or Edit Movie) counts as an entry of that row's cut too.

**Refresh.** After every movie refresh, Radarr queues `RefreshEditionCatalog` for the same
movies (all movies after a full refresh). It only re-asks titles whose catalog is older than
30 days: IMDb in batches of 50 ids, 1.5 s apart, Wikidata 250 ids per query, then the TMDb
baseline. A failed batch is retried once, then skipped; its titles keep their cached entries
and are retried on the next refresh. After 3 failed IMDb batches in a row, IMDb is left alone
for an hour, and the health check *Edition catalog: the last N IMDb runtime requests failed*
appears. The Edition Audit page's *Refresh catalog* button sends the command with
`force: true` (every title, whatever its age); the first fill of a 3000-title library takes
a few minutes. Turning IMDb or Wikidata off in settings hides its entries at once (they stay
stored, and come back when the source is turned on again).

**IMDb terms.** The IMDb provider calls the GraphQL endpoint imdb.com's own pages use
(`caching.graphql.imdb.com`), with the browser headers it expects. It is not a documented
API: IMDb allows its data for personal, non-commercial use only, and may change or block
the endpoint at any time. Cached runtimes survive an outage, Wikidata and library entries
remain, and the provider can be switched off (*Fetch Editions from IMDb*).

### Settings (Settings › Media Management › Editions)

| Setting | Default | Meaning |
|---|---|---|
| Edition Runtime Tolerance | 3 min (0–30) | how far a file may be from a catalog runtime and still match it; PAL speed-up (×25/24, ×24/25) is tried on top |
| Fetch Editions from IMDb | on | IMDb provider |
| Fetch Editions from Wikidata | on | Wikidata provider |
| Move Untagged Imports to Matching Edition | on | re-home at import, see below |

### Matching

A file of `r` minutes matches an entry of `R` minutes when `r`, `r × 25/24` or `r × 24/25` is
within the tolerance of `R`. One cut name matching → *unique*; two or more → *ambiguous*,
nothing is decided; none, with the file more than 8 minutes longer than every entry →
*unknown longer cut*; else *none*.

### Audit v2

`GET /api/v3/edition/audit` now covers **every default-row file**, tagged or not, and adds
`fileRuntimeMinutes`, `isPal`, `runtimeMatchKind`, `runtimeSuggestedEdition`,
`runtimeSuggestedMinutes`, `catalogSource` and `verdict` to the fields above. By default it
returns only files with something to say; `?includeAll=true` returns every file.

| Verdict | Meaning | Example |
|---|---|---|
| `consistent` | tag and runtime agree, or an untagged file runs the theatrical length | Fellowship untagged at 178 |
| `tagOnly` | tagged, but the runtime only matches the theatrical / TMDb runtime and the catalog does not know the tagged cut | Fast Five "Extended" at 131 |
| `runtimeOnly` | untagged (or tagged Theatrical) and the runtime uniquely matches another cut, or is an unknown longer cut | Fellowship untagged at 228 → Extended; Waterworld at 176 → Director's Cut |
| `conflict` | the tag names one cut, the runtime uniquely matches another | tagged Extended at 178, Extended known at 208 |
| `pal` | the tagged / theatrical cut at 25 fps | Extended at 199.7 for 208 |
| `checkIdentification` | runtime matches nothing and is more than 50 % away from the TMDb and IMDb theatrical runtimes: likely the wrong TMDb movie | Seven at 18 min (a short) vs 127 |
| `unknown` | no runtime, no catalog, or an ambiguous match | |

**Apply.** `POST /api/v3/edition/audit/apply` with `[{movieId, editionName, runtimeMinutes?, aliases?}]`
(`movieId` = the default row): per item, it finds the edition row of that name or creates it
(monitored, quality profile and tags as on the default row, the given aliases, the runtime
as its expected runtime, no search), records a
`library` catalog entry (confidence 90; the given runtime, else the file's de-PAL'd runtime),
and moves the default row's file to the edition row as *movefile* does. It answers
`[{movieId, editionMovieId, movieFileId, ok, error}]`; items fail independently. No TMDb call,
and nothing is created without this request.

**Edition Audit page** (Movies › Edition Audit, `/editionaudit`): the same list with a verdict
filter (*Needs action* by default), the suggested edition with its minutes and source, a
picker to override it, multi-select and *Apply selected*, *Refresh catalog*, and a CSV export
shown in a text box to copy.

### At import

MediaInfo is read before the import decision, so the runtime is known there (not at grab
time: routing by release name is unchanged). `EditionRuntimeImportSpecification` runs after
the edition gate, and skips files without a runtime, existing files and manual picks
(*edition override*):

- **Tagged file, runtime matches another cut.** Rejected: *Tagged Extended (208 min) but
  runtime 178 min matches Theatrical (178 min)*. Choose the row by hand in Manual Import to
  import it anyway.
- **Untagged file on the default row, runtime uniquely matches a sibling row's expected
  runtime** (and not the default row's own). With *Move Untagged Imports to Matching Edition*
  on and that row monitored, the file is **re-homed** to the edition row (logged at Info);
  otherwise it is rejected for the default row: *Runtime 208 min matches the Extended edition
  of this movie*. A release variant tag (IMAX, Remastered...) counts as untagged; any other
  tag the catalog does not know (a fan edit) leaves the file where it is.
- Ambiguous, unknown or longer runtimes are accepted as before; the audit lists them.

### Catalog API

| Where | What |
|---|---|
| `GET /api/v3/edition/catalog?movieId=` | `{movieId, movieMetadataId, tmdbId, toleranceMinutes, merged[], entries[]}`: `merged` is what matching uses, `entries` every stored row with its `source`; each entry has `informational` (below confidence 30) |
| `POST /api/v3/edition/catalog` | `{movieId, name, runtimeMinutes}` adds or updates a `user` entry |
| `DELETE /api/v3/edition/catalog/{id}` | `user` entries only (`400` otherwise) |
| `GET /api/v3/edition/catalog/export` | every `library` and `user` entry as `{tmdbId, imdbId, title, year, name, runtimeMinutes, source, confidence, attributes}` |
| `POST /api/v3/edition/catalog/import` | the same list; stored as `library` unless `source` is `user`, matched by TMDb id, never weakens a stronger local entry; answers `{added, updated, unchanged, skipped, errors[]}` |
| `POST /api/v3/command` `{"name":"RefreshEditionCatalog","movieIds":[],"force":false}` | refresh the catalog (empty `movieIds` = whole library) |
| movie resources | `expectedRuntimeMinutes` on every row, in `edition` and in `editions[]`; settable by `POST /movie` (add edition) and `PUT /movie/{id}`. A `PUT` that leaves it out clears it, like the other edition fields |
| `/api/v3/config/mediamanagement` | `editionRuntimeToleranceMinutes`, `editionCatalogImdbEnabled`, `editionCatalogWikidataEnabled`, `editionRehomeOnImport` |

Copying confirmed entries from one instance to another:

```bash
curl -s -H "X-Api-Key: $KEY_A" http://radarr-a:7878/api/v3/edition/catalog/export \
 | curl -s -H "X-Api-Key: $KEY_B" -H 'Content-Type: application/json' \
        -d @- http://radarr-b:7878/api/v3/edition/catalog/import
```

### Limits

- The IMDb failure count behind the health check lives in memory and resets on restart.
- Manual Import does not send the re-home back to the server: when you change a re-homed
  file's quality or language, the preview re-checks the runtime and keeps the re-home while
  the runtime still points to that row (an edit of the catalog in between can change it).
- Migration 249 (`MovieEditionCatalog`, `Movies.ExpectedRuntimeMinutes`) was only run on
  SQLite.

## Naming

New token **`{Edition Name}`** = the edition configured on the row, rendered verbatim
(`Director's Cut`, `Ulysses Cut`), **empty on the default edition**. It is independent of
`{Edition Tags}`, which is still the spelling parsed from the release. Recommended file
format:

```
{Movie Title} ({Release Year}) {edition-{Edition Name}} {Quality Full} {Custom Formats}
```

`{edition-{Edition Name}}` collapses entirely on the default row, so adopting the format
renames nothing in an existing library, and the written tag is what the parser and Plex read
back. The token is not allowed in the movie **folder** format (editions share the folder; the
validator and health check refuse it). The naming preview switches to a Director's Cut sample
when the format contains the token.

## API summary

| Where | What |
|---|---|
| `GET /api/v3/movie`, `GET /api/v3/movie?tmdbId=` | `includeEditions` (default `false`): without it edition rows are left out, so existing clients see the library as before. Every resource carries `edition` (`null` on default rows, else `{name, aliases, acceptUntagged}`); default rows carry `editions[]` (`{id, name, monitored, hasFile, qualityProfileId, movieFileId}`), edition rows carry `editionParentId`. |
| `GET /api/v3/movie/{id}` | works for any row |
| `POST /api/v3/movie` | with `edition` adds an edition to an existing movie; without it always creates a default row |
| `PUT /api/v3/movie/{id}` | edition fields editable, path not; a default row cannot be turned into an edition |
| `DELETE /api/v3/movie/{id}` | `400` on a default row that still has editions |
| `GET /api/v3/edition/audit` | default-row files that carry an edition, classified `cut` / `theatrical` / `variant` / `unrecognised` (see *Existing files*), with a runtime `verdict` (see *Runtime and the edition catalog*); `includeAll=true` lists every default-row file |
| `POST /api/v3/edition/audit/apply` | create the edition row if needed, record the runtime, move the file (see *Audit v2*) |
| `/api/v3/edition/catalog` | the edition catalog (see *Catalog API*) |
| `POST /api/v3/edition/movefile` | `{movieFileId, movieId}`: move a file and its extras to another row of the same movie, then rename it; `400` for another movie, a row that has a file, or the owning row |
| `GET /api/v3/calendar` | `includeEditions` (default `false`) |
| `GET /feed/v3/calendar/radarr.ics` | one event per title (an edition stands in when it is the only monitored row) |
| `/api/v3/wanted/missing`, `/wanted/cutoff`, queue, history, blocklist, manual import, parse | edition rows always included, resources carry `edition` |
| SignalR `movie` channel | broadcasts edition rows regardless of `includeEditions` (the fork UI asks for them) |
| Webhook | `movie.edition` (string, `""` on default rows); Discord and the generic notification body append ` [Extended]` |

The frontend sends `includeEditions=true` on the movie list, the `tmdbId` lookup and the
calendar.

## Limitations (slice 1)

- **RSS routes untagged releases to the default edition only.** Radarr maps one release to
  one movie row, so an RSS release without an edition word never reaches an edition that has
  *Accept releases without an edition tag* on. Such editions are filled by their own
  **searches**: the automatic missing / cutoff-unmet searches and interactive search judge
  every release of the title for the searched row. Tagged releases route by RSS as expected.
- No index filter to hide edition rows yet; they appear as extra cards with the badge.
- An edition cannot be promoted to default, and a default row cannot become an edition.
- One torrent containing two cuts: the file matching the grabbing row imports; the other is
  left for manual import, where it routes to its row.
- Migration 248 replaces the unique index on `Movies.MovieMetadataId` by one on
  `(MovieMetadataId, Edition)`. An older image started on a migrated database boots and
  ignores the new columns (verified with `6.4.4.10685-krzw.1` on a database holding two edition
  rows), but it shows edition rows as duplicate movies sharing a folder. Roll back by restoring
  the pre-upgrade database backup, or delete the edition rows first.
- Postgres: the migration is written for both engines but was only run on SQLite.

## Source

Branch `feat/multi-edition-master` (cut from `v6.4.4.10685+krzw.1`; 17 commits,
`44b7d79d5` to `5071d6a32`: design `44b7d79d5`, foundation `d31b2be5c`, parser `dd49ca341`,
frontend `3b47da12b` / `ed6c5d4b7`, routing `a6cb8e0a4`, gates `e229017cc`, shared-folder
rules `111e32fba`, naming token `04bda5360`, API `d84c3db12`, frontend merge `da54f450c`,
review and integration fixes `feaf34e93`, docs `97f633f11`, verified fixes `95a56de3a`,
`573934a79`, `e7e96a358`, `5071d6a32`), merged into
`personal/all-features-master` as `a5534a06f` (release `v6.4.4.10685+krzw.2`).
Audit and move-file-to-edition: branch `feat/edition-file-reassign-master` (`3f73e6a99`, docs `89d72fa42`),
merged as `e4973c93d` (release `v6.4.4.10685+krzw.3`), files
`Movies/Editions/MovieEditionFileService`, `Radarr.Api.V3/Movies/MovieEditionFileController`,
`frontend/src/Movie/Edition/MovieEditions.tsx`, marked `krzw(edition-file-reassign)`.
Runtime catalog: branch `feat/edition-runtime-catalog-master` (plan
[docs/design/edition-runtime-catalog.md](../design/edition-runtime-catalog.md)), files
`Movies/Editions/Catalog/*` (catalog, providers, refresh, label table, matcher),
`Movies/Editions/EditionAudit.cs`, `MediaFiles/MovieImport/EditionRuntimeImportEvaluator` and
`Specifications/EditionRuntimeImportSpecification`, `Datastore/Migration/249_add_edition_catalog`,
`Radarr.Api.V3/Movies/EditionCatalogController`, `frontend/src/Movie/Edition/Audit/*`, marked
`krzw(edition-runtime-catalog)`.
Key files: `Movies/Movie.cs` (`Edition`,
`EditionAliases`, `EditionAcceptUntagged`), `Movies/Editions/*` (`EditionNormalizer`,
`EditionPresets`, `EditionMatcher`, `EditionGate`, `EditionValidation`,
`AddMovieEditionService`), `Datastore/Migration/248_add_movie_editions`,
`Parser/Parser.cs` (`{edition-X}` reader, Ulysses Cut, Criterion), `Parser/ParsingService.cs`
(routing), `DecisionEngine/Specifications/EditionGateSpecification`,
`MediaFiles/MovieImport/Specifications/EditionGateImportSpecification`,
`MediaFiles/MediaFileService` (`GetSiblingEditionFiles`, sibling-aware `FilterExistingFiles`),
`Extras/EditionExtraFileFilter`, `Download/TrackedDownloads/TrackedDownloadService` (grab
history mapping), `Organizer/FileNameBuilder` (`{Edition Name}`),
`Radarr.Api.V3/Movies/MovieEditionResource` + `MovieController`,
`frontend/src/Movie/Edition/*`. All hunks in upstream files are marked `krzw(multi-edition)`.
