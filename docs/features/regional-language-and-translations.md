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
- **Deterministic pick.** Because a movie now has several rows for one language, every
  place that shows or uses "the" translation for a language orders the candidates with
  `OrderByRegionalPreference` (variants list order → bare language → other regions
  alphabetically) before taking the first: the movie/collection/lookup/discover API
  titles, `{Movie TranslatedTitle}` in renaming, Kodi NFO metadata, and the search title
  kept per language in `Standard` mode (or per region in `OnePerRegion`). With `fr-CA`
  in the variants list, French UI titles are the Quebec ones.

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
- **The field is not empty by default.** A fresh install starts with
  `fr-CA,en-CA,es-MX,pt-BR`. SkyHook tags *every* TMDB translation with a region
  (`fr-FR`, `en-US`, `es-ES`, …), so in `OnePerRegion`/`AllTitles` this default drops
  the France/US/Spain/Portugal titles from searches. Clear the field to search all
  regions, or add the ones you want (e.g. `fr-CA,fr-FR`). Only user-imported
  translations without a region are "bare" and pass regardless.
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

## Upstream

Related upstream Radarr issues (state as of 2026-09-09):

- [Radarr#11443](https://github.com/Radarr/Radarr/issues/11443) — *Torznab movie search does
  not fall back to alternate/original titles, causing missed localized releases* (open, 2026).
  Upstream searches only the title, original title and one translation per profile language;
  a maintainer called searching every TMDB alternative title "careless". The `AllTitles` mode
  here does search them (opt-in), and the
  [user titles](user-alternative-titles.md) feature covers the "let me type the title the
  indexer actually uses" request made in that thread. Earlier duplicates closed as support:
  [#3346](https://github.com/Radarr/Radarr/issues/3346),
  [#7155](https://github.com/Radarr/Radarr/issues/7155),
  [#9167](https://github.com/Radarr/Radarr/issues/9167).
- [Radarr#1447](https://github.com/Radarr/Radarr/issues/1447) — *Allow selecting Alternate Name
  when adding movies* (open, Help Wanted, +67 since 2017). Only partially related: the
  deterministic regional pick decides which `fr-*` row is shown, it does not let you choose a
  display title.

## Source

Commits: `d6bf51274` (settings + migration 243 + enum), `cb54d996d` (RegionalLanguage
fix + AllTitles incl. alternative titles), `c43445fc6` (IsoLanguages fallback).
Key files: `Configuration/RegionalTranslationSearchMode.cs`,
`Datastore/Migration/243_add_regional_language_to_movie_translations.cs`,
`IndexerSearch/ReleaseSearchService.cs`, `Parser/IsoLanguages.cs`,
`MetadataSource/SkyHook/SkyHookProxy.cs`,
`Movies/Translations/MovieTranslationExtensions.cs` (`OrderByRegionalPreference`, used by
`Organizer/FileNameBuilder.cs`, `Extras/Metadata/Consumers/Xbmc/XbmcMetadata.cs` and the
movie/collection/lookup/import-list API controllers).
