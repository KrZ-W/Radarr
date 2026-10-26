# Multi-Edition Movies

> **Status:** pilot (slice 1) · **Since:** `v6.4.4.10685+krzw.2` · **Surface:** movie page (*Editions* box, *Add Edition*, move file to edition), edit-movie modal, naming token `{Edition Name}`, `/api/v3/movie`, `/api/v3/calendar`, `/api/v3/edition` (audit and move file: unreleased) · **Design:** [design/multi-edition.md](../design/multi-edition.md)

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
| `GET /api/v3/edition/audit` | default-row files that carry an edition, classified `cut` / `theatrical` / `variant` / `unrecognised` (see *Existing files*) |
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
Audit and move-file-to-edition: branch `feat/edition-file-reassign-master` (`3f73e6a99`), files
`Movies/Editions/MovieEditionFileService`, `Radarr.Api.V3/Movies/MovieEditionFileController`,
`frontend/src/Movie/Edition/MovieEditions.tsx`, marked `krzw(edition-file-reassign)`.
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
