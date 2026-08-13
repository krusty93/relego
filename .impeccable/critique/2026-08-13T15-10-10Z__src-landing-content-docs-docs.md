---
target: doc website
total_score: 32
max_score: 40
na_heuristics:
p0_count: 0
p1_count: 2
timestamp: 2026-08-13T15-10-10Z
slug: src-landing-content-docs-docs
---
## Design Health Score

| # | Heuristic | Score | Key issue |
|---|---|---:|---|
| 1 | Visibility of System Status | 3 | Current page and stages orient readers, but setup success checkpoints are vague. |
| 2 | Match System / Real World | 4 | The reader-to-highlights-to-recap circuit maps directly to the product. |
| 3 | User Control and Freedom | 3 | Navigation is strong, but the docs home lacks an unmistakable first-run entry point. |
| 4 | Consistency and Standards | 3 | The visual system is cohesive; server versus CLI timezone wording conflicts. |
| 5 | Error Prevention | 3 | Kindle and security warnings are helpful but appear after readers have committed to setup. |
| 6 | Recognition Rather Than Recall | 3 | Rails, stages, and screenshots help, but the next action is mostly prose-led. |
| 7 | Flexibility and Efficiency | 3 | Web-first and CLI paths coexist well; mobile CLI reference tables are laborious. |
| 8 | Aesthetic and Minimalist Design | 4 | The warm paper palette, restrained rules, and type support reflective reading. |
| 9 | Error Recovery | 3 | Troubleshooting exists, but confirmation checkpoints are limited. |
| 10 | Help and Documentation | 3 | Concrete content, but a first recap requires stitching guidance across pages. |
| **Total** | | **32/40** | **Strong, focused documentation experience** |

## Design Specificity Verdict

**LLM assessment: 4/4, product-authored.** The Round Trip structure, warm-paper reading atmosphere, and actual Relego UI captures make this feel purpose-built for a highlight-review ritual rather than a generic developer documentation template. The tri-rail desktop layout is calm and highly orienting.

**Deterministic scan: 0 findings.** `detect.mjs` returned `[]` for `src/landing/content/docs/docs`.

**Browser evidence:** every inspected route returned 200 at 1440x900 and 390x844; no document-level horizontal overflow, missing alt text, or broken images were observed. The browser audit found one genuine moderate `heading-order` violation on `revisit.md:29`: `#### Relego Daily Recap` follows an `h2` without an intervening `h3`. Local headless font requests to Google Fonts failed due to CORS/ORB, but this is likely environment-specific; fallback fonts rendered and it needs deployed-browser confirmation before it is treated as production breakage. No reliable user-visible detector overlay is available because only headless Playwright was exposed; a nonvisual Axe scan was used instead.

## Overall Impression

This is an unusually coherent reading experience with a clear product metaphor. The biggest opportunity is to turn the attractive, explanatory round trip into a single, confidence-building path to a first successful recap.

## What's Working

- **The Round Trip teaches the product rather than merely listing pages.** It gives unfamiliar readers a useful mental model before the operational detail arrives.
- **The web-first approach is tangible.** Actual library, import, delivery, and recap screens make the intended path credible while leaving CLI usage available for automation.
- **The visual restraint is appropriate.** Paper-toned surfaces, editorial typography, and deliberate whitespace support long-form reading without feeling like generic API documentation.

## Priority Issues

1. **[P1] First-run setup is not a single executable checklist.**
   - **Why it matters:** Docker, SMTP, device access, Kindle approval, and expected outcomes are distributed across pages. First-time self-hosters must retain too much context before earning the first recap.
   - **Fix:** Add a compact "First recap checklist" near the docs home or Round Trip entry: prerequisite, action, expected result, and recovery link for each step.
   - **Suggested command:** `$impeccable onboard`

2. **[P1] Timezone semantics conflict between web/server and CLI guidance.**
   - **Why it matters:** A remote deployment can deliver at an unexpected hour, undermining trust in recurring recaps.
   - **Fix:** Establish one authoritative timezone model, state it in both places, and add a remote-server example.
   - **Suggested command:** `$impeccable clarify`

3. **[P2] The mobile CLI reference requires horizontal table scrolling.**
   - **Why it matters:** The tables remain accessible and avoid page overflow, but users cannot comfortably scan parameters, accepted values, and descriptions together on a phone.
   - **Fix:** Render mobile command options as stacked definition-list or command-card content while retaining tables on desktop.
   - **Suggested command:** `$impeccable adapt`

4. **[P2] One recap guide heading skips a level.**
   - **Why it matters:** The `h2` to `h4` jump on `Relego Daily Recap` weakens the document outline for assistive technology and creates avoidable semantic inconsistency.
   - **Fix:** Change `revisit.md:29` to an `h3`, or introduce an appropriate `h3` parent section.
   - **Suggested command:** `$impeccable polish`

5. **[P2] The home-page starting action is too understated.**
   - **Why it matters:** "The round trip" link and "Next: Overview" ask first-time readers to infer the intended sequence rather than offering a confident first move.
   - **Fix:** Give the homepage a visible "Set up your first recap" action with expected time and prerequisites.
   - **Suggested command:** `$impeccable onboard`

## Persona Red Flags

- **Jordan, first-timer:** The web-first story still reaches Docker and SMTP decisions before a concrete success checkpoint; Kindle sender approval is introduced too late to prevent a confusing delivery failure.
- **Alex, power user:** The CLI reference is comprehensive on desktop, but mobile tables make a quick parameter lookup and copy-paste workflow slow.
- **Mina, remote self-hoster:** The no-auth warning is clear, but timezone ownership, persistent-data/backup validation, and a concise remote deployment path leave important operational assumptions implicit.

## Minor Observations

- The source UI in desktop documentation captures is visually small; mobile-specific captures address this well on phones.
- Search, menu, copy, and disclosure controls measured around 32px tall in the headless scan. They are not automatically WCAG failures, but deserve a touch-comfort review.
- "A graveyard" is memorable copy but may sound judgmental to readers with a large unreviewed library.

## Questions to Consider

- Should SMTP be a prerequisite before readers can experience Relego's value, or can the first-run path separate import and delivery confidence?
- Does Kobo's "send it on using your usual method" complete the product promise enough to sit beside Kindle's Send-to-Kindle flow?
- Should deployment safety become a distinct path from the reader-focused recap journey?
