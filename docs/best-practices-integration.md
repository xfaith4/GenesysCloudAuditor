# Best Practices Integration

## Purpose

`GenesysCloudAuditor` now consumes the shared content package in `shared/Genesys.BestPractices` as a policy and reference layer.

Responsibility boundary:

- Core retrieves truth from Genesys Cloud APIs.
- Auditor evaluates that truth against shared best-practice policy.

The package is used to make findings explainable, stable, and reportable through:

- best-practice keys
- remediation guidance
- owner/team hints
- glossary references
- roadmap-driven policy expansion

## Runtime Assumptions

The application resolves the shared content root in this order:

1. configured absolute `BestPractices:RootPath`
2. configured relative `BestPractices:RootPath` from `AppContext.BaseDirectory`
3. upward walk from the app base directory until `shared/Genesys.BestPractices` is found

If the content cannot be resolved or a file is missing:

- the application does not crash
- empty repositories are returned
- diagnostics are logged
- UI/report enrichment degrades gracefully

Published builds that are copied outside the repo should set `BestPractices:RootPath` explicitly.

## Loaded Content

The auditor consumes:

- `best-practices/best-practices.catalog.json`
- `best-practices/best-practices.schema.json`
- `best-practices/best-practices-map.json`
- `best-practices/best-practices-map.schema.json`
- `best-practices/glossary.json`

It also resolves the reference markdown files for diagnostics:

- `README.md`
- `Glossary.md`
- `Roadmap.md`
- `best-practices/BestPractices.md`

## Validation Behavior

Schema-aware validation runs during content load.

- When `FailOnValidationError = false`, validation problems are logged and deserialization continues if the JSON is still safe to read.
- When `FailOnValidationError = true`, invalid content is skipped for that file, but the app still degrades safely instead of crashing.

Diagnostics include:

- resolved root path
- file presence
- schema validation pass/fail
- loaded catalog/map/glossary counts
- unmapped finding types observed during enrichment

## Finding Mapping

The shared `best-practices-map.json` uses policy-oriented finding types such as `DidOrExtensionAssignmentInconsistent`.

The application contains a small adapter layer that maps current auditor finding codes to those shared mapping types. The first integrated paths are:

- user telephony integrity findings
- DID mismatch findings
- stale flow findings
- role/group overlap findings

Other finding domains are still tracked as unmapped so the package and adapter can grow without silent gaps.

## Report and UI Surfaces

The integration currently enriches the consolidated report with:

- `Best_Practice_Guidance` worksheet
- mapped remediation and ownership guidance on report objects
- Run Audit UI guidance panel with content health and top mapped items

Separate per-audit workbook exports intentionally suppress the shared guidance sheet to avoid cross-domain spillover.

## Updating the Shared Package Safely

When updating the shared package:

1. update the catalog, map, and glossary in `shared/Genesys.BestPractices`
2. keep keys stable once released
3. validate that any new required fields still deserialize into the application models
4. add or update app-side aliases when a new package mapping type should match an existing auditor finding code
5. add tests for the new mapping or guidance path

## Adding New Analyzer Mappings

When a new auditor finding should map to the shared package:

1. add the shared mapping entry in `best-practices-map.json`
2. add or update the app-side alias in `FindingBestPracticeEnricher`
3. decide whether the finding should produce a first-class guidance row or remain tracked as unmapped
4. add a focused unit test covering zero/one/many-match behavior

This keeps policy growth explicit and prevents Core or transport code from absorbing policy judgments.
