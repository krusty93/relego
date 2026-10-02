---
name: prd
description: 'Generate high-quality Product Requirements Documents (PRDs) for software systems. Includes an executive summary, personas, functional and non-functional requirements, technical specifications, and risk analysis.'
license: MIT
---

# Product Requirements Document (PRD)

## Overview

Design comprehensive, production-grade Product Requirements Documents (PRDs) that bridge the gap between business vision and technical execution. This skill works for modern software systems, ensuring that requirements are clearly defined.

## When to Use

Use this skill when:

- Starting a new product or feature development cycle
- Translating a vague idea into a concrete technical specification
- Stakeholders need a unified "source of truth" for project scope
- User asks to "write a PRD", "document requirements", or "plan a feature"

---

## Operational Workflow

### Phase 1: Discovery (The Interview)

Before writing a single line of the PRD, you **MUST** interrogate the user to fill knowledge gaps. Do not assume context.

**Ask about:**

- **The Core Problem**: Why are we building this now?
- **Success Metrics**: How do we know it worked?
- **Constraints**: Budget, tech stack, or deadline?

### Phase 2: Analysis & Scoping

Synthesize the user's input. Identify dependencies and hidden complexities.

- Map out the **User Flow**.
- Define **Non-Goals** to protect the timeline.

### Phase 3: Technical Drafting

Generate the document using the **Strict PRD Schema** below.

---

## PRD Quality Standards

### Requirements Quality

Use concrete, measurable criteria. Avoid "fast", "easy", or "intuitive".

```diff
# Vague (BAD)
- The search should be fast and return relevant results.
- The UI must look modern and be easy to use.

# Concrete (GOOD)
+ The search must return results within 200ms for a 10k record dataset.
+ The search algorithm must achieve >= 85% Precision@10 in benchmark evals.
+ The UI must follow the 'Vercel/Next.js' design system and achieve 100% Lighthouse Accessibility score.
```

---

## Strict PRD Schema

You **MUST** follow this exact structure for the output:

### 1. Executive Summary

- **Problem Statement**: 1-2 sentences on the pain point.
- **Proposed Solution**: 1-2 sentences on the fix.
- **Success Criteria**: 3-5 measurable KPIs.

### 2. User Experience & Functionality

- **User Personas**: Who is this for?
- **Acceptance Criteria**: Bulleted list of "Done" definitions for each requirement.
- **Non-Goals**: What are we NOT building?

### 3. Technical Specifications

- **Architecture Overview**: Data flow and component interaction.
- **Integration Points**: APIs, DBs, and Auth.
- **Security & Privacy**: Data handling and compliance.

### 4. Risks & Roadmap

- **Phased Rollout**: MVP -> v1.1 -> v2.0.
- **Technical Risks**: Latency, cost, or dependency failures.

---

## Implementation Guidelines

### DO (Always)

- **Iterate**: Present a draft and ask for feedback on specific sections.

### DON'T (Avoid)

- **Skip Discovery**: Never write a PRD without asking at least 2 clarifying questions first.
- **Hallucinate Constraints**: If the user didn't specify a tech stack, ask or label it as `TBD`.

---

## Example: Documentation Search System

### 1. Executive Summary

**Problem**: Users struggle to find specific documentation snippets in massive repositories.
**Solution**: A search system that returns ranked matching documents with direct source links.
**Success**:

- Reduce search time by 50%.
- Top-5 result relevance >= 95%.

### 2. User Experience & Functionality

- **Personas**: Developers searching large documentation repositories.
- **Acceptance Criteria**:
  - Returns ranked results with source links.
  - Returns code blocks with a "Copy" button.
- **Non-Goals**: Editing or authoring documentation.

### 3. Technical Specifications

- **Integration Points**: Documentation index, search API.

### 4. Risks & Roadmap

- **Phased Rollout**: MVP with keyword search, then ranking improvements.
