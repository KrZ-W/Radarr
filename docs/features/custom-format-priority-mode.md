# Custom Format Priority Mode

> **Status:** stable · **Since:** `v6.1.1.10317+krzw.1` · **Surface:** Settings → Profiles → Quality

## What it does

Adds a per-custom-format **Priority** checkbox to the quality-profile editor. A custom
format flagged **Priority** is compared **before** quality when Radarr ranks releases
and decides on upgrades.

This lets a *language* custom format (the motivating case: **VFQ / Quebec French**)
take precedence over quality — Radarr will prefer the VFQ release even if a non-VFQ
release is a higher quality — while still allowing normal quality upgrades **within**
the same priority tier.

## Why it exists

Upstream Radarr ranks releases by **quality first, then custom-format score**. With a
plain (non-priority) VFQ custom format you can add points, but a higher-quality
English release will still out-rank a VFQ one, and can even replace a VFQ file you
already have. There was no way to say "language matters more than quality, but I still
want the best quality *for that language*." The global *Prefer Custom Formats* toggle
was all-or-nothing and didn't express per-format intent.

## How it works

The per-CF Priority flag splits a profile's custom-format score into two numbers:

- **Priority score** — sum of scores from CFs flagged Priority
- **Regular score** — sum of scores from all other CFs

Comparison order becomes: **Priority score → quality → revision → regular CF score**.

This is applied consistently in three places that previously disagreed:

| Stage | Where | Behavior |
|---|---|---|
| **Initial grab** | `DownloadDecisionComparer` | Priority tier is compared ahead of quality, so a priority release wins the first grab even at lower quality. |
| **Upgrade decision** | `UpgradableSpecification` | Higher priority score upgrades regardless of quality; lower priority score is rejected regardless of quality; equal priority falls through to the normal quality/revision/CF chain. |
| **Import** | `MovieImport/.../UpgradeSpecification` | Mirrors the grab/upgrade logic so a file grabbed for a priority language isn't refused at import by a quality-only check. Respects `CutoffFormatScore` and `MinUpgradeFormatScore`. |

When a priority comparison causes a rejection, the queue/activity message now lists
the **matched custom formats and absolute scores** for both the new and existing file,
instead of only a bare delta — so you can see *why* a release was kept or refused.

## Configuration

1. **Settings → Custom Formats** — create the custom format you want to prioritize
   (e.g. a VFQ format).
2. **Settings → Profiles → Quality → (edit a profile)** — find that custom format in
   the format list and:
   - give it a **score** (as usual), and
   - tick the new **Priority** checkbox.
3. Save. The profile now treats that format as language-first.

> Multiple formats can be flagged Priority; their scores are summed into the priority
> tier. A profile with **no** Priority-flagged formats behaves exactly like stock
> Radarr (priority score is always 0).

## Behavior & edge cases

- **Backward compatible.** With nothing flagged Priority, every release scores 0 in
  the priority tier and the original quality-first ordering is unchanged.
- **Quality still matters within a tier.** Two VFQ releases are still compared by
  quality/revision — you get the best VFQ copy, not just the first one.
- **Cutoff & minimum scores are respected.** Import upgrades still honor
  `CutoffFormatScore` and `MinUpgradeFormatScore`.

## Related

- [VFQ Audio-Title Detection](vfq-audio-title-detection.md) — pair Priority with the
  Audio Title condition so VFQ is detected from file content, not just the release name.
- [Import-time Enforcement](import-time-enforcement.md) — complementary import-side
  Language / MinFormatScore checks.
- User Guide: [Make VFQ win over higher-quality English](../user-guide.md#recipe-make-vfq-win-over-higher-quality-english-audio).

## Source

Commits: `fac3bfd70` (per-CF flag), `f59d6a34a` (grab), `b2fb6c737` (import),
`a56378fc2` (rejection messages), `3bb4b7d6c` (tests). Key files:
`Profiles/ProfileFormatItem.cs`, `Profiles/Qualities/QualityProfile.cs`,
`DecisionEngine/Specifications/UpgradableSpecification.cs`,
`DecisionEngine/DownloadDecisionComparer.cs`,
`MediaFiles/MovieImport/Specifications/UpgradeSpecification.cs`.
