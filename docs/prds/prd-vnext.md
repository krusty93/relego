# Product Requirements Document — VNext (Deferred Features)

**Date:** 2026-10-02
**Status:** Draft
**Scope:** Post-V1 (Phases 2–5)

These are the features deferred from the V1 iteration. V1 is defined in [`prd-v1.md`](prd-v1.md). They are committed direction, not cancelled ideas, and are documented with the decisions already taken so that later work does not re-litigate them.

## Deferred Features

- **FR-11** — Broader Sources and Dedicated Destinations (Phase 2)
- **FR-12** — Manual Flashcards and Review (Phase 3)
- **FR-13** — Manual Book Summaries (Phase 4)
- **FR-14** — BYOK Question Drafting Through a Skill (Phase 5)

### FR-11 — Broader Sources and Dedicated Destinations (Phase 2)

**Priority:** Should

**Requirement.** Phase 2 broadens import sources and adds dedicated export destinations beyond Notion.

**Acceptance criteria:**

- Migrate from the other free competitor's Markdown export, based on real export fixtures and a declared compatibility version, preserving all supported metadata or reporting what is lost.
- Add general web capture through a browser extension, initiated by the user, storing the source URL and available metadata. It must not grow into full-article archiving, an RSS reader, or a replacement for a full reading application.
- Add PDF and HTML export in addition to Markdown and comma-separated formats, preserving readable text, notes, supported heading structure, and provenance, with HTML safe to open.
- Add dedicated Obsidian and Capacities integrations. Dedicated connectors are required for the named destinations rather than relying on generic Markdown alone. Only Notion ships in this iteration, so these follow next.
- Before implementing each destination, verify that a supported API or local integration contract exists. If it does not, the blocker must be raised for an explicit product decision rather than worked around by scraping or by silently dropping the requirement.
- Provider-specific export, update, and recovery semantics must be defined per destination; the Notion append-only behavior is not assumed to be universal.

### FR-12 — Manual Flashcards and Review (Phase 3)

**Priority:** Should

**Requirement.** Phase 3 adds manual flashcard authoring and review, after the core workflow has proved itself.

**Acceptance criteria:**

- Flashcards are deliberately not part of this iteration; the core workflow proves itself first, and card authoring is added afterward.
- Manual question and answer authoring comes before any AI assistance. AI generation is added only if users demonstrate that authoring effort is the blocking friction.
- Authored cards are linked to their source material and remain distinct from raw highlights; source navigation and answer correction stay possible.
- On the reading device, delivery presents the question before its answer so the reader can attempt recall covertly. This cannot capture answers, score performance, or synchronize device-side review feedback.
- Email and web provide the overt review path, recording a response or an explicit self-assessment with feedback. Whether email uses a reply or a link into the web flow is a feature decision; arbitrary executable email content is not used.
- Feedback can inform future scheduling. Delivered, attempted, answered or self-rated, and successfully recalled are separate states, and no response is not automatically treated as a wrong answer.
- Repeated submissions and stale review links must not corrupt scheduling, and feedback requires authentication and expiry even while managed-service accounts remain deferred.
- The scheduling algorithm, rating scale, and overdue behavior are explicitly left to the feature specification, and recaps continue to work independently of cards.

### FR-13 — Manual Book Summaries (Phase 4)

**Priority:** Should

**Requirement.** Phase 4 adds one editable manual Markdown summary per book.

**Acceptance criteria:**

- Provide one editable Markdown summary per book, created and edited by the user, retained across restarts, and removable without modifying source highlights.
- Summaries appear in supported portable book exports with an explicit boundary between summary and annotations.
- AI book summarization and structured composition from individual highlights are not part of this feature.

### FR-14 — BYOK Question Drafting Through a Skill (Phase 5)

**Priority:** Should

**Requirement.** Phase 5 lets users draft review questions with their own model provider and key through a documented agent skill.

**Acceptance criteria:**

- The capability is delivered as a documented agent skill, not merely as an AI button inside the application.
- The user brings their own model provider and key. No Relego-funded model account and no compulsory AI subscription are required.
- Invocation is explicit and limited to material the user selects; whole-library or silent background generation is rejected.
- Drafts are editable and rejectable, and must be approved before entering scheduled review. Manual authoring and existing review continue to work when generation fails.
- Source text is treated as untrusted data rather than instructions, so embedded content cannot authorize new tools, credential access, broader reads, or outbound requests.
- The user is told which provider will receive which data before transmission; credentials are redacted from prompts and logs, and access can be replaced or revoked.
- Work is bounded by batch and cost limits where the provider exposes usage. Timeouts, rate limits, refusals, malformed output, and exhausted budgets stop or bound the work while preserving already accepted drafts without duplicates.
- Quality is evaluated against a versioned corpus that includes factual, conceptual, ambiguous, context-poor, aesthetic, multilingual, and adversarial passages, measuring schema and provenance validity, grounding and answerability, usefulness compared with manual authoring, safety, and robustness. Numeric thresholds are not yet agreed and must be approved before a model is accepted.
- Generated questions are proposals, not verified facts, and no retention improvement may be claimed without a separate suitable study.

## Open Decisions

- Each later destination's capability and update rules.
- Flashcard scoring and scheduling, and the secure mechanism for email and web review feedback.
- Skill runtime and tool transport, provider and model selection, key storage, budget limits, and quality thresholds.

## Risks and Mitigations

- **External provider costs.** Model usage may carry provider fees. Mitigation: document prerequisites and fees, keep provider costs separate, and bound Phase 5 work by batch and cost limits.
- **Review integrity (Phase 3).** Repeated submissions and stale links could corrupt scheduling. Mitigation: require authentication and expiry, and separate delivered, attempted, answered or self-rated, and successfully recalled states.
- **Quality thresholds (Phase 5).** No numeric thresholds have been agreed, and generated questions are proposals rather than facts. Mitigation: evaluate against a versioned corpus and approve numeric thresholds before a model is accepted; make no retention claims without a separate study.
