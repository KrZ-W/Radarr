# Grabbed Release Title

> **Status:** stable · **Since:** `v6.3.0.10514+krzw.12` (unreleased) · **Surface:** Settings → Media Management → *File Management* (advanced) → *Score Files by Grabbed Release Title*, `MovieFile.GrabbedReleaseTitle` (`GET /api/v3/moviefile?movieId=N`), command `BackfillGrabbedReleaseTitles`

## What it does

When Radarr imports a download it remembers the **title of the release it grabbed** on the
movie file (`MovieFiles.GrabbedReleaseTitle`, sanitised, nullable).

With **Score Files by Grabbed Release Title** on, every upgrade decision that scores the
*existing* file re-scores it under the grabbed title as well as the titles it uses today, and
keeps whichever candidate is best. Nothing on disk, in the database apart from that column, or
in any name changes.

## Why it exists

A release is scored from its **release title** at grab time. Once it is imported, the only
title Radarr still has is whatever survived the import: `SceneName` when the download client
reported one, otherwise the original file name, otherwise the (possibly renamed) library file
name. For a well-named grab whose scene name was lost — a `.mkv` handed over by a script, a
manual import, a torrent whose name Radarr renamed, a grab whose title carried
`TRUEFRENCH`/`VFQ`/`MULTi` markers the file name does not — the file scores **lower after
import than the release scored before it**.

The consequence is a loop: RSS sees a release that scores higher than the (under-scored) file,
grabs it, imports it, the file loses the title again, and the same release scores higher again
next sync. Keeping the grabbed title turns that into a stable comparison between the score the
release actually earned and the score of anything new.

## Configuration

Settings → Media Management → show advanced → **File Management**:

| Setting | Default | Meaning |
|---|---|---|
| **Score Files by Grabbed Release Title** | off | Consider the grabbed release title when scoring an existing file for upgrade decisions. |

On `GET/PUT /api/v3/config/mediamanagement` as `scoreFilesByGrabbedReleaseTitle`.

The column is filled for new imports as soon as the fork is running, independently of the
setting. Existing files need the backfill below.

## Behavior

### Capture

At import, when the import carries a download client item with a `DownloadId` and that id has a
`grabbed` history row, the row's `SourceTitle` is **sanitised** and stored on the new movie
file. A manual import with no download id stores nothing, and the column stays `NULL`.

Sanitising exists because some trackers publish a whole description block as the release title:

```
White Chicks 2004 Unrated MULTi VF2 1080p WEBRip x264-PopHD (…)\n\t\n\t
Taille: 4 GB Seeders: 27 Leechers: 4 Complétés: 3079 Catégories: Films
```

Only the **first non-empty line** is kept, trimmed, with internal runs of whitespace collapsed
to a single space. An empty result is stored as `NULL`. 229 of the grabs in the reference
library look like this.

### Selection: Pareto, not "highest score"

Scoring an existing file today walks a ladder: `SceneName` → file name of `OriginalFilePath` →
file name of `RelativePath`. Call the result of that ladder the **incumbent**.

With the setting on:

```
candidates = { GrabbedReleaseTitle, SceneName, filename(OriginalFilePath) }  + the incumbent
eligible   = candidates whose priorityScore >= incumbent.priorityScore
                                AND totalScore    >= incumbent.totalScore
winner     = best eligible by (totalScore, then priorityScore); ties keep the incumbent
```

The incumbent is always eligible, so **neither the total custom-format score nor the priority
score can ever decrease** when the setting is switched on. That is a structural property of the
rule, not an observation.

Selecting by plain highest score would not have it. This fork's
[Custom Format Priority Mode](custom-format-priority-mode.md) compares the **priority** score
*before* quality, so a candidate title with a higher total but a lower priority score would make
the file weaker on the axis that is checked first — and cause exactly the re-grab this feature
is meant to stop.

`RelativePath` is not listed as a candidate on purpose: `ReleaseTitleSpecification` already ORs
the file name into every match (`CustomFormatInput.Filename`), so it is in play for every
candidate anyway.

Only the **release title** varies between candidates. `ReleaseGroup`, `Languages`, `Quality`,
`Size`, `IndexerFlags`, `Edition`, the audio titles read by the
[Audio Title](vfq-audio-title-detection.md) condition and the file name are taken from the file
and stay constant, so a "FR Tier"/"Scene Groups" format that matches on the release group keeps
matching regardless of which title wins.

### Where the new scoring applies

| Site | What it decides |
|---|---|
| `DecisionEngine/Specifications/UpgradeAllowedSpecification` | grab time — the re-grab fix |
| `DecisionEngine/Specifications/UpgradeDiskSpecification` | grab time — cutoff met / is this an upgrade |
| `DecisionEngine/Specifications/RssSync/DelaySpecification` | grab time — is the delay worth bypassing |
| `MediaFiles/MovieImport/Specifications/UpgradeSpecification` | import time |
| `MediaFiles/MovieImport/Manual/ManualImportService` | the manual-import listing and its score |
| `Radarr.Api.V3/MovieFiles/MovieFileResource` | `customFormats` / `customFormatScore` on `GET /api/v3/moviefile` |

`GET /api/v3/movie` is **not** one of them: `MovieController` builds the resource without the
format calculator, so `customFormatScore` is always `0` there with or without this feature.

### Naming is not affected

`Organizer/FileNameBuilder` renders the `{Custom Formats}` token through the **legacy** ladder,
deliberately left untouched. Switching the setting on therefore never changes a rendered name
and never makes *Rename Files* propose a rename. `{Scene Name}` and `{Original Title}` read
their own fields and are equally unaffected, as are webhooks and notifications.

## Backfill

Files imported before this fork ran have no grabbed title. Fill them in once:

```
POST /api/v3/command   {"name": "BackfillGrabbedReleaseTitles"}
```

It is a manual task (not scheduled), idempotent, and safe to re-run.

Matching is an **oracle first, a heuristic only as a fallback**:

1. The `downloadFolderImported` history row whose `Data["fileId"]` equals the movie file's id is
   the import that actually produced this file. If it has a `DownloadId`, use it. If it does
   **not**, the file was imported manually — it is **skipped**, never time-matched.
2. Only a file with no `fileId`-bearing import row falls back to the `DownloadId`-bearing
   import for the same movie whose date is closest to the file's `DateAdded`, within **6 hours**.
3. The title is the `SourceTitle` of the latest `grabbed` row for that download id, compared
   case-insensitively, then sanitised.

Step 1 is what makes the result trustworthy. Measured against the `fileId` oracle on the
reference library, pure time proximity attributed 39 of 2,322 files to the wrong grab, and 38 of
those 39 were manual imports whose nearest neighbour in time was some other torrent.

The run logs `scanned / set / no-import-event / no-download-id / no-grab / unchanged`. History
is loaded once into dictionaries and writes are batched; nothing is queried per file.

## Guarantees and limits

- **Off by default**, and a complete no-op while off: the legacy ladder result is returned
  unchanged.
- **Scores never go down.** Enabling the setting cannot lower a file's total score or its
  priority score (see the Pareto rule above).
- **It can block upgrades that used to be allowed.** This is the point of the feature, and the
  one behaviour change to expect. A higher score on the existing file is a rejection reason at
  `UpgradableSpecification.cs` (`CustomFormatScore`, `CustomFormatCutoff`,
  `MinCustomFormatScore`) and at `MovieImport/Specifications/UpgradeSpecification.cs`. Releases
  that only looked like upgrades because the file had forgotten its own title will now be
  rejected.
- **Naming, webhooks and the `{Custom Formats}` token are unchanged**, on purpose.
- **Files whose true import was a manual import are skipped by the backfill**, by design: there
  is no grab to attribute, and guessing is what produces wrong titles.
- **No file is ever read or written.** Only one nullable text column.
- **Capture needs grab history.** History cleanup that removed the `grabbed` row leaves the
  column `NULL`; the file just keeps scoring the way it does today.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md) — the priority score the Pareto
  rule protects.
- [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — the other half of scoring a file
  by what it is rather than by what it is called.
- [Import-time Enforcement](import-time-enforcement.md) — the import-side mirror that also reads
  the new score.

## Source

Branch `feature/grabbed-release-title-master`, merged into
`personal/all-features-master`. Key files: `Datastore/Migration/247_add_grabbed_release_title_to_movie_files.cs`,
`MediaFiles/GrabbedReleaseTitles/*` (`GrabbedReleaseTitleSanitizer`,
`BackfillGrabbedReleaseTitlesCommand`, `BackfillGrabbedReleaseTitlesService`),
`CustomFormats/CustomFormatCalculationService.ParseCustomFormatForScoring`, `MediaFiles/MovieFile.cs`,
`Parser/Model/LocalMovie.cs` (`GrabbedReleaseTitle`, `ScoringCustomFormats`),
`MediaFiles/MovieImport/ImportApprovedMovie.cs`,
`MediaFiles/MovieImport/Aggregation/Aggregators/AggregateReleaseInfo.cs`,
`History/HistoryRepository.AllByEventType`, `Configuration/ConfigService.cs`,
`Radarr.Api.V3/Config/MediaManagementConfig*`, `Radarr.Api.V3/MovieFiles/MovieFileResource.cs`,
`frontend/src/Settings/MediaManagement/MediaManagement.tsx`.
`git grep -n 'krzw(grabbed-release-title)'` lists every touch of an upstream file.
