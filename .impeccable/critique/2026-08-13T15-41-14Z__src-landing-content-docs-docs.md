---
target: doc website
total_score: 31
max_score: 40
na_heuristics:
p0_count: 0
p1_count: 2
timestamp: 2026-08-13T15-41-14Z
slug: src-landing-content-docs-docs
---
## Design Health Score

| # | Heuristic | Score | Key issue |
|---|---|---:|---|
| 1 | Visibility of System Status | 3 | Stage and page location are clear, but the first-recap path has no progress or completion checkpoint. |
| 2 | Match System / Real World | 4 | The reader-to-highlights-to-recap circuit is unusually product-native. |
| 3 | User Control and Freedom | 3 | Navigation is strong; the first-recap action leads to an overview rather than the immediate next action. |
| 4 | Consistency and Standards | 3 | The visual language is cohesive, but schedule time-zone wording conflicts across the web and CLI paths. |
| 5 | Error Prevention | 3 | Warnings exist, but SMTP and Kindle approved-sender prerequisites arrive late. |
| 6 | Recognition Rather Than Recall | 3 | Screenshots and rails help; web, CLI, Docker, and environment handoffs still require recall. |
| 7 | Flexibility and Efficiency | 3 | Web, CLI, and Docker routes coexist; the CLI lacks a task-oriented action index. |
| 8 | Aesthetic and Minimalist Design | 3 | Strong authored system, but the primary CTA currently fails text contrast. |
| 9 | Error Recovery | 3 | Troubleshooting is concrete, but setup lacks contextual validation checkpoints. |
| 10 | Help and Documentation | 3 | Comprehensive reference, but the activation path remains split between an overview and its first action. |
| **Total** | | **31/40** | **Strong visual system; activation and accessibility need attention** |

## Design Specificity Verdict

**LLM assessment: strongly product-authored.** The warm-paper palette, Playfair headings, hairline rules, and three-stop Round Trip create a documentation experience inseparable from Relego's reflective reading purpose. It is not category-interchangeable.

**Deterministic scan: 0 findings.** `detect.mjs` returned `[]` for `src/landing/content/docs/docs`.

**Browser evidence:** every representative route returned HTTP 200 at desktop and phone sizes, with no document-level horizontal overflow, missing alt text, or unloaded imagery. The prior heading-order defect is resolved: Revisit now has no skipped heading levels. The mobile CLI reference reflows without horizontal table scrolling; Import and Revisit retain intentional, contained table scrolling.

**Important browser finding:** Axe reports one serious `color-contrast` violation on the new first-recap CTA: its `#f7f1e8` text on `#b56b39` measures 3.64:1. No reliable user-visible detector overlay was available. Local Astro's dev toolbar was excluded because it is environment tooling, not shipped docs UI.

## Overall Impression

The design system and journey model are excellent. The biggest remaining opportunity is to make the promising first-recap launchpad truly action-first, accessible, and self-confirming rather than a second route into explanation.

## What's Working

- **The Round Trip remains the docs' core advantage.** It teaches Relego's real workflow rather than imposing generic documentation categories.
- **The mobile CLI adaptation succeeds.** Commands are readable as labeled rows without compromising the desktop table reference.
- **The first-recap checklist makes prerequisite and outcome information more visible.** It is a meaningful improvement over a text-only start link.

## Priority Issues

1. **[P1] The primary first-recap CTA fails color contrast.**
   - **Why it matters:** The main activation action fails a serious Axe check at normal text size, impairing legibility for users with low vision.
   - **Fix:** Use the dark ink token for CTA text on terracotta, or a contrast-safe darker terracotta surface with white text; validate both themes.
   - **Suggested command:** `$impeccable polish`

2. **[P1] The primary action is visually and behaviorally one step removed from setup.**
   - **Why it matters:** It sits after the large Library image, below the first desktop viewport and roughly 1,744px into a phone page. Its destination is the Round Trip overview, not "Start the server," so a newcomer must scroll again before acting.
   - **Fix:** Place the launchpad above the screenshot and route its primary action to `/docs/import/#1-start-the-server` or a dedicated four-step route. Keep the Round Trip as secondary context.
   - **Suggested command:** `$impeccable onboard`

3. **[P2] Setup has no visible progress or success checkpoints.**
   - **Why it matters:** Docker, import, SMTP, approval, and send-now require working memory; readers do not get a clear "Step 1 of 4" orientation or confirmation that the journey is ready for its next stage.
   - **Fix:** Show a concise numbered progression in the first-recap path, with expected confirmation after each step and a pre-send reminder for Kindle approved senders.
   - **Suggested command:** `$impeccable onboard`

4. **[P2] Mobile header and code-copy controls remain 32px.**
   - **Why it matters:** The tested Menu, GitHub, and copy controls are below the 44px touch-comfort target, particularly frustrating on a reference-heavy phone flow.
   - **Fix:** Increase visible compact controls to at least 44px square at phone widths, preserving their visual glyph size and existing desktop density.
   - **Suggested command:** `$impeccable adapt`

5. **[P2] Delivery decisions still arrive without enough recommendation.**
   - **Why it matters:** New users see several SMTP providers, their own relay, a demo profile, and Kindle approval as separate facts rather than a decision sequence. Remote scheduling language also remains inconsistent.
   - **Fix:** Add a three-way decision guide (existing SMTP, test with smtp4dev, choose a relay) and state that schedules use the server's configured time zone with a remote-host example.
   - **Suggested command:** `$impeccable clarify`

## Persona Red Flags

- **Jordan, first-timer:** The new CTA is easy to miss after the Library image and lands on another explainer before the Docker command. SMTP and Amazon's approved-sender requirement still feel like late surprises.
- **Alex, power user:** The CLI is now mobile-readable, but has no task index for "send now," "schedule," or "configure delivery" and still requires manual Docker-prefix recall.
- **Mina, remote self-hoster:** Conflicting server versus CLI time-zone wording can make a recap arrive at the wrong hour, and `localhost` appears before an early connection model for remote hosting.

## Minor Observations

- Hiding the Round Trip return leg on very small screens preserves space but weakens the signature circuit metaphor.
- The home checklist has improved findability, but its screenshot placement suppresses its hierarchy.
- The generated on-page heading preceding the document H1 is a Starlight structural artifact; it did not produce a heading-order violation.

## Questions to Consider

- Should the first-recap action optimize for a reader who has only three minutes and needs a test delivery, not a full conceptual tour?
- Is a test relay the recommended default for exploration, rather than a provider decision before value is proven?
- Would a single explicit "recap delivered" checkpoint make the entire setup feel shorter and safer?
