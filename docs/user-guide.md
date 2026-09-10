# KrZ-W/Radarr User Guide

Task-oriented walkthroughs for the fork's features. Each recipe is self-contained;
follow the one that matches what you're trying to achieve. For the *why* and full
reference, follow the links into [features/](features/).

**Contents**

- [Recipe: Make VFQ win over higher-quality English audio](#recipe-make-vfq-win-over-higher-quality-english-audio)
- [Recipe: Detect VFQ from audio tracks](#recipe-detect-vfq-from-audio-tracks)
- [Recipe: Stop wrong-language files from importing](#recipe-stop-wrong-language-files-from-importing)
- [Recipe: Find releases under regional (Quebec) titles](#recipe-find-releases-under-regional-quebec-titles)
- [Recipe: Add missing French/Quebec titles TMDB doesn't have](#recipe-add-missing-frenchquebec-titles-tmdb-doesnt-have)
- [Recipe: Let IMDb fill in French/Quebec titles automatically](#recipe-let-imdb-fill-in-frenchquebec-titles-automatically)
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

## Recipe: Rescue mislabeled French audio

**Goal:** a release named `FRENCH`/`VFQ` whose only audio track is tagged `eng` or
`und` (very common with Quebec dubs) should import as French instead of being rejected
as "not French" — and a release that merely *claims* French should still be refused, with
a reason that says the audio was actually checked.

1. Run a [whisper-asr-webservice](https://github.com/ahmetoner/whisper-asr-webservice)
   container reachable from Radarr (the same one Bazarr's Whisper provider uses; the
   `base` model is enough for language identification):

   ```yaml
   whisper:
     image: onerahmet/openai-whisper-asr-webservice:latest
     environment:
       - ASR_MODEL=base
       - ASR_ENGINE=faster_whisper
   ```

2. **Settings → Media Management → show advanced → Audio Language Verification:** tick
   **Enable**, set **Whisper Endpoint** to `http://whisper:9000`, keep the defaults
   (threshold `0.85`, clip at 300 s for 30 s, *Verify Tagged Tracks* = Never). Save.

3. Make sure the quality profile of the movies you care about has **Language = French**
   (or a positively scored *Language: French* custom format). With Language = *Any* and
   no language custom format, nothing is ever probed.

That's it. On the next import whose tags disagree with the release claim, Radarr
extracts a clip of each suspicious track with the bundled `ffmpeg`, asks Whisper, and:

- a confident `fr` detection makes the file import with `Languages = French` — the
  "Not French" custom format no longer matches and a French release is not offered as
  an upgrade;
- no French anywhere keeps the rejection, now worded
  `… Audio verified: no French track (detected en 0.97, en 0.94)`;
- Whisper down or slow → one warning in the log and the import behaves exactly as
  before you enabled the feature.

The outcome is stored per track on the movie file (`audioLanguageVerification` in
`GET /api/v3/moviefile?movieId=…`) and is left untouched by *Rescan Movie*. This feature
never modifies files; to fix the tag in the file too, see the next recipe.

> **Cost:** one ffmpeg extraction and one Whisper call per suspicious track per distinct
> track layout in a download — a 10-file pack with identical layouts costs one probe.
> Set *Verify Tagged Tracks* to *For release groups* with a group list only if you know
> a group mislabels tracks that *look* right.

> Full reference: [Audio Language Verification](features/audio-language-verification.md).

---

## Recipe: Fix the language tag of rescued files

**Goal:** the French movie rescued by the previous recipe imports fine, but Plex still calls
its audio "English" and Bazarr fetches English subtitles, because they read the track's
language tag (`eng`) rather than what Radarr decided. Make the file itself say `fre`.

1. Have the previous recipe working (a verification record is required; nothing is
   re-probed).
2. **Settings → Media Management → show advanced → Audio Language Verification:** tick
   **Retag Audio Tracks**.
3. Pick **Hardlinked Files** for your setup:
   - you import by **copy** or **move** and do not seed from the library → any value works,
     the file is retagged directly;
   - you import by **hardlink** and keep seeding → *Skip* (default) leaves seeding files alone
     (record `skipped-hardlinked`), *Copy then retag* gives the library its own retagged copy
     and leaves the seed intact (costs the file's size in disk space), *Retag in place* edits
     the shared bytes — **the torrent will fail its hash check and stop seeding**.

That's it. Right after an import whose verification found a mistagged track, Radarr runs
`mkvpropedit` (bundled in the image) on the library file — a header-only edit, streams are
never rewritten — re-probes the file, and updates the movie file's languages. Check with:

```bash
ffprobe -v error -select_streams a -show_entries stream_tags=language -of csv=p=0 "Movie (2020).mkv"
curl -s "http://radarr:7878/api/v3/moviefile?movieId=123" -H "X-Api-Key: $KEY" | jq '.[].audioTrackRetag'
```

For a file imported before you enabled this, or one skipped while it was seeding:

```bash
curl -s -X POST http://radarr:7878/api/v3/command -H "X-Api-Key: $KEY" \
     -H 'Content-Type: application/json' -d '{"name":"RetagAudioTracks","movieFileId":456}'
```

It applies the same rules (MKV only, verification threshold, hardlink mode) to that one
file. A file whose record says `done` is never retagged twice; *Rescan Movie* never retags.

> Only Matroska files can be edited in place. By default MP4/M4V/AVI files are left alone
> (record `skipped-container`). If you want those fixed too, set **Non-MKV Files** to
> *Remux to MKV then retag*: Radarr stream-copies the file with the bundled `ffmpeg` into a new
> `.mkv` next to it (no re-encode, same quality, about as long as copying the file), writes the
> corrected tags in the same pass, checks the result with `ffprobe` (stream count, duration,
> tags), then swaps it in — the file's extension changes to `.mkv` and the movie file record,
> size and media info follow; notifications and library updates get the same rename events a
> manual rename raises. The original is only removed after the checks pass; on any failure it is
> left untouched and the record says `failed` with ffmpeg's last lines. A hardlinked MP4 is
> remuxed whatever *Hardlinked Files* says: the new `.mkv` is a separate file and the seed keeps
> its bytes (you lose the shared storage for that file). Subtitle streams Matroska cannot carry
> (MP4 `mov_text`) are dropped and logged; audio and video are never dropped.

> Full reference: [Audio Track Retag](features/audio-track-retag.md).

---

## Recipe: Find releases under regional (Quebec) titles

**Goal:** search for a movie using its Quebec title and alternative titles, not just
the primary/English one.

1. **Settings → Media Management → Regional Translation Variants** — add `fr-CA`.
2. **Translation Search Mode** — choose:
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
       "missingFrenchTitles": [ { "title": "Amélie de Montmartre", "region": "CA" } ] }
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

**To make those titles searchable too**, post the same file to the translations
importer — alternative titles cover parse/import matching, translations are what
the `OnePerRegion` search mode queries:

```bash
curl -X POST "http://<host>:7878/api/v3/translation/user/import" \
  -H "X-Api-Key: <api-key>" -H "Content-Type: application/json" \
  -d @curated.json
```

> **Region field:** use `"region": "CA"` for Quebec titles (stored as `fr-ca`, which
> matches an `fr-CA` entry in *Regional Translation Variants*). **Omit `region`
> entirely for France titles** — `"region": "FR"` stores `fr-fr`, which the variants
> filter drops from search unless you also add `fr-FR` to that list. Titles in other
> languages take `"language": "de"` etc. (ISO 639-1; ISO 639-2 and full tags like
> `fr-CA` are accepted and canonicalised).

> Full reference: [User Alternative Titles](features/user-alternative-titles.md).

---

## Recipe: Let IMDb fill in French/Quebec titles automatically

**Goal:** stop maintaining a curated JSON file — have the instance pull the missing
titles from IMDb's public dataset itself, for the library and for every movie you add.

1. Check *Settings → Media Management → Regional Translation Variants* contains
   `fr-CA` (it does by default). Quebec titles are stored as `fr-ca` only when that
   tag is listed; France titles are always stored as bare `fr`.
2. *Settings → Metadata → IMDb Title Provider*: tick **Enable**. Defaults: Regions
   `CA,FR`, Languages `fr`, Refresh Interval `7` days. Save.
3. *System → Tasks* → run **Imdb Title Dataset Refresh** once (or
   `POST /api/v3/command` with `{"name":"ImdbTitleDatasetRefresh"}`). The first run
   downloads ~300 MB; the log ends with `IMDb titles applied to library: …`.
4. Spot-check a movie: `GET /api/v3/alttitle?movieId=<id>` lists the new rows with
   `"sourceType": "user"`; `GET /api/v3/parse?title=<French release name>` resolves.

From now on the dataset is re-downloaded on the interval (skipped when IMDb reports it
unchanged), every movie you add is synced within seconds, and a health warning appears
if the index is missing or has not been rebuilt for twice the interval.

> **Licence:** IMDb's datasets are for personal, non-commercial use. The fork downloads
> them on your instance only and never redistributes them.

> Full reference: [IMDb Title Provider](features/imdb-title-provider.md).

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
    image: ghcr.io/krz-w/radarr:6.3.0.10514-krzw.7   # pin to a release
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
