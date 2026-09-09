# Completed Download Handling

> **Status:** stable · **Since:** `v6.2.1.10461+krzw.10` · **Surface:** automatic (no settings)

## Stuck "Import Pending" self-heal

A tracked download whose client item is no longer reported as `Completed` and that is
sitting in `ImportPending` or `ImportBlocked` is reverted to `Downloading`, and its
status messages are cleared.

## Why it exists

Completed Download Handling returns early for any item the download client does not
report as `Completed`. So if the client misreads a still-running download as complete
even once — seen in this stack during external recheck/relocate operations — the item is
promoted to `ImportPending` / `ImportBlocked` with an import warning attached, and that
same early return then means nothing ever re-evaluates it.

The queue entry stays stuck with a stale warning for the rest of the download's life.
External queue cleaners that treat a warning as a failed import will act on it and mark
the grab failed **mid-download**, so a perfectly healthy transfer is cancelled and
re-queued.

## Scope

Only `ImportPending` and `ImportBlocked` are reverted. Settled states (`Imported`,
`Failed`, `Ignored`) are never touched, so the self-heal cannot resurrect a download that
has already concluded.

Genuine failures are unaffected: a client item reporting `Failed` is still handled by the
failed-download path, which keys off the **client** status rather than the tracked state.

Recovery is automatic on the next CDH run — no restart or queue intervention needed.

**Where it runs:** the guard lives in both `CompletedDownloadService.Check` (reached by
`ImportBlocked` items via the download monitor) and `CompletedDownloadService.Import`
(reached by `ImportPending` items via download processing). `krzw.10` only had the
`Check` half, so `ImportPending` items never self-healed; `krzw.11` added the `Import`
half.

## Relationship to the Sonarr fork

The same fix ships in the [KrZ-W/Sonarr](https://github.com/KrZ-W/Sonarr) fork as of
`v4.0.19.2979+krzw.10`. The code path is identical in both, and the trigger is the
download client, which both share.

## Upstream

No open or declined upstream Radarr request matches the stuck `ImportPending` self-heal
(searched 2026-09-09).

## Source

Commits: `f86cb016a` (fix in `Check`), `faeb8c6c3` (regression tests), `6475b4196` (same guard in
`Import`, the path `ImportPending` items actually take). Key files:
`Download/CompletedDownloadService.cs` (`Check()` early-return branch),
`Download/TrackedDownloads/TrackedDownload.cs` (`ResetStatus()`).
