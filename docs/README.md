# KrZ-W/Radarr Documentation

Documentation for the KrZ-W fork of Radarr. For the high-level overview and
versioning scheme, see [`../FORK.md`](../FORK.md). For the release history, see
[`../CHANGELOG.md`](../CHANGELOG.md).

## User Guide

- **[User Guide](user-guide.md)** — task-oriented walkthroughs. Start here if you
  just want to *use* a feature ("make VFQ win over higher-quality English audio",
  "run the fork in Docker", "stop wrong-language files from importing").

## Feature Reference

One page per feature — what it does, why it exists, where to configure it, and the
exact behavior.

| Feature | Summary |
|---|---|
| [Custom Format Priority Mode](features/custom-format-priority-mode.md) | Per-CF "Priority" flag: language beats quality, with downgrade protection |
| [VFQ Audio-Title Detection](features/vfq-audio-title-detection.md) | "Audio Title" CF condition reads audio-track titles for content-based VFQ grading |
| [Import-time Enforcement](features/import-time-enforcement.md) | MinFormatScore + quality-profile Language enforced at import, not just grab |
| [Atomic Upgrade Imports](features/atomic-upgrade-imports.md) | Existing file survives unless the replacement import fully commits; failed upgrades restore everything |
| [Regional Language & Translations](features/regional-language-and-translations.md) | Regional title variants in search + regional ISO-code fallback |
| [User Alternative Titles & Translations](features/user-alternative-titles.md) | Bulk-import curated titles TMDB lacks: alt titles for parse/import matching plus regional translations the `OnePerRegion` search queries; both survive refreshes |
| [Configurable Indexer Cooldown](features/configurable-indexer-cooldown.md) | Editable indexer back-off/escalation schedule |
| [Audio Language Verification](features/audio-language-verification.md) | Whisper-backed audio language probe at import when tags contradict the claim / are unknown / would cause a rejection; verified languages outrank tags, outcome stored per track |
| [Completed Download Handling](features/completed-download-handling.md) | Stuck `ImportPending`/`ImportBlocked` items self-heal instead of stalling until a cleaner marks them failed |
| [Docker / GHCR Deployment](features/docker-deployment.md) | LinuxServer.io-style image published to GHCR |

## Maintainer

- **[Releasing](releasing.md)** — how to cut a versioned release (tag → image → GitHub release).

## Conventions used in these docs

- **"Grab time"** = when Radarr decides which release to download.
- **"Import time"** = when a downloaded file is evaluated and moved into the library.
  Several upstream checks only run at grab time; this fork adds import-time mirrors.
- **CF** = Custom Format.
