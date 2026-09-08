# Regional Language & Translations

> **Status:** stable · **Since:** `v6.1.1.10317+krzw.1` · **Surface:** Settings → Media Management

## What it does

Improves how Radarr handles **regional** language variants (e.g. `fr-CA` Quebec French
vs `fr-FR` France French) in two areas:

1. **Search** — lets you search using regional **title translations** and alternative
   titles, controlled by a new search mode, so regionally-titled releases are found.
2. **Language resolution** — adds a fallback so regional ISO codes that aren't
   explicitly registered (e.g. `fr-BE`, `de-AT`, `zh-TW`) resolve to their base
   language instead of failing.

## Why it exists

A movie's Quebec title can differ from its France title and from its English title.
Radarr's stock search uses a limited set of titles, so a release published under a
regional title may never be found. Separately, Radarr's `IsoLanguages` table only
knows specific country variants; an unregistered regional code returned `null`, and
SkyHook never populated the country field, so regional translations all collapsed into
one and `RegionalLanguage` was always empty.

## Settings

**Settings → Media Management** gains:

| Setting | What it does |
|---|---|
| **Regional Translation Variants** | CSV of regional language codes to include in searches (e.g. `fr-CA`). In `OnePerRegion`/`AllTitles` modes, region-qualified titles **not** in the list are dropped; bare-language titles (e.g. `fr`) always pass. Empty = include all variants. Has no effect in `Standard` mode. |
| **Translation Search Mode** | How regional titles are used when searching — see below. |

### Search modes

| Mode | Behavior |
|---|---|
| **Standard** | Upstream behavior — no extra regional titles added to searches. |
| **OnePerRegion** | Adds one translated title per configured region. |
| **AllTitles** | Adds **all** regional translations **and** TMDB **alternative titles** to the search queries — widest net. |

## How it works

- A `RegionalLanguage` column is added to movie translations (DB **migration 243**),
  populated from SkyHook's full language code (e.g. `fr-ca`) instead of the never-set
  `Iso31661` field — so regional translations no longer deduplicate into one entry.
- `ReleaseSearchService` filters/expands the set of titles used for searching based on
  the selected search mode; `AllTitles` additionally pulls in TMDB alternative titles.
- `IsoLanguages.Find()` gains a fallback: when a code has a country part but no exact
  match and no empty-country entry exists, it keeps the base-language entries so the
  lookup returns the base language (e.g. `fr-BE` → French) instead of `null`.

## Configuration

1. **Settings → Media Management → Regional Translation Variants** — add the regional
   code(s) you want, e.g. `fr-CA`.
2. **Translation Search Mode** — choose `OnePerRegion` for a focused search or
   `AllTitles` for the widest coverage. Leave on `Standard` to keep stock behavior.
3. Trigger a search; regionally-titled releases should now be matched.

## Behavior & edge cases

- Leaving **Regional Translation Variants** empty is allowed — all regional variants of
  your profile's acceptable languages are then searched (one per region / all titles,
  per the mode).
- Historical note: before `v6.2.1.10461+krzw.2` the variants list was accepted but never
  read — the search modes worked, but the list did not restrict anything.
- `AllTitles` casts the widest net but can surface more false positives — pair it with
  good custom formats / quality profiles.
- The `IsoLanguages` fallback is general (French, German, Chinese, Portuguese, …), not
  French-specific.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md) and
  [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — for *grading* the
  regional releases this feature helps you *find*.

## Source

Commits: `2364f27f9` (settings + migration 243 + enum), `7322fbaf7` (RegionalLanguage
fix + AllTitles incl. alternative titles), `0f3c83e61` (IsoLanguages fallback).
Key files: `Configuration/RegionalTranslationSearchMode.cs`,
`Datastore/Migration/243_add_regional_language_to_movie_translations.cs`,
`IndexerSearch/ReleaseSearchService.cs`, `Parser/IsoLanguages.cs`,
`MetadataSource/SkyHook/SkyHookProxy.cs`.
