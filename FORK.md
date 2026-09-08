# KrZ-W/Radarr — Fork Notes

This is a personal fork of [Radarr](https://github.com/Radarr/Radarr) that adds a
handful of features focused on **language-aware grabbing/importing** (Quebec French /
VFQ in particular) and **self-hosted Docker deployment**. It is maintained by a single
person for a private *arr stack; it is not affiliated with the Radarr team.

- **Upstream base:** Radarr `6.2.1.10461` (the commit this fork is rebased onto)
- **Primary branch:** `personal/all-features-master` (all features merged together)
- **Container image:** `ghcr.io/krz-w/radarr`
- **Current fork version:** `v6.2.1.10461+krzw.11`

> The stock upstream `README.md` is kept as-is apart from a short fork callout at the
> top. Everything KrZ-W-specific lives in [`docs/`](docs/) and
> [`CHANGELOG.md`](CHANGELOG.md).

## Features at a glance

| Feature | What it does | Docs |
|---|---|---|
| **Custom Format Priority Mode** | Per-CF "Priority" flag so a language CF (e.g. VFQ) wins over quality, with downgrade protection | [features/custom-format-priority-mode.md](docs/features/custom-format-priority-mode.md) |
| **VFQ Audio-Title Detection** | New "Audio Title" custom format condition that reads audio-track title tags, so VFQ is detected from content, not just the release name | [features/vfq-audio-title-detection.md](docs/features/vfq-audio-title-detection.md) |
| **Import-time Enforcement** | Enforces MinFormatScore and quality-profile Language at *import*, not just at grab | [features/import-time-enforcement.md](docs/features/import-time-enforcement.md) |
| **Atomic Upgrade Imports** | An upgrade never deletes the existing file until the replacement is fully imported (file + DB row); failures restore the original and re-queue the download | [features/atomic-upgrade-imports.md](docs/features/atomic-upgrade-imports.md) |
| **Regional Language & Translations** | Regional title variants (e.g. `fr-CA`) in search, plus a fallback for regional ISO codes | [features/regional-language-and-translations.md](docs/features/regional-language-and-translations.md) |
| **Configurable Indexer Cooldown** | Make the indexer back-off/escalation schedule editable | [features/configurable-indexer-cooldown.md](docs/features/configurable-indexer-cooldown.md) |
| **User Alternative Titles & Translations** | Bulk-import curated titles that TMDB lacks: alt titles (`SourceType=User`) for parse/import matching, plus regional translations (e.g. `fr-CA`) that the `OnePerRegion` search mode queries. Both survive TMDB refreshes | [features/user-alternative-titles.md](docs/features/user-alternative-titles.md) |
| **Completed Download Handling** | Stuck `ImportPending`/`ImportBlocked` queue items self-heal back to `Downloading` when the client stops reporting the download complete | [features/completed-download-handling.md](docs/features/completed-download-handling.md) |
| **Docker / GHCR Deployment** | LinuxServer.io-style image (PUID/PGID/TZ/UMASK, `/config`, ffprobe bundled) published to GHCR | [features/docker-deployment.md](docs/features/docker-deployment.md) |

New here? Start with the **[User Guide](docs/user-guide.md)** for task-oriented walkthroughs
("make VFQ win over higher-quality English", "run it in Docker", etc.).

## Versioning

This fork uses the upstream build version plus a fork counter as
[SemVer build metadata](https://semver.org/#spec-item-10):

```
v<upstream-version>+krzw.<N>
        │                │
        │                └─ fork release number on this base; resets to 1 on each rebase
        └─ the Radarr version this fork is rebased onto (e.g. 6.1.1.10317)
```

Examples:

| Git tag | Meaning |
|---|---|
| `v6.1.1.10317+krzw.1` | First fork release, based on Radarr 6.1.1.10317 |
| `v6.1.1.10317+krzw.2` | Second fork release, **same** upstream base |
| `v6.2.0.10390+krzw.1` | First release after rebasing onto Radarr 6.2.0.10390 |

The `+` is valid in git tags / GitHub releases / SemVer but **not** in container image
tags, so the Docker tag replaces `+` with `-`:

```
git tag      v6.2.1.10461+krzw.1
docker image ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1
```

See [docs/releasing.md](docs/releasing.md) for how to cut a release.

> **In-app version:** the version Radarr shows in *System → Status* comes from
> upstream's build machinery (`src/Directory.Build.props` sets `AssemblyVersion`
> `10.0.0.*`; upstream's CI substitutes the real number, this fork's workflows do
> not, so the compiler fills the last part with a build-time stamp such as
> `10.0.0.6727`). It is meaningless here and **not** changed by this fork. Use the git tag / image
> tag above as the source of truth for "which fork build am I running".

## Pulling the image

```bash
# Pinned to a release (recommended for stability)
docker pull ghcr.io/krz-w/radarr:6.2.1.10461-krzw.11

# Bleeding edge — tip of personal/all-features-master
docker pull ghcr.io/krz-w/radarr:latest
```

See [features/docker-deployment.md](docs/features/docker-deployment.md) for a full
`docker run` / compose example.

## Relationship to upstream

- The clone has one remote, `origin` → `KrZ-W/Radarr` (this fork). Upstream
  `Radarr/Radarr` is fetched by URL when rebasing (see
  [docs/releasing.md](docs/releasing.md)); `origin/master` and `origin/develop` are
  stale upstream mirrors and are **not** the base.
- Each feature lives on its own `feature/*` or `fix/*` branch cut from the upstream
  release tag the fork is based on (`-master` suffix = master line, `-develop` =
  develop line), and is merged into `personal/all-features-master`. Rebasing onto a newer upstream is done per-branch,
  then re-merged. See [CHANGELOG.md](CHANGELOG.md) for the per-feature history.
