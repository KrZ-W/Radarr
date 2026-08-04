# KrZ-W/Radarr User Guide

Task-oriented walkthroughs for the fork's features. Each recipe is self-contained;
follow the one that matches what you're trying to achieve. For the *why* and full
reference, follow the links into [features/](features/).

**Contents**

- [Recipe: Make VFQ win over higher-quality English audio](#recipe-make-vfq-win-over-higher-quality-english-audio)
- [Recipe: Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks)
- [Recipe: Stop wrong-language files from importing](#recipe-stop-wrong-language-files-from-importing)
- [Recipe: Find releases under regional (Quebec) titles](#recipe-find-releases-under-regional-quebec-titles)
- [Recipe: Tune indexer cooldown](#recipe-tune-indexer-cooldown)
- [Recipe: Run the fork in Docker](#recipe-run-the-fork-in-docker)

---

## Recipe: Make VFQ win over higher-quality English audio

**Goal:** Radarr should prefer a Quebec-French release even when a higher-quality
English one exists, but still upgrade quality *within* French.

1. **Settings → Custom Formats** — create a `VFQ` custom format (title regex such as
   `VFQ|VOQ|TRUEFRENCH`). To also catch VFQ hidden in generic `FRENCH` releases, add a
   **separate** audio-based format — see
   [Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks). Do **not** put
   an Audio Title condition inside this title format: the condition groups AND together
   and the format would stop matching at grab time.
2. **Settings → Profiles → Quality → (your profile)** — find the `VFQ` format, give it
   a positive **score**, and tick the **Priority** checkbox.
3. Save.

Now VFQ is compared *before* quality: a VFQ release wins the grab and is protected from
being replaced by a non-VFQ upgrade, while two VFQ releases still compare by quality.

> Full reference: [Custom Format Priority Mode](features/custom-format-priority-mode.md).

---

## Recipe: Detect VFQ from audio tracks

**Goal:** catch VFQ releases that are scene-named only `FRENCH` (no VFQ token), using
the audio track's title.

1. Find the real audio labels: open a known-VFQ file's **Media Info** in Radarr, or run
   `ffprobe yourfile.mkv` and read each audio stream's `title` (e.g. `Français (Québec)`).
2. **Settings → Custom Formats → Add** — create a **separate** format, e.g.
   `VFQ (Audio)` (not a condition inside your title VFQ format — see the warning in
   the recipe above).
3. Add an **Audio Title** condition (Required) with a regex built from what you saw,
   e.g. `\bVFQ\b|\bVOQ\b|Qu[eé]b|Canad`.
4. Add a **negated, Required Release Title** condition containing your title-VFQ regex,
   so the two formats are mutually exclusive and a file scores VFQ only once.
5. Save, then in your quality profile give `VFQ (Audio)` the **same score and Priority
   flag** as the title VFQ format (recipe above).
6. **Trigger a library scan** so existing files re-probe and gain audio titles
   (the MediaInfo schema bump forces a re-probe on the next scan).

> Full reference: [VFQ Audio-Title Detection](features/vfq-audio-title-detection.md).

---

## Recipe: Stop wrong-language files from importing

**Goal:** never let a file with the wrong audio language land in your library, even if
the release name fooled the grab-time check.

There's **nothing to enable** — the fork already re-checks quality-profile **Language**
and **MinFormatScore** at import time. Just make sure your quality profile's **Language**
is set to what you actually want (not *Any*) and your **MinFormatScore** is meaningful.

A file that fails will be rejected at import with reason `WantedLanguage` or
`CustomFormatMinimumScore`, visible in the manual-import / activity view.

> Full reference: [Import-time Enforcement](features/import-time-enforcement.md).

---

## Recipe: Find releases under regional (Quebec) titles

**Goal:** search for a movie using its Quebec title and alternative titles, not just
the primary/English one.

1. **Settings → Media Management → Regional Translation Variants** — add `fr-CA`.
2. **Regional Translation Search Mode** — choose:
   - `OnePerRegion` for a focused extra title per region, or
   - `AllTitles` for all regional translations **plus** TMDB alternative titles.
3. Save and trigger a search.

> Full reference: [Regional Language & Translations](features/regional-language-and-translations.md).

---

## Recipe: Add missing French/Quebec titles TMDB doesn't have

**Goal:** make releases named after a French/QC title match their movie when TMDB
lacks that title — and keep those titles across refreshes.

1. Prepare a JSON file in the curated-dataset format:

   ```json
   [
     { "tmdbId": 194, "imdbId": "tt0211915", "movieTitle": "Amélie", "year": 2001,
       "missingFrenchTitles": [ { "title": "Amélie de Montmartre", "region": "QC" } ] }
   ]
   ```

2. Import it:

   ```bash
   curl -X POST "http://<host>:7878/api/v3/alttitle/user/import" \
     -H "X-Api-Key: <api-key>" -H "Content-Type: application/json" \
     -d @curated.json
   ```

3. Check the summary response; entries under `moviesNotFound` aren't in your library.

Imported titles show `"sourceType": "user"` in `GET /api/v3/alttitle?movieId=<id>`,
survive metadata refreshes, and are safe to re-import after adding movies (already
present titles are skipped).

> Full reference: [User Alternative Titles](features/user-alternative-titles.md).

---

## Recipe: Tune indexer cooldown

**Goal:** change how long a failing indexer is backed off.

1. **Settings → Indexers** — enable *Advanced Settings* (toggle, top-right).
2. **Options → Indexer Cooldown Periods** — enter a CSV of minutes starting with `0`,
   e.g. `0,2,10,30,120`. Leave empty to keep the upstream default.
3. Save.

> Full reference: [Configurable Indexer Cooldown](features/configurable-indexer-cooldown.md).

---

## Recipe: Run the fork in Docker

**Goal:** run this fork as a container, pinned to a known version.

```yaml
services:
  radarr:
    image: ghcr.io/krz-w/radarr:6.2.1.10461-krzw.1   # pin to a release
    container_name: radarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/Paris
      - UMASK=002
    volumes:
      - ./config:/config
      - /srv/movies:/movies
      - /srv/downloads:/downloads
    ports:
      - 7878:7878
    restart: unless-stopped
```

- Use `:latest` instead of the pinned tag if you want the bleeding-edge primary branch.
- `PGID=100` is safe on this image (the entrypoint uses `groupadd -o`).

> Full reference: [Docker / GHCR Deployment](features/docker-deployment.md).
