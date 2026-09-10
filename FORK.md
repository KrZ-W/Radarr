# KrZ-W/Radarr — Fork Notes

This is a personal fork of [Radarr](https://github.com/Radarr/Radarr) that adds a
handful of features focused on **language-aware grabbing/importing** (Quebec French /
VFQ in particular) and **self-hosted Docker deployment**. It is maintained by a single
person for a private *arr stack; it is not affiliated with the Radarr team.

- **Upstream base:** Radarr `6.3.0.10514` (the commit this fork is rebased onto)
- **Primary branch:** `personal/all-features-master` (all features merged together)
- **Container image:** `ghcr.io/krz-w/radarr`
- **Current fork version:** `v6.3.0.10514+krzw.9`

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
| **IMDb Title Provider** | Downloads IMDb's alternative-title dataset on a schedule and feeds the missing French/Quebec titles into the user-titles pipeline automatically (new movies within seconds); no external scripts | [features/imdb-title-provider.md](docs/features/imdb-title-provider.md) |
| **Audio Language Verification** | Listens to a short clip of suspicious audio tracks with a self-hosted Whisper server at import, so a French track mistagged `eng`/`und` imports as French (and a truly non-French file is rejected with an "Audio verified" reason); stores the per-track outcome on the file, never modifies files | [features/audio-language-verification.md](docs/features/audio-language-verification.md) |
| **Audio Track Retag** | After import, rewrites the language tag of the MKV audio tracks verification found mistagged (`mkvpropedit`, header-only, streams never rewritten) so Plex/Jellyfin/Bazarr agree; hardlink-aware (*Skip* / *Copy then retag* / *Retag in place*), outcome stored on the file, manual per-file command | [features/audio-track-retag.md](docs/features/audio-track-retag.md) |
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
docker pull ghcr.io/krz-w/radarr:6.3.0.10514-krzw.9

# Bleeding edge — tip of personal/all-features-master
docker pull ghcr.io/krz-w/radarr:latest
```

See [features/docker-deployment.md](docs/features/docker-deployment.md) for a full
`docker run` / compose example.

## Upstream issues this fork relates to

For people arriving from an upstream issue: the table below maps each fork feature to the
Radarr issues it addresses or was declined as. Each feature page has an **Upstream** section
with details. This fork does not submit changes upstream.

| Upstream issue | State | Fork feature | Relationship |
|---|---|---|---|
| [Radarr#8444](https://github.com/Radarr/Radarr/issues/8444) deleted-event flood on failed imports | open, Confirmed | [Atomic Upgrade Imports](docs/features/atomic-upgrade-imports.md) | Fixed here (delete deferred until the import commits) |
| [Radarr#11443](https://github.com/Radarr/Radarr/issues/11443) alternate titles not searched | open | [Regional Translations](docs/features/regional-language-and-translations.md), [User Titles](docs/features/user-alternative-titles.md) | `AllTitles` search mode + curated user titles |
| [Radarr#11373](https://github.com/Radarr/Radarr/issues/11373), [#3602](https://github.com/Radarr/Radarr/issues/3602) wrong/working alt titles | open / Maybe One Day | [User Titles](docs/features/user-alternative-titles.md) | Cross-movie guard for user titles only |
| [Radarr#5074](https://github.com/Radarr/Radarr/issues/5074), [#8010](https://github.com/Radarr/Radarr/issues/8010), [#9363](https://github.com/Radarr/Radarr/issues/9363) language before quality | Won't Fix | [Custom Format Priority Mode](docs/features/custom-format-priority-mode.md) | Declined upstream; fork-only by design |
| [Radarr#11422](https://github.com/Radarr/Radarr/issues/11422) grab vs import CF score mismatch | open | [Import-time Enforcement](docs/features/import-time-enforcement.md) | Adjacent only, not fixed |
| [Sonarr#5598](https://github.com/Sonarr/Sonarr/issues/5598) CF comparison release vs file | open (Sonarr) | [VFQ Audio-Title Detection](docs/features/vfq-audio-title-detection.md) | Related discussion |
| [Radarr#11385](https://github.com/Radarr/Radarr/issues/11385) external audio language provider / AI-assisted tagging | open, Needs Triage | [Audio Language Verification](docs/features/audio-language-verification.md), [Audio Track Retag](docs/features/audio-track-retag.md) | Fixed here (Whisper detection at import + tags written back to the file) |
| [Sonarr#8453](https://github.com/Sonarr/Sonarr/issues/8453), [#7523](https://github.com/Sonarr/Sonarr/issues/7523) same request / reject wrong-language imports | not planned (Sonarr) | [Audio Language Verification](docs/features/audio-language-verification.md) | Implemented natively in both forks |
| [Radarr#7584](https://github.com/Radarr/Radarr/issues/7584), [#11189](https://github.com/Radarr/Radarr/issues/11189) rescan does not re-read track languages after an external retag | closed (support / logs needed) | [Audio Track Retag](docs/features/audio-track-retag.md) | Adjacent only: the fork's own edits re-probe and update the record; external retags still need a rescan trigger |
| [Sonarr#3366](https://github.com/Sonarr/Sonarr/issues/3366) reanalyze files after fixing language tags by hand | closed (2019, Sonarr) | [Audio Track Retag](docs/features/audio-track-retag.md) | Adjacent: tags fixed automatically and record refreshed in the same step |

## Relationship to upstream

- The clone has one remote, `origin` → `KrZ-W/Radarr` (this fork). Upstream
  `Radarr/Radarr` is fetched by URL when rebasing (see
  [docs/releasing.md](docs/releasing.md)); `origin/master` and `origin/develop` are
  stale upstream mirrors and are **not** the base.
- Every fork change to an upstream file carries a `krzw(<feature>)` marker comment
  (`// krzw(atomic-upgrade): ...`), so fork hunks are identifiable at rebase time;
  `git grep -n 'krzw('` lists them. Files that cannot hold comments (`en.json`,
  generated `*.css.d.ts`) are the only unmarked ones.
- Each feature lives on its own `feature/*` or `fix/*` branch cut from the upstream
  release tag the fork is based on (`-master` suffix = master line, `-develop` =
  develop line), and is merged into `personal/all-features-master`. Rebasing onto a newer upstream is done per-branch,
  then re-merged. See [CHANGELOG.md](CHANGELOG.md) for the per-feature history.
