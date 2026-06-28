# Changelog

All notable **fork-specific** changes to KrZ-W/Radarr are documented here.
This changelog covers only what this fork adds on top of upstream Radarr — it does
**not** reproduce [upstream's own changelog](https://github.com/Radarr/Radarr/releases).

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this fork's versioning is described in [FORK.md](FORK.md#versioning):
`v<upstream-version>+krzw.<N>`.

## [Unreleased]

_Nothing yet._

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

[Unreleased]: https://github.com/KrZ-W/Radarr/compare/v6.2.1.10461+krzw.1...HEAD
[v6.2.1.10461+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.2.1.10461%2Bkrzw.1
[v6.1.1.10317+krzw.1]: https://github.com/KrZ-W/Radarr/releases/tag/v6.1.1.10317%2Bkrzw.1
