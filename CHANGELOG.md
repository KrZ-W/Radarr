# Changelog

All notable **fork-specific** changes to KrZ-W/Radarr are documented here.
This changelog covers only what this fork adds on top of upstream Radarr — it does
**not** reproduce [upstream's own changelog](https://github.com/Radarr/Radarr/releases).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork's versioning is described in [FORK.md](FORK.md#versioning):
`v<upstream-version>+krzw.<N>`.

## [Unreleased]

### Changed

- **User title import pipeline moved into Core** (`Movies/UserTitles/`). The two import
  endpoints are now thin controllers over `IUserTitleImportService`; movie resolution, the
  cross-movie guard, language mapping and the summary were previously duplicated across
  the two controllers in the API project. Behaviour is unchanged for existing callers.
  Improvements that came with the move: the guard runs **one** title sweep per movie
  instead of three queries per title (per-title attribution only when a collision is
  found); a row that throws is reported in a new `moviesFailed` list and the request
  continues instead of returning HTTP 500 with partial writes; requests are validated up
  front (max 5000 movies, 100 titles per movie, 500 characters per title → HTTP 400);
  the summary gains `titlesGuarded`, `titlesUnknownLanguage` and `titlesAlreadyPresent`
  (their sum is the existing `titlesSkipped`); `titles` is accepted as an alias of
  `missingFrenchTitles`. `RegionalLanguageTag` is now the single definition of the
  `RegionalLanguage` storage shape, used by both SkyHook (TMDB rows) and the importer.
  See [docs](docs/features/user-alternative-titles.md#architecture).

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

[Unreleased]: https://github.com/KrZ-W/Radarr/compare/v6.2.1.10461+krzw.5...HEAD
[v6.2.1.10461+krzw.5]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.5
[v6.2.1.10461+krzw.4]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.4
[v6.2.1.10461+krzw.3]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.3
[v6.2.1.10461+krzw.2]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.2
[v6.2.1.10461+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.1
[v6.1.1.10317+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.1.1.10317%2Bkrzw.1
