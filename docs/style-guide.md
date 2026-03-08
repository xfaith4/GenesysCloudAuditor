# Documentation Style Guide

Use this guide when creating or editing Markdown in this repository.

## Core Principles

- Write for fast scanning first, deep reading second.
- Prefer explicit, technical, verifiable statements.
- Keep docs role-specific; avoid mixing architecture, runbooks, and QA policy in one file.

## Required Structure

Each document should start with:

1. one-sentence scope statement
2. audience
3. when to use

Then organize content with descriptive headings in a logical flow.

## Formatting Rules

- Use sentence case headers.
- Keep paragraphs short.
- Use tables for comparisons and key settings.
- Use fenced code blocks with language labels.
- Use tree diagrams only when structure is easier to understand visually.

## Terminology Standards

Use these canonical terms:

- `IncludeInactiveUsers`
- profile extensions
- assigned extensions
- audit paths
- runner
- scheduled profile

Avoid introducing synonyms for these terms in new docs.

## Cross-linking

- Link related docs instead of duplicating large sections.
- Prefer relative links.
- Update links immediately when filenames change.

## Verification Discipline

- Do not present uncertain behavior as fact.
- Put unresolved claims in `NOTES.md` with verification context.

## Maintenance

- Update docs in the same change set as behavior changes.
- Review `QA.md` and `detailed-qa-matrix.md` when modifying retry, pagination, normalization, or export logic.
