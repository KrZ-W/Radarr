# Changelog

All notable **fork-specific** changes to KrZ-W/Radarr are documented here.
This changelog covers only what this fork adds on top of upstream Radarr — it does
**not** reproduce [upstream's own changelog](https://github.com/Radarr/Radarr/releases).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork's versioning is described in [FORK.md](FORK.md#versioning):
`v<upstream-version>+krzw.<N>`.

## [Unreleased]

_Nothing yet._

## [v6.2.1.10461+krzw.11] — based on Radarr 6.2.1.10461

### Fixed

- **Stuck "Import Pending" self-heal now actually reaches `ImportPending` items.**
  krzw.10 placed the revert inside `CompletedDownloadService.Check`, but the download
  monitor only calls `Check` for `Downloading`/`ImportBlocked` items; `ImportPending`
  items are routed to `Import` on every run instead, so only the `ImportBlocked` half
  of the fix was live. The same guard now sits at the top of `Import`: a client item
  no longer reported `Completed` reverts to `Downloading` with its stale warnings
  cleared, before any import is attempted.
- **Library rescans no longer reject existing files on language.** The import-time
  language check (`LanguageSpecification`) ran on files already inside the library
  during disk rescans and DB rebuilds, refusing to map any file whose audio language
  did not contain the profile language — including the atomic-upgrade crash-recovery
  path. Existing files now skip the check, matching the minimum-CF-score spec.

### Changed

- Frontend lint (prettier) clean on `MediaManagement.tsx`; no functional change.
- Docs: added the User Alternative Titles row to `docs/README.md`; corrected its
  `Since:` line; fixed the sample dataset (`"region": "CA"`, not `"QC"`); the
  setting is named **Translation Search Mode** as in the UI; user-guide table of
  contents lists all recipes; image-pin examples point at the current release;
  `docs/releasing.md` no longer references non-existent `upstream`/`myfork` remotes
  and its base-version check works on this clone.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.11`.

## [v6.2.1.10461+krzw.10] — based on Radarr 6.2.1.10461

### Fixed

- **Downloads permanently stranded in "Import Pending" (mark-failed mid-download):** a
  transient completed-state misread from the download client (seen during external
  recheck/relocate operations) moved a still-downloading item to `ImportPending` /
  `ImportBlocked` with a stale import warning attached. Once there, the item never
  recovered — the completed-download check returns early for any item the client no
  longer reports as `Completed`, so the state and its warning persisted while the
  download was still running, and external queue cleaners read that warning as a failed
  import and marked the grab failed mid-download. Such items now self-heal on the next
  refresh: the state reverts to `Downloading` and the stale warnings are cleared until
  the client actually reports the download complete. Settled states (`Imported`,
  `Failed`, `Ignored`) are left untouched. Ported from the Sonarr fork, where the same
  defect was found; the code path is identical in both. See
  [docs](docs/features/completed-download-handling.md#stuck-import-pending-self-heal).

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.10`.

## [v6.2.1.10461+krzw.9] — based on Radarr 6.2.1.10461

### Fixed

- **Phantom empty slots on failed upgrade imports (data loss):** an upgrade import
  deleted the existing file and its DB row *before* moving the replacement into the
  library, so any failure after that point (destination error, crash, DB write failure)
  destroyed the old file while importing nothing — with no recycle bin configured, the
  loss was permanent. Seen in production as chronic *grabbed → movieFileDeleted/Upgrade
  → no import → missing → re-grab* loops that only manual repair broke. The ordering is
  inherited from upstream; the fork's priority-CF upgrades made it fire far more often.
  Upgrades are now **atomic**: the existing file is parked aside (renamed), the
  replacement is imported and committed, and only then is the original recycled — any
  failure restores the original and returns a moved replacement to the download folder.
  See [docs](docs/features/atomic-upgrade-imports.md).
- **Manual Import no longer 500s on stale file rows:** a database-referenced movie file
  missing from disk made the Manual Import listing throw a fatal
  `FileNotFoundException`; it now logs a warning and lists the item with its last-known
  size.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.9`.

## [v6.2.1.10461+krzw.8] — based on Radarr 6.2.1.10461

### Fixed

- **Library rescans no longer reject existing files below the minimum CF score.**
  The fork's import-time `MinFormatScore` enforcement also ran on unmapped files
  already inside a movie folder during a disk rescan; a file scoring below the
  profile minimum was rejected on every rescan and never mapped into the database
  (present on disk, invisible to Radarr, movie still treated as missing).
  Existing files now skip the check, matching the convention of the other
  import-gatekeeping specs. Enforcement on the download/import path is unchanged.
  Mirrors the same fix in the Sonarr fork (`v4.0.19.2979+krzw.8`).

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.8`.

## [v6.2.1.10461+krzw.7] — based on Radarr 6.2.1.10461

### Fixed

- **Cross-movie guard in `UpsertUserTranslations`:** the translation service
  relied entirely on the controller calling `UserTitleImportGuard.IsSafeForMovie`
  to prevent cross-movie collisions. Added `FindByCleanTitles` to the translation
  repository and an inline guard matching what `UpsertUserTitles` already had, so
  the service rejects collisions regardless of caller.
- **Null body handling on both import endpoints:** a null or malformed JSON body
  on `POST /api/v3/alttitle/user/import` or `POST /api/v3/translation/user/import`
  caused an unhandled `NullReferenceException` (500). Both now return an empty
  summary.
- **Missing `[Consumes("application/json")]`** on the alt-title import endpoint,
  matching the translation endpoint.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.7`.

## [v6.2.1.10461+krzw.6] — based on Radarr 6.2.1.10461

### Fixed

- **User title imports no longer risk misidentifying releases.** The alt-titles
  importer's cross-movie guard only queried the AlternativeTitles table, so a
  curated title that already belonged to another movie's *translation* slipped
  through — and since `MovieService.FindByTitle` checks alternative titles before
  translations, that movie's releases would have resolved to the wrong movie. Both
  importers now share one guard covering movie titles, alternative titles, and
  translations. Titles rejected by it are counted as skipped instead of vanishing
  from the summary.
- **Language codes are canonicalised before building the translation tag.**
  `language: "fra"` resolved to French but stored `RegionalLanguage "fra-ca"` — a
  value no consumer recognises, so the Regional Translation Variants filter dropped
  it from search and `OnePerRegion` counted it as a separate region. The tag now
  uses the resolved two-letter code, and a region carried in the language field
  (`fr-CA`) is honoured when no explicit region is given.

### Docs

- The user-guide recipe now covers the translations importer alongside the
  alt-titles one, and spells out the region rule: `CA` for Quebec, **omit `region`
  for France** (`FR` stores `fr-fr`, which the variants filter drops).
- `FORK.md`'s feature row now mentions the searchable regional translations, its
  `Current fork version` line is current again, and `docs/releasing.md` gained a
  step so that line stops going stale each release.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.6`.

## [v6.2.1.10461+krzw.5] — based on Radarr 6.2.1.10461

### Added

- **User regional translations (Phase 1b):** curated regional titles can now be
  imported as first-class `MovieTranslations` rows so the production search modes
  (`OnePerRegion` + Regional Translation Variants) emit them as queries with no
  search-code changes. Adds `MovieTranslations.SourceType` (migration 244); the
  translations refresh preserves non-TMDB rows with the same user-wins-over-TMDB
  duplicate rule as alt titles. New `POST /api/v3/translation/user/import` accepts
  the curated envelope with standard identifiers per title —
  `{title, language?, region?}` (ISO 639-1 + ISO 3166-1; language defaults to `fr`)
  — building lowercase `lang`/`lang-region` tags that match TMDB rows, so titles in
  any language are importable; idempotent against existing translations, global
  cross-movie clean-title guard across all title tables. The alt-titles endpoint
  remains for parse/import matching. See
  [features/user-alternative-titles.md](docs/features/user-alternative-titles.md).

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.5`.

## [v6.2.1.10461+krzw.4] — based on Radarr 6.2.1.10461

### Added

- **User alternative titles:** alternative titles with `SourceType != Tmdb` now
  survive metadata refreshes — `UpdateTitles` only reconciles TMDB-sourced rows, and
  an incoming TMDB duplicate of a preserved title is skipped instead of converting
  it. New `POST /api/v3/alttitle/user/import` endpoint bulk-upserts titles as
  `SourceType=User` from the curated FR/QC dataset format
  (`[{tmdbId, imdbId, movieTitle, year, missingFrenchTitles:[{title, region}]}]`);
  idempotent, library movies only, returns an added/skipped/not-found summary. User
  titles participate in search, release parsing, and import identification like any
  other alternative title. See
  [features/user-alternative-titles.md](docs/features/user-alternative-titles.md).

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.4`.

## [v6.2.1.10461+krzw.3] — based on Radarr 6.2.1.10461

### Fixed

- **Priority CF upgrades respect Upgrades Allowed:** a profile with upgrades disabled
  no longer auto-replaces files when a release carries a higher priority CF score.
  (Import stays permissive, matching upstream: the flag is a grab-side gate.)
- **Housekeeping no longer crashes with a long cooldown schedule:** with more than 10
  `IndexerCooldownPeriods` entries, a persisted escalation level past the default
  backoff table made `FixFutureProviderStatusTimes` throw on every run; the level is
  now clamped like `CalculateBackOffPeriod`.
- **Deterministic FR vs QC titles in renaming/NFOs:** with multiple translation rows
  per language (`fr` + `fr-CA`), `FileNameBuilder` and Kodi/Emby metadata picked by DB
  row order. Translations are now ordered: configured **Regional Translation Variants**
  first (in list order), then the bare-language row, then remaining variants
  alphabetically — a profile configured for `fr-CA` consistently gets the Quebec title.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.3`.

## [v6.2.1.10461+krzw.2] — based on Radarr 6.2.1.10461

### Fixed

- **Regional translations:** the **Regional Translation Variants** list now actually
  restricts which region-qualified titles are searched in `OnePerRegion`/`AllTitles`
  modes — it was previously accepted but never read. Bare-language titles always pass,
  an empty list means no restriction, and `Standard` mode is unaffected.
  See [docs](docs/features/regional-language-and-translations.md).
- **CI:** a manual `docker-release.yml` dispatch now checks out the requested tag —
  previously it built the default branch HEAD but published it under the release tag,
  silently mislabeling an immutable release image. The image's `revision` label now
  records the actually-built commit.

### Docs

- The VFQ audio-title guide now prescribes a **separate** `VFQ (Audio)` custom format
  with a negated title condition — combining Audio Title and Release Title conditions
  in one format matches nothing at grab time (conditions AND by type group).
- Refreshed post-rebase `Source` hashes, fixed stale tag/image version examples, and
  added a mandatory image boot-test step to the release procedure.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.2`.

## [v6.2.1.10461+krzw.1] — based on Radarr 6.2.1.10461

Maintenance release — rebased the fork onto upstream Radarr **6.2.1.10461** (from `6.1.1.10317`).
All fork features carry forward unchanged; the only fork-side change was dropping the incidental `global.json` .NET SDK pin in favor of upstream's. The full feature set is unchanged
from the previous release (below).

### Changed

- Rebased onto upstream Radarr **6.2.1.10461** (from 6.1.1.10317), picking up upstream's fixes
  between those versions. No fork feature behavior changed.

Container image: `ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1`.

## [v6.1.1.10317+krzw.1] — based on Radarr 6.1.1.10317

First documented fork release. Bundles every feature currently merged into
`personal/all-features-master`. Container image:
`ghcr.io/krz-w/radarr:6.1.1.10317-krzw.1`.

### Added

- **Custom Format Priority Mode** — a per-custom-format **Priority** checkbox in the
  quality-profile editor. Priority CFs are compared *before* quality, so a language
  CF (e.g. VFQ) can win over a higher-quality release while still allowing quality
  upgrades within the same priority tier. Honored at grab, upgrade, and import.
  See [docs](docs/features/custom-format-priority-mode.md).
- **"Audio Title" custom format condition** — a new regex condition that matches
  against each audio stream's title tag. Audio-track titles are now captured into
  `MediaInfoModel.AudioTitles`, enabling content-based VFQ detection for releases
  scene-named only "FRENCH". See [docs](docs/features/vfq-audio-title-detection.md).
- **Regional translation settings** — `RegionalTranslationVariants` plus a
  `RegionalTranslationSearchMode` (Standard / OnePerRegion / AllTitles) in
  *Settings → Media Management*. `AllTitles` also folds in TMDB alternative titles.
  Adds DB migration 243 and a `RegionalLanguage` column on movie translations.
  See [docs](docs/features/regional-language-and-translations.md).
- **Configurable indexer cooldown** — `IndexerCooldownPeriods` (CSV of minutes) in
  *Settings → Indexers → Options (advanced)*, replacing the hard-coded escalation
  schedule for indexers only. See [docs](docs/features/configurable-indexer-cooldown.md).
- **Docker image + GHCR publishing** — multi-stage `Dockerfile` (.NET 8),
  LinuxServer.io-compatible entrypoint (PUID/PGID/TZ/UMASK, `/config` volume, port
  7878), and a GitHub Actions workflow that pushes to `ghcr.io/krz-w/radarr`.
  See [docs](docs/features/docker-deployment.md).

### Changed

- Initial **grab/release selection** now ranks the priority-CF tier ahead of quality
  (`DownloadDecisionComparer`), matching the upgrade path. Backward compatible: with
  no CF flagged Priority, scores are 0 and ordering is unchanged.
- **Import-side upgrade decisions** mirror the grab-side priority-first logic, so a
  file grabbed for a priority language is no longer refused at import by a
  quality-only check.
- **Priority-upgrade rejection messages** now list the matched custom formats and
  absolute scores for both the new and existing file (previously only a delta).
- **MediaInfo schema** bumped 14 → 15 (CURRENT and MINIMUM) so existing files
  re-probe and gain `AudioTitles` on the next library scan.

### Fixed

- **MinFormatScore is now enforced at import time** (mirrors the grab-side
  `CustomFormatAllowedByProfileSpecification`). Adds the `CustomFormatMinimumScore`
  rejection reason.
- **Quality-profile Language is now enforced at import time** (mirrors the grab-side
  `LanguageSpecification`), re-checking the MediaInfo-derived languages. Fixes
  wrong-language files importing over correctly-tagged ones (reproducible with some
  QxR releases). Adds the `WantedLanguage` rejection reason.
- **`IsoLanguages.Find()` fallback** for regional codes with no exact entry
  (e.g. `fr-BE`, `de-AT`, `zh-TW`) now returns the base language instead of `null`.
- **`RegionalLanguage` was always `null`** because SkyHook never populates
  `Iso31661`; now uses SkyHook's full language code (e.g. `fr-ca`) directly.
- **ffprobe is bundled** in the Docker image (ffmpeg apt package + symlink to
  `/app/ffprobe`); imports no longer fail at sample-detection with
  "Cannot determinate if file is a sample". `mediainfo` dropped, `jq` added.
- **`groupadd`/`useradd` use `-o`** so PUID/PGID can reuse an existing GID/UID;
  fixes container start failure when `PGID=100` (a common Proxmox/LXC default)
  collides with Debian's `users` group.

[Unreleased]: https://github.com/KrZ-W/Radarr/compare/v6.2.1.10461+krzw.11...HEAD
[v6.2.1.10461+krzw.11]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.11
[v6.2.1.10461+krzw.10]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.10
[v6.2.1.10461+krzw.9]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.9
[v6.2.1.10461+krzw.8]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.8
[v6.2.1.10461+krzw.7]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.7
[v6.2.1.10461+krzw.6]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.6
[v6.2.1.10461+krzw.5]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.5
[v6.2.1.10461+krzw.4]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.4
[v6.2.1.10461+krzw.3]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.3
[v6.2.1.10461+krzw.2]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.2
[v6.2.1.10461+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.1
[v6.1.1.10317+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.1.1.10317%2Bkrzw.1
