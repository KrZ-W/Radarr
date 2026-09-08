# Docker / GHCR Deployment

> **Status:** stable · **Since:** `v6.1.1.10317+krzw.1` · **Image:** `ghcr.io/krz-w/radarr`

## What it does

Adds a multi-stage **`Dockerfile`** that builds Radarr from source against **.NET 8**
and produces a runtime image following **LinuxServer.io-compatible** conventions, plus
a **GitHub Actions** workflow that publishes the image to **GitHub Container Registry
(GHCR)** at `ghcr.io/krz-w/radarr`. This is what lets the Proxmox/Docker stack pull a
pre-built fork image instead of building locally.

## Image conventions

| Aspect | Value |
|---|---|
| Registry / image | `ghcr.io/krz-w/radarr` |
| Web UI port | `7878` |
| Config volume | `/config` |
| User mapping | `PUID` / `PGID` (default `1000` / `1000`) |
| Timezone | `TZ` (default `Etc/UTC`) |
| File mode | `UMASK` (default `002`) |
| Healthcheck | `GET http://localhost:7878/ping` |

## Image tags

| Tag | Points at | Use for |
|---|---|---|
| `6.2.1.10461-krzw.11` | a tagged release (immutable) | **production — pin to this** |
| `latest` | tip of `personal/all-features-master` | bleeding edge |
| `personal-all-features-master` | same branch (ref tag) | bleeding edge |
| `sha-<short>` | a specific commit | debugging / rollback |

> Release tags use `-krzw.N` because container registries don't allow `+` in tags;
> the matching git tag / GitHub release uses `+krzw.N`. See [../../FORK.md](../../FORK.md#versioning).

## Quick start

### docker run

```bash
docker run -d --name radarr \
  -p 7878:7878 \
  -e PUID=1000 -e PGID=1000 -e TZ=Europe/Paris -e UMASK=002 \
  -v /path/to/config:/config \
  -v /path/to/movies:/movies \
  -v /path/to/downloads:/downloads \
  ghcr.io/krz-w/radarr:6.2.1.10461-krzw.11
```

### docker-compose

```yaml
services:
  radarr:
    image: ghcr.io/krz-w/radarr:6.2.1.10461-krzw.11
    container_name: radarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Paris
      - UMASK=002
    volumes:
      - /path/to/config:/config
      - /path/to/movies:/movies
      - /path/to/downloads:/downloads
    ports:
      - 7878:7878
    restart: unless-stopped
```

## Notes & gotchas

- **ffprobe is bundled.** Recent Radarr probes media with ffprobe (via `FFMpegCore`)
  rather than libmediainfo. The image installs `ffmpeg` (providing `/usr/bin/ffprobe`)
  and symlinks it to `/app/ffprobe` so `FFMpegCore` finds it locally; `jq` is included
  for LSIO-style script compatibility. Without this, imports fail with
  *"Cannot determinate if file is a sample"*. `mediainfo` is intentionally **not**
  installed (unused at runtime, matching the LSIO image).
- **PUID/PGID can reuse existing IDs.** The entrypoint runs `groupadd -o` / `useradd -o`,
  so a `PGID=100` (a common Proxmox/LXC default that collides with Debian's `users`
  group) no longer crashes container start.
- **`:latest` is bleeding edge, not stable.** It tracks the primary branch tip. Pin a
  `…-krzw.N` tag for anything you care about.
- **Platform:** images are built for `linux/amd64`.

## Building locally

```bash
docker build -t radarr-fork .
```

The build compiles the frontend and backend from the checked-out source, so it needs
no pre-built artifacts.

## Source

Commits: `61cba4a41` (Dockerfile + workflow + entrypoint), `05d52a29b` (ffprobe),
`b820361f2` (`-o` GID/UID reuse). Key files: `Dockerfile`, `docker/entrypoint.sh`,
`.dockerignore`, `.github/workflows/docker-image.yml`.
