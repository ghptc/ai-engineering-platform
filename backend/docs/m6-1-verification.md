# M6.1 initial intake slice — verification and limits

Implemented bounded Markdown/TXT/DOCX intake, stable source-backed candidate IDs, explicit ambiguity/duplicate handling, immutable project scope revisions, owner correction, status/dependency validation and compact responsive UI/history. Existing runs, manual backlog entries, authentication/security policies and financial accounting are unchanged. The owner checkout and M5 review workspace remain preserved; M5 run `24464e57c4384cb38b5771eabe24fe1c` was not resumed. No paid model call or external repository mutation was performed for M6.

This phase is not a complete M6 estimation system. PDF/OCR, semantic BA/Architect analysis, repository implementation assessment, pricing/calibration, milestone cost forecasts, project financial authorization, shared budget reservation/reconciliation, reforecasting and integrated certification remain pending. Imports do not automatically become executable tasks and do not mark any requirement Verified. M5 success remains a prerequisite for M6 integrated live certification.

## Executed verification

- Cached dependency restore and full solution build: passed, zero warnings/errors.
- Full backend regression against isolated MongoDB on localhost:27058: 337 passed, nine Docker/live/remote opt-ins skipped. The baseline is protected master `ed6ebd9`; tests from unmerged later milestones are not counted here.
- Final focused intake regression after parser size/ambiguity hardening: 10 passed; ten tests cover 125 requirements, source identity, duplicate references, explicit TBD ambiguity, reviews, CAS conflicts, dependency cycles, cross-project IDs, revision history pagination, fresh-store persistence, owner/CSRF checks, unsupported files, strict UTF-8, cancellation, DOCX macros/DTD/expansion bounds and prevention of owner-fabricated Verified status.
- Frontend typecheck and production build: passed; existing bundle-size warning retained.
- Frontend unit/component tests: 54 passed, including four intake cases covering bounded 125-row rendering/search, honest empty state, unsupported files, confirmation/CSRF/revision-bound raw uploads and stale review conflict diagnostics.
- Playwright: five scenarios passed on dedicated ports 18388/18373 against disposable test-only services/databases; the final focused rerun also passed with explicit history traversal and expanded-history mobile overflow checks. The new scenario imports 125 source requirements through the real API, saves a review, reloads the persisted project, checks no run was created and verifies mobile viewport bounds. No provider/executor fake is loaded by production; deterministic adapters are restricted to test assemblies.
- Production Docker image `aep-platform:m6-intake-verified` built successfully; immutable config identity `sha256:61a6ace28f1acc63c257410fb091750690444bcf6a3679979627d1f668445a14`. No service was deployed or restarted. Source/TRX/report hashes and screenshots are recorded in the manifest. Generated baseline screenshots were retained under /tmp and restored from unchanged HEAD rather than included as unrelated source edits.

Failed intermediate compilation/empty-response/component-selector and expanded-history responsive checks are retained privately, corrected and excluded from passing claims. Parser tests exposed that an empty Results.Json response did not contain literal JSON null; the new endpoint now returns explicit null for an empty scope. A test-only timeout was adjusted for the large Ant Design review form; no production inspection threshold, budget or executor limit changed.

See [ADR 016](architecture/016-project-requirement-intake.md) for security review, exact bounds, storage behavior, supported formats, migration, rollback and remaining phases. Review screenshots: [desktop](ux-evidence/m6-intake-desktop.png) and [mobile](ux-evidence/m6-intake-mobile.png).

The final browser run retained development-only SignalR proxy EPIPE/reconnect and existing Ant Design deprecation diagnostics during reload/teardown; the persisted UI/history assertions passed. No exact-once streaming guarantee or new transport fix is claimed.
