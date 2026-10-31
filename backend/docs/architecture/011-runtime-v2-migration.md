# ADR 011: Incremental runtime V2 and independent deployment

Status: M1 implementation under verification; M2–M5 pending. Baseline: protected master `8c05538`.

## Audit

The platform already has role definitions, structured Responses outputs, task-scoped context assembly/compaction, durable operation identities/receipts, optimistic leases/fences, revision-aware quality gates, bounded inspection recovery, rework targets, MongoDB outbox/history and a compact SignalR execution UI. Those mechanisms remain authoritative. There is no justification for replacing them with an autonomous shell runner.

Gaps at the baseline: no Compose manifest; manually started host executor and manually repaired image pins; cookie-only tickets with ephemeral container keys, nonpersistent browser cookies and no server revocation; worker lifetime coupled to API; in-process SignalR publication; no independent runtime/task-message abstraction or worker-specific task checkpoints. Existing specialist output must not be relabeled as independently scheduled work.

## Deployment and authentication decision (M1)

Compose builds the UI into the API image, runs a separate worker service, private MongoDB and the included trusted executor as a separate container. Only that executor receives the Docker socket. This is a host-equivalent privileged boundary even without `privileged: true`; the application and workers never receive the socket. Archive-only workloads retain the existing no-network, no-mount, non-root, resource and cleanup checks. Owner-built runtime tags resolve to immutable image IDs before profiles become available, and a separate retained pin prevents a moving-tag rebuild from removing an active digest. Managed executor cleanup labels use a persistent per-volume instance identity, so identical paths in independent containers cannot share cleanup ownership. Existing JSON-managed/Unix/HTTPS deployments remain supported.

The owner configures an allowlist in scalar `EXECUTOR_REPOSITORIES` entries `owner/name@installation:branch`. Model tools cannot edit this list or select images/commands. Empty allowlists permit administration but no repository work. Credentials remain server-side and outside workloads. Source preservation takes precedence over retention: daemon startup no longer deletes expired workspaces. Archival/automated capacity reclamation requires a separate safe lifecycle milestone.

Authentication uses ASP.NET Core cookies with an encrypted opaque reference and a Mongo session record keyed by a SHA-256 session identifier. Protected tickets are encrypted independently with Data Protection; keys persist on an owner-restricted volume. Each login creates 256 random bits, revokes the previous cookie session, records its authentication time and expires exactly eight hours later. No renewal extends it. TTL is only garbage collection; every lookup enforces expiration. Logout deletes the server record; credential rotation rejects old tickets. Session data never enters project/history/context records. Existing cookie-jar API clients retain their login/logout endpoints; there was no bearer authentication scheme at baseline.

Local HTTP is explicit and restricted to loopback publication/hosts. HTTPS uses Secure cookies. SameSite Strict plus the existing non-simple request header and explicit same-origin Origin checking protect mutations. Login rate limiting remains enabled. No CORS policy is introduced.

A worker crash does not remove the UI or session. API replicas relay bounded persisted event windows to their own SignalR connections. Reconnect history remains authoritative and workers retain their existing lease/checkpoint recovery. Engineering execution loops and workspace operations run in the worker service in Compose. Legacy conversational planning replies remain API-hosted until the runtime migration.

## Next milestones

M2 adds `IAgentRuntime` and strict versioned engineering messages alongside compatibility contracts, with durable identifiers, bounded context/checkpoints and evidence provenance. M3 migrates Backend/Test task workers first, preserving budgets, idempotency, source-revision handoffs and targeted rework. M4 migrates other roles and exposes actual persisted task status; parallelism requires independent workspace ownership/dependencies. M5 is live certification through the selected runtime, separate from deterministic tests.

Official Codex documentation confirms SDK/noninteractive interfaces and API-key automation, but that alone does not establish compatibility with AEP's fixed authorized tool gateway and mutation reconciliation. Responses remains the selected initial runtime; no embedded Codex CLI, borrowed ChatGPT session or shell permission bypass is introduced. Sources: https://developers.openai.com/codex/sdk and https://developers.openai.com/codex/noninteractive (checked 2026-10-31). ASP.NET session persistence follows https://learn.microsoft.com/en-us/aspnet/core/security/authentication/cookie?view=aspnetcore-10.0 .

## Certification authorization

The owner authorized only `ghptc/aep-sandbox-m8-20261028-3a0ed9`, GitHub App installation `176070757`, maximum aggregate OpenAI spend USD 10, on a new isolated feature branch. Verify installation access and current repository state before any modification. AEP agents must author target source/tests; Codex may configure, diagnose and verify the harness. Preserve prior source, branches, receipts and checkpoints. Prepare a draft PR; never merge automatically. This authorization does not cover other customer projects.
