# Dependency acquisition recovery after PR #30

## Incident and exact reproduction

The protected incident is `ghptc/pdf-tools`, run `e5a957b89f044c219fb7448adc5e35cb`, request `e31031feefd2066e1f40dbf3b2408fcf5e821ab3fb8e359d4ae78c11296d2b39`. No operation was submitted to that run, its repository was not edited, and its five acquisition markers (one current and four archived) were retained. No state directory, checkpoint, receipt, budget, deadline or Docker volume was deleted or reset.

A separate source snapshot of merged PR #30 (`e2045ec`) reproduced the failure against a disposable local bare Git repository initialized with only README.md and the exact packages `pdf-lib@1.17.1`, `pdfjs-dist@5.4.624`, and `@playwright/test@1.56.1`. The actual exception was:

```text
System.InvalidOperationException: Registry response exceeds byte bound.
  at DependencyExecutor.ReadBoundedAsync(...)
  at DependencyExecutor.ResolveAsync(...)
Bounded official registry path: /playwright
```

The baseline snapshot had only test instrumentation to retain the validated registry path on the exception. The application source and protected run were not substituted during reproduction. The assertion checks the exact exception type and message; it is not inferred from the generic HTTP response.

PR #30 requests abbreviated npm metadata for the whole package history, without negotiating compression. The measured `/playwright` metadata was 8,188,157 bytes, exceeding the 8,000,000-byte individual metadata response limit. Other measured metadata also exceeded this limit (`playwright-core`: 8,450,295; `typescript`: 8,768,978). The first failing package can depend on npm request ordering and current registry history. These are measurements during this investigation, not immutable package metadata sizes.

The executor maps that `InvalidOperationException` to HTTP 409 with a generic `executor_rejected` problem. The dependency HTTP client discarded the actual classification, and governance treated every failure as an uncertain acquisition. Thus the request stayed Resolving, no resolution/grant was produced, and the UI continued offering acquisition retries. The old marker was written before several preflight checks, so marker presence alone did not prove registry access had started.

## Actual pinned image

The active executor started at `2026-11-01T16:54:22.050401682Z`. Its protected run's capability receipts were written at `16:56:20.459208254Z` and `16:57:03.254996579Z`, inside that same daemon lifetime. Every advertised profile in those receipts pins:

```text
sha256:8d4b66a539000a4357faa7dac83f1f020a0787e24f8be074f4ba3a5e7df5316c
```

An unstarted disposable container from that exact image confirmed `/opt/aep/dependencies.py` exists and uses the 32 MiB broker input bound. The much older image lacking the helper was not assumed to be selected. The moving runtime tag was not used as evidence of the active daemon's selection, and the daemon was not restarted or repinned. New readiness responses additionally report the configured immutable runtime IDs.

## Correction

- Negotiate gzip from the official npm registry. Count bounded wire bytes against the existing individual response and 64,000,000-byte acquisition limits; separately cap decoded responses at 20,000,000 bytes. Redirects, proxies, credentials, arbitrary origins and agent egress remain disabled.
- Seed a README-only frontend from the existing approved bootstrap lock, rather than unnecessarily resolving its entire transitive history again. The catalog and exact requested versions remain unchanged.
- Acquire tarballs directly through the existing broker and verify SHA-512 before writing content-addressed cache blobs. Avoid repeated `npm cache add` subprocesses and redundant acquisition of blobs already verified in the immutable runtime image.
- Retain the full graph for owner approval, including optional entries for other platforms. Export only new blobs needed by the runtime OS/CPU/libc. Receipts distinguish runtime-provided blobs from incompatible optional entries. Offline use of either optimization requires exactly the resolver image digest.
- Version new receipts as `npm-v2`; require helper contract 2 before registry access. Nullable cache provenance fields are omitted when absent, preserving the serialization and fingerprints of old receipts.
- Return bounded structured ProblemDetails and structured logs: phase, stable code, acquisition-started flag, outcome-known flag and permitted recovery action. Raw exception text, npm output, filesystem paths and secrets are not forwarded. Preserve inner exceptions only for local diagnosis. Validate classifications on the authenticated HTTP client before exposing them to governance.
- Persist known failures only after independent cleanup and absence verification. Governance records ResolutionFailed and retains the owner audit. Transport ambiguity and unverified cleanup remain Resolving with Reconcile required. Cleanup verifies the executor ownership label; helper names include executor ownership as well as request identity.
- Fence uncertain persisted markers before any fallible image/workspace preflight. A preflight failure cannot overwrite an older uncertainty with a known-failure receipt. Explicit reconciliation checks both legacy and ownership-scoped helper names and never removes a live/created helper or original marker. Only confirmed absence permits recording an interrupted attempt as failed.
- Remove blind RetryResolution from both server decisions and owner controls. Known failure requires administrator remediation and a separate scoped owner Resolve decision with the current optimistic version and remaining original capacity. No automatic retry, grant, resume, policy extension or delivery occurs.

## Verification and boundaries

Sanitized evidence is recorded in `evidence/dependency-acquisition-recovery/`. The source fingerprint in the verification evidence binds checks to the implementation and tests, independently of documentation-only updates. Final command results are recorded there after verification.

The exact-combination fixture resolves the complete graph, persists its receipt and owner grant, prepares the approved manifest/lock, and checks the default Git HEAD remains unchanged. It then runs native offline npm and exercises PDF generation using the requested packages in a disposable three-package smoke project inside the same isolated executor. Optional packages are omitted in that smoke; it does not certify Playwright browser binaries, PDF Tools behavior, or a complete application build.

An attempted full bootstrap-plus-PDF installation exceeded the unchanged 256 MB workspace tmpfs (ENOSPC). A 512 MB full-install attempt also exited without useful output; its OOM cause was not established. These are verification limits, not permission to raise the protected run's resources. Acquisition and bounded smoke are separate evidence. No claim is made that the complete graph fits a full installation under the current workspace limit.

Initial broad MongoDB checks failed because the temporary test servers created during this investigation inherited a soft open-file limit of 1,024. Their logs explicitly reported `Too many open files`; measured descriptor counts were 1,012 and 1,004. Production Compose already sets 65,536. The failed test servers were stopped without removing their data or volumes. A separate isolated test server on port 27030 was started with `--ulimit nofile=65536:65536`. The suite then passed; its observed descriptor count exceeded 1,700 without connection failure. CPU or memory exhaustion was not asserted as the cause.

Verification runs are sequential. Real Docker/Git/npm evidence is reported separately from deterministic browser adapters. Live OpenAI, GitHub App delivery and remote disposable Git opt-ins remain unverified/skipped. The owner's application, worker, executor and database services were not replaced. Deployment of the application, daemon and compatible runtime requires an owner-managed upgrade; this change does not authorize replay or resumption of the incident run.

### Final results

- Backend: 450 passed, 3 skipped, 0 failed (Application 93, Architecture 2, Domain 19, Integration 249, LocalExecutor 87). The final LocalExecutor rerun after receipt-preservation/phase-reporting changes also passed 87, with the one remote Git opt-in skipped.
- Frontend: npm ci, typecheck and production build passed; 64 unit tests passed. The existing large-chunk build warning remains.
- Browser: all 7 tests passed using fresh dedicated hosts on ports 15488/15573 and the isolated test database. Dependency approvals survived reload without automatic resume.
- Docker: application, separately tagged daemon and separately tagged runtime builds passed. Real Docker isolation, timeout, output, cleanup, offline npm, local Git, uncertainty fencing and pinned-helper compatibility tests ran successfully.
- Preservation: the final read-only hash inventory matched for all 44 protected files, including all five acquisition markers. Owner Compose services remained healthy and were not replaced. Temporary test databases were stopped with their data/volumes preserved.

See [verification.json](evidence/dependency-acquisition-recovery/verification.json) for source fingerprint, image IDs, per-suite counts, evidence hashes and limits. These results do not certify the full bootstrap/PDF graph fits the unchanged workspace, live external delivery, or deployment of this change.
