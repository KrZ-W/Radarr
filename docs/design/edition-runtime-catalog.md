# Runtime-aware editions: plan

Follow-up to multi-edition slice 1 (krzw.2) and the audit / move-file release (krzw.3). Date 2026-10-28. Fork KrZ-W/Radarr, base 6.4.4.10685.

## 1. Why tags are not enough (measured on the radarr-fr copy of 2026-09-15)

2673 of 2676 library files have a MediaInfo runtime. Compared with the TMDb runtime of their title:

| Files | Runtime vs TMDb | Reading |
|---|---|---|
| 2328 | within 2 min | same cut as TMDb's runtime |
| 104 | 4 % shorter | PAL speed-up (25 fps transfers), same cut |
| 143 | 3 to 7 min off | credits, logos, regional cuts, TMDb rounding |
| 72 | more than 7 min longer | 37 carry a tag (Extended, Director's Cut…); **35 carry none**: Fellowship +49, Apocalypse Now +49, Kingdom of Heaven +46, Dances with Wolves +52, Almost Famous +37, Troy +33, Ghostbusters +17, The Wolverine +12 … IMDb has a matching runtime for 28 of the 35; the other 7 are mis-identified titles (Seven at 18 min, Mulan at 50, two shorts) |
| 26 | more than 7 min shorter | TV cuts, wrong TMDb runtime, or wrong match |
| 33 | tagged as a cut, yet within 2 min of TMDb | either TMDb's runtime *is* that cut (Blade Runner 118 = Final Cut, Alien 117) or the tag is wrong (Fast Five "Extended" at 131 min) |

So neither signal works alone. A release tag says what the uploader claimed; the runtime says what the file is; TMDb's single runtime does not say which cut it describes. The plan therefore rests on a per-title **edition catalog** (names with expected runtimes) and matches both tag and runtime against it.

## 2. Sources for the catalog (tested 2026-10-28 from arrdev, all 3156 IMDb-linked titles)

| Source | What it gives | Measured coverage | Verdict |
|---|---|---|---|
| **IMDb GraphQL** (`caching.graphql.imdb.com`, the endpoint IMDb's own site uses) | per-title `runtimes` with attributes: Waterworld 135 + 176 "director's cut"; Fellowship 178 / 208 "Special DVD Extended Edition" / 228 "Blu Ray Extended Edition" / 171 "DVD Widescreen"; Kingdom of Heaven 144 / 190 "director's cut" / 194 "director's cut roadshow"; Star Trek II 113 / 116 "director's cut"; The Exorcist 122 / 132 "director's cut" | **567 titles with two or more runtimes, 512 of them labelled**; whole library fetched in 64 batched calls of 50 ids, ~2 minutes, zero failures | **primary online source** |
| TMDb (via Skyhook, already stored) | one runtime per title, unlabelled | all | baseline and sanity check only |
| Wikidata `P2047` + `P518` | labelled runtimes | 23 titles | secondary, cheap, CC0 |
| Wikipedia infobox, Blu-ray.com, DVDCompare, Schnittberichte | partial or scraping | not pursued | skip |
| The library itself | MediaInfo runtime of every file, plus the user's confirmations | all | fills the gaps IMDb leaves and settles ambiguities |

IMDb label distribution on our titles: extended 72, director's cut 71, cut 50, unrated 40, tv 35, dvd 32, approximate 18, special edition 16, original 14, extended edition 13, extended cut 11, unrated director's cut 10, workprint 9, international 9, québec 7, uncut 5. The first unlabelled runtime is the theatrical reference, which is a better baseline than TMDb (TMDb lists the longer cut for some titles).

Two caveats, both handled in the design: the endpoint is unofficial and needs the request headers IMDb's site sends (a plain request gets 403); and IMDb's terms allow limited non-commercial use only, which a personal home server satisfies but the image must not turn into a bulk mirror (hence batching, caching and refresh only on movie refresh). If IMDb changes or blocks the endpoint, the provider degrades to Wikidata plus library evidence; nothing else breaks.

## 3. Design

### 3.1 Edition catalog

New table `MovieEditionCatalog`: `MovieMetadataId`, `Name` (preset or free text, same rules as edition rows), `RuntimeMinutes`, `Source` (`Tmdb`, `Wikidata`, `Library`, `User`), `Confidence` (0–100), `Updated`. One title can hold several entries. Rules:

- `Imdb` entries: every runtime IMDb lists. The first unlabelled one is `Theatrical` (confidence 80). Labelled ones are mapped by a normalisation table: director's cut / unrated director's cut → Director's Cut; extended, extended edition/cut/version → Extended; unrated → Unrated; uncut → Uncut; special edition → Special Edition; final cut → Final Cut; workprint → Workprint; "director's cut roadshow", "Blu Ray Extended Edition" and similar keep the preset name with the full IMDb text as an alias. Labels that describe a transfer or market (tv, dvd, video, approximate, country names, "original") become low-confidence entries that never drive a verdict.
- `Tmdb` entry: the stored runtime, kept as a sanity check; marked suspect when it disagrees with IMDb's theatrical runtime by more than 50 % (that is how the 7 mis-identified titles surface).
- `Wikidata` entries: labelled durations mapped to presets (theatrical version → Theatrical, director's cut → Director's Cut, extended cut → Extended, final cut → Final Cut). Unlabelled ones are ignored.
- `Library` entries: created when the user confirms a file's edition (move-file, edit file edition, "accept suggestion" in the audit). Runtime = that file's runtime, de-PAL'd.
- `User` entries: typed in the Add Edition dialog or the audit page. Highest priority.
- Merge: `User` > `Library` > `Imdb` > `Wikidata` > `Tmdb`; same name within tolerance collapses to one entry.
- Edition rows get `ExpectedRuntimeMinutes` (nullable) copied from the catalog entry they were created from, editable.

Refresh: a `RefreshEditionCatalog` step inside the existing movie refresh: IMDb batched 50 titles per call with 1.5 s spacing and the required headers, Wikidata batched 250 per query, results cached 30 days, one retry then skip on errors. A one-off `RefreshEditionCatalog` command fills the whole library at install (about 2 minutes). Settings: IMDb provider on/off, Wikidata on/off.

### 3.2 Runtime matching

A file runtime `r` matches a catalog entry `R` when `|r − R| ≤ T`, or `|r × 25/24 − R| ≤ T` (PAL file, NTSC reference), or `|r × 24/25 − R| ≤ T`. `T` defaults to 3 minutes, configurable per instance. If two entries match, the result is ambiguous and nothing is decided automatically. A file more than 8 minutes longer than every entry is an **unknown longer cut**: reported, never routed.

### 3.3 Where runtime is used

**a) Audit v2** (`GET /api/v3/edition/audit`, existing endpoint extended). Every default-row file gets: `fileRuntime`, `tagVerdict` (today's classification), `runtimeVerdict` (catalog match), and a combined `verdict`:

| Verdict | Meaning | Example from the copy |
|---|---|---|
| consistent | tag and runtime agree | Star Trek II Theatrical 113 = 113 |
| tag-only | tagged, runtime equals TMDb | Fast Five "Extended" 131: TMDb may be the extended cut, or the tag lies |
| runtime-only | untagged, runtime matches a known other cut, or is an unknown longer cut | Fellowship 228 (Wikidata: extended), Troy 196 (unknown, user names it) |
| conflict | tag names one cut, runtime matches another | tagged Director's Cut, runtime = theatrical |
| pal | same cut at 25 fps | 104 files, informational |

With an `apply` action: `POST /api/v3/edition/audit/apply` takes a list of `{movieId, editionName, runtimeMinutes}` and, per item, creates the edition row if missing, records a `Library` catalog entry, and moves the file to it (reusing krzw.3's move). The 72 untagged longer cuts become a review list the user clears in minutes; nothing happens without the user's selection.

**b) Import routing.** MediaInfo is read before the import specifications run, so the runtime is available at import, unlike at grab time. New `EditionRuntimeImportSpecification`, after the existing edition gate:

- untagged file, runtime uniquely matches a sibling edition row's expected runtime and not the default's → the file is re-homed to that row if it is monitored and has no better file; otherwise rejected for the default row with the reason, left for manual import
- tagged file whose runtime contradicts the tag (matches another entry uniquely) → rejected: "Tagged Extended (expected 208 min) but runtime 178 min matches Theatrical". Manual pick overrides, as today.
- no catalog entry beyond TMDb, runtime far longer → accepted for the default row as today, flagged in the audit. No auto-created editions.

**c) Grab time.** No runtime exists before download, so tag routing stays. Import-time checks catch mistakes and either re-home or reject the download, which then shows in Activity with the reason.

**d) UI.** Edition rows show expected runtime; the file table shows the file runtime and verdict badge; Add Edition pre-fills from the catalog ("Known: Theatrical 178 · Extended 208 (Wikidata)"); the audit gets a page under Movies with filters by verdict and multi-select apply; Settings → Media Management gets tolerance and the Wikidata toggle.

**e) Cross-instance.** radarr-en and radarr-fr share titles. A catalog export/import (`GET/POST /api/v3/edition/catalog`) lets one instance's confirmed entries seed the other, and lets the ecosystem scripts read expected runtimes.

### 3.5 As built (deviations from this plan)

- **Sources.** `Source` also has `Imdb` (enum `Tmdb=0, Wikidata=1, Imdb=2, Library=3, User=4`), and each entry keeps its raw labels in `Attributes` (JSON). The TMDb entry is named `Default` (confidence 30, or 0 with attribute `suspect`); the audit reads it as Theatrical. Transfer and market labels are kept at confidence 20; everything below 30 is shown but never matched. `workprint` maps to the name `Workprint`, but no Workprint preset was added.
- **Wikidata** also reads the unit of P2047 (`psv:P2047/wikibase:quantityUnit`) and converts hours and seconds; unlabelled durations are ignored as planned.
- **Refresh.** Not a step inside the movie refresh but a queued `RefreshEditionCatalogCommand {movieIds, force}` pushed by `RefreshMovieService` after each refresh, so a slow IMDb never holds the movie refresh. Staleness is read from the TMDb baseline entry, which is written last for every title, already stale when an online provider did not answer for it. A title IMDb leaves out or returns as null keeps its cached IMDb entries. After 3 failed IMDb batches in a row, IMDb is skipped for an hour (across chunks and runs); the health check warns on 3 failed batches in a row, not after a day. Switching a provider off hides its entries from every reader at once instead of deleting them.
- **Audit v2** keeps the old fields and adds `fileRuntimeMinutes`, `isPal`, `runtimeMatchKind`, `runtimeSuggestedEdition`, `runtimeSuggestedMinutes`, `catalogSource` and a single `verdict` (`consistent`, `tagOnly`, `runtimeOnly`, `conflict`, `pal`, `checkIdentification`, `unknown`) instead of separate `tagVerdict` / `runtimeVerdict`. It returns only files with something to say unless `includeAll=true`. Rows' expected runtimes count as catalog entries.
- **Re-home** happens in `ImportDecisionMaker` after MediaInfo is read, not in the per-file edition router (which runs before MediaInfo exists). It needs the setting on and a monitored target row; it does not check whether the target already has a better file (the upgrade specifications do that afterwards). An untagged default-row file whose runtime names a sibling row is rejected whenever it was not re-homed (setting off, or target unmonitored). A release variant tag (IMAX...) counts as untagged; an unknown raw tag is left alone.
- **Library learning** at import (confidence 60) only when the merged catalog does not know the row's cut and the file matches the row's expected runtime, if one is set. The audit's apply records confidence 90.
- **Catalog API** is split: `GET /edition/catalog?movieId=` (merged + stored entries), `POST`/`DELETE` for user entries, and `GET /edition/catalog/export` / `POST /edition/catalog/import` keyed by TMDb id.
- **UI.** The Add Edition modal lists the known editions (informational entries hidden); there is no screen for user catalog entries; the audit page's CSV is a copyable text box, not a download.

### 3.4 Identification check (free by-product)

A file whose runtime matches none of IMDb's runtimes for the title, while TMDb and IMDb disagree with each other or with the file by more than half, is almost always a wrong TMDb match (Seven at 18 minutes is a short film). The audit lists these under `check-identification` so they stop polluting the edition list and get fixed at the source.

## 4. Phases and effort

| Phase | Content | Effort |
|---|---|---|
| P1 | catalog table and migration 249, providers (IMDb GraphQL with label normalisation, TMDb baseline, Wikidata, Library, User), refresh step and fill command, audit v2 verdicts (including check-identification) and CSV, expected runtime on edition rows, UI runtime display and Add Edition pre-fill | 6–7 days |
| P2 | import-time runtime specification with re-home and conflict rejection, audit apply action and page, settings | 4–5 days |
| P3 | catalog export/import between instances | 2 days |

Each phase is a release (krzw.4, krzw.5). All code is additive (new table, new specification, new provider classes); hooks into upstream code are one call in the refresh service and one specification registration. Merge burden stays in the krzw.3 class.

## 5. Risks and open decisions

- IMDb's endpoint is unofficial. Mitigation: provider isolated behind an interface, cached results survive outages, Wikidata and library evidence remain, and a health-check warning appears if IMDb fails for a day.
- TMDb runtime errors (Seven at 18 min) would make everything look like a longer cut. Mitigation: IMDb's theatrical runtime is the baseline and such titles land in `check-identification`.
- Credits-stripped or chaptered rips drift 1–4 minutes. The 3-minute default tolerance covers most; conflicts need two unique matches, so drift alone never rejects.
- Titles where TMDb's runtime is the longer cut (Blade Runner): the catalog cannot know which cut TMDb means until the user names it; verdict stays `tag-only`, never `conflict`.
- Re-homing at import changes which row a download lands on. Default on, with a setting to make it reject-only.
- Decide: tolerance default (3 min), re-home on/off default, whether `Library` entries from the other instance count as confirmed, and whether the IMDb provider is on by default (recommended: on).

## 6. What the user gets after P1

Run the audit on radarr-fr: 28 of the 35 untagged longer cuts come back already named from IMDb (Fellowship 228 = Blu-ray Extended Edition, Kingdom of Heaven 190 = Director's Cut, Apocalypse Now, Dances with Wolves, Troy…), the other 7 are flagged as mis-identified titles, the 37 tagged longer cuts are confirmed or contradicted by runtime, and the 33 tag-only files get a verdict. Tick and apply in bulk. From then on every import is checked against IMDb's runtimes, and a mis-tagged torrent cannot land on the wrong cut.
