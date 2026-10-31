# ADR 016 — bounded, immutable project requirements intake

Status: implemented first M6.1 slice; broader M6 estimation and certification pending.

## Context and decision

The existing requirements API stores mutable manual/model backlog records and verification links for engineering runs. It has no original document, stable document anchors or scope revision identity. M6 estimates and financial approvals need an immutable project scope; copying imported text into OriginalContext or automatically treating it as an authorized execution would lose that distinction.

Add `RequirementScope` and source-backed `ScopedRequirement` in Domain, the explicit `RequirementIntake` application use case, document extraction and scope store integration contracts, Infrastructure extraction/Mongo implementations, owner-only API endpoints and a compact project workspace tab. Legacy backlog records and execution contracts remain unchanged. Later M6 phases must map an explicitly approved scope/plan to run tasks; this phase does not automatically publish imported rows into the old execution backlog.

## Intake semantics

Supported formats are strict UTF-8 Markdown/TXT and macro-free DOCX. Maximum upload: 750000 bytes; extracted text: 250000 characters/20000 fragments; normalized scope: 2000000 UTF-8 JSON bytes; 1–500 candidates; 8000 characters per description; 500 immutable revisions per project. At the revision ceiling further edits fail closed and retain all history; archival/migration requires an explicit owner procedure. These are intake storage limits, not changes to executor budgets or tool limits.

PDF, images, scanned documents, DOC, DOCM and HTML uploads are explicitly rejected. A bounded process-isolated PDF parser/OCR pipeline is a remaining intake extension; no custom PDF interpretation or unbounded parser was introduced. Research verified the PdfPig API/package exists, but a library running in the API alone does not establish hard memory/time bounds for hostile compressed PDFs. No extra dependency was installed.

The literal parser recognizes explicit REQ/FR/NFR/R labels, numbered items, headings containing such labels and bullet markers. With no explicit markers, non-heading paragraphs become draft candidates and the uncertainty is prominently stated. Other introductory prose/headings stay in the complete extracted source. Nested bullets may be separate candidates; this is not advertised as a semantic BA assessment. The owner must review/correct candidates before estimation. Obvious TBD/TODO/TBC/as-needed markers and conflicting repeated labels are flagged; this heuristic is not claimed to detect all ambiguity. No acceptance criteria, dependency, complexity or repository evidence is fabricated. FR/NFR-based classification is explicitly marked as an inference pending owner confirmation.

Stable opaque requirement IDs derive from project, owner-selected stable document key and explicit source label. Unlabeled candidates use whitespace-normalized content identity. Duplicate comparison is case-sensitive, so distinct case-sensitive API identifiers are not merged. Exact normalized duplicates retain every original source reference/text. If a label changes meaning, its stable ID persists while its source/review revision changes. Reimports reset review for newly extracted candidates. Absent existing requirements remain blocked with original sources until the owner reconciles them or explicitly excludes them; previously reviewed exclusions remain visible. Nothing silently drops from scope.

Original file SHA-256, name, extracted text and line/paragraph references persist. Raw binary ZIP content is not retained; owner-supplied original bytes can be matched to the stored hash. Review changes create a new scope with immutable parent identity. Source references cannot be supplied or changed by the review endpoint. Owner implementation classifications are unverified assertions. The API rejects direct Verified transitions; later source-revision/acceptance evidence policy is required to certify implementation. ReadyForEstimation is a review indicator, never financial approval or implementation certification.

## Persistence and concurrency

Two additive MongoDB collections: `requirement_scope_heads_v1` and `requirement_scope_snapshots_v1`, each with the built-in unique `_id` index. No production data rewrite, destructive migration or replica-set transaction requirement. A complete immutable snapshot is inserted before a head CAS on project ID, revision and monotonic version. On initial import, unique head insertion arbitrates concurrent owners. Candidate insertion alone cannot authorize a revision; only the winning head and its parent chain are visible through public history/revision reads. Prewritten losing/interrupted candidates remain preserved for reconciliation, not automatically deleted. Duplicate candidate insertion is idempotent; an interrupted ambiguous head update requires reading the current scope before retrying. A crash after CAS retains the entire snapshot and lineage.

History walks parent identities with bounded metadata projections, loads at most twenty summaries and uses an exact revision cursor. It does not repeatedly load source documents. A selected revision loads its full source exactly once after committed-lineage validation. New store instances recover current and historic revisions from MongoDB. Tests verify persistence through fresh store construction, not a physical MongoDB power-loss experiment. Imported scopes have no run lifecycle foreign-key deletion; cancelling an execution cannot delete them.

## Security review

Owner-only authorization, existing CSRF header/origin checks, cookie persistence, rate limits and ProblemDetails are reused. Upload names/keys, extensions, strict UTF-8, controls, lengths, nulls, enums, arrays and same-scope dependency IDs are validated. Dependencies cannot form cycles. Review CAS prevents stale UI writes; the UI pins the revision captured when opening a review/import confirmation rather than silently adopting a background refresh. Unknown/project-mismatched revisions are not readable as another project's scope.

DOCX validates OOXML main content type, ZIP signature, duplicate/path-like entries, active/macro/embedded content and compressed/uncompressed entry/count ceilings. Only word/document.xml text is read; relationships, external links, images and fonts are never fetched/executed. XML DTD/entity resolution is prohibited, characters and expanded entry reads bounded, CancellationToken propagated. No ZIP entry is extracted to the filesystem. No upload invokes a shell or model. Documents are rendered as escaped plain text, with source preview chunks of 8000 characters, not injected HTML. Private documents remain in the project database and are not sent to third-party models by this feature.

## UI and compatibility

The Requirements intake tab is separate from the existing run-backed Requirements tab. It shows draft/owner-reviewed states, searchable twenty-row pages, filters, provenance, bounded source preview, historical read-only revisions and explicit import confirmation. It has no fake cost, financial approval or Verified control. Models, sandbox profiles, repository allowlists, billing/accounting and executor controls are unchanged. No existing AEP run is resumed and no external source repository is modified.

## Deployment and rollback

Deploy the API/frontend artifact normally. Collections are created lazily on the first explicit project import. Existing projects do not require a migration or any automatic document/model processing. Back up the two collections together with project data; retain snapshots and head identities. On rollback, stop intake writes and deploy the previous application image while retaining both collections. Old code ignores them, and legacy project/run data remains compatible. Do not delete unreferenced snapshots or rewrite heads to recover a failed edit; compare committed history and preserve candidate evidence for owner reconciliation.

## Remaining phases

M6.2 must provide strict BA/Architect assessments, authorized cached repository evidence, deterministic versioned price calculations, three assumption-labeled scenarios, historical success/failure calibration and requirement-complete acyclic milestone plans. M6.3 must bind immutable owner approvals to scope/plan/catalog/model identities and enforce shared atomic reservations/reconciliation in the billable runtime, including ambiguous outcomes and crash recovery. M6.4 adds financial/coverage/reforecast dashboards; M6.5 certifies an approved subset of 120+ requirements after successful M5 certification. No current document import or owner review authorizes those external operations or additional spending.
