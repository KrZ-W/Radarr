# Import-time Enforcement (Language & MinFormatScore)

> **Status:** stable · **Since:** `v6.1.1.10317+krzw.1` · **Surface:** automatic (no new settings)

## What it does

Mirrors two of Radarr's **grab-time** quality-profile checks onto the **import** side,
so that what gets *kept* in your library actually matches the profile — not just what
was *requested* at grab time:

1. **Quality-profile Language** is re-checked at import against the languages Radarr
   detects from the file's MediaInfo.
2. **MinFormatScore** is re-checked at import against the custom-format score computed
   from the actual file.

If a downloaded file violates either, it is **rejected at import** with a clear reason
instead of being silently moved into the library.

## Why it exists

Upstream Radarr enforces `profile.Language` and `profile.MinFormatScore` **only at
grab time** (`DecisionEngine/Specifications/LanguageSpecification` and
`CustomFormatAllowedByProfileSpecification`). The grab-time decision is based on the
**release-name parse**. But the parse and the **actual file content** can disagree:

- A release name lists subtitle languages that the parser treats as audio (common with
  some **QxR** releases), so a file with the wrong **audio** language passes the
  grab-time language check and then imports — and a quality upgrade can replace a
  correctly-tagged file with one in the wrong language.
- Content-derived custom formats can score differently once real MediaInfo is
  available, pushing the score below `MinFormatScore` — but the import proceeds anyway.

These two import-side specs close those gaps.

## How it works

| Check | Mirrors grab-side spec | At import it re-checks… | New rejection reason |
|---|---|---|---|
| **Language** | `LanguageSpecification` | `localMovie.Languages` (set by `AggregateLanguage` from MediaInfo) vs `qualityProfile.Language`, using the same **Any / Original / specific** semantics | `WantedLanguage` |
| **MinFormatScore** | `CustomFormatAllowedByProfileSpecification` | the custom-format score of the actual file vs `profile.MinFormatScore` | `CustomFormatMinimumScore` |

The two are complementary: the Language spec catches the `profile.Language` case
specifically, while the MinFormatScore spec catches *any* custom-format setup that
drops the score below the minimum, regardless of which CFs are responsible.

## Configuration

None. These checks use your **existing** quality-profile Language and MinFormatScore
settings — they simply now apply at import as well as at grab. If your profile sets
Language = *Any* and MinFormatScore = the default, behavior is unchanged.

## Behavior & edge cases

- A rejected import shows the reason (`WantedLanguage` or `CustomFormatMinimumScore`)
  in the manual-import / activity view, so you can tell *why* a file was refused.
- This pairs naturally with [VFQ Audio-Title Detection](vfq-audio-title-detection.md):
  once VFQ is scored from audio content, the Language/score checks keep a non-VFQ file
  from importing over your VFQ copy.

## Related

- [Custom Format Priority Mode](custom-format-priority-mode.md)
- [VFQ Audio-Title Detection](vfq-audio-title-detection.md)
- User Guide: [Stop wrong-language files from importing](../user-guide.md#recipe-stop-wrong-language-files-from-importing).

## Source

Commits: `d6f282c24` (language), `d329fa443` (MinFormatScore), `75aeeea66` (existing files skip
the language check on rescan). Key files:
`MediaFiles/MovieImport/Specifications/LanguageSpecification.cs`,
`MediaFiles/MovieImport/Specifications/MinimumCustomFormatScoreSpecification.cs`,
`MediaFiles/MovieImport/ImportRejectionReason.cs`.
