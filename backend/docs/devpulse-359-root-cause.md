# DevPulse implementation failure: executor rejection followed by append replay failure

Run: `359ac51313d64d86887758c67335cb39`. Investigation date: 2026-10-30. Application base: merged PR #15, `39ed6e7`.

## Evidence collected before behavior changes

Production MongoDB, workspace/receipts, executor configuration, daemon identity and incident-window application logs were inspected read-only. The incident daemon writes to `/dev/pts/17`; its incident output is not in the surviving daemon log file. Worker logs contain exception types/stacks but no status or Mongo write code. Historical error bodies cannot be quoted as observed evidence.

The persisted `ExecutorOperationBlocked` event at sequence 1801 establishes **HTTP 409**, `WorkspaceConflict`, and `outcomeUnknown=True` for operation `b47d80645236b171197a51f982f4997f6830d658f3a98864cfb2391751755b6f`. The persisted backend model task requests exactly `RunCommand {"profile":"build"}`. The failure event ID is `executor-failure-` followed by that operation ID. There is no successful `tool` record or executor operation receipt with that operation ID.

| Sequence | UTC time | Persisted transition/evidence |
| --- | --- | --- |
| 1801 | 05:33:55.004778 | First failure event, HTTP 409 |
| 1802 | 05:33:55.0059295 | Implementing → Paused |
| 1803 | 05:39:44.4917069 | Paused → Implementing |
| 1818 | 05:39:44.5989879 | Implementing → Failed after Mongo append error |
| 1820 | 05:39:50.9806988 | Failed → Implementing |
| 1835 | 05:39:51.6961761 | Implementing → Failed after repeated append error |

The run has one failed-stage retry recorded. The worker does not automatically retry MongoWriteException; these recorded resume/retry transitions must not be described as automatic HTTP retries. Cached model tasks and tool receipts cause the same operation ID to be reused.

The `records` collection has the default unique `_id_`, a unique `ExternalId_1` partial index applying only to string ExternalId values, and nonunique activity/query indexes. The blocked event's ExternalId is null. No collection validator is configured. Its ID does **not** collide with an earlier successful tool receipt: it collides with its own earlier failure event.

An isolated replay of the exported production failure document, changing only its retry timestamp/sequence, returned MongoDB **11000**, index **`_id_`**. A regression executed against unchanged PR #15 code reproduced MongoWriteException at AppendAsync line 104. The duplicate handler normalizes Sequence but compares the rest of serialized Data byte-for-byte, including newly generated CreatedAt. Therefore the retry's duplicate ID is not accepted as an identical append. The catch in ToolAsync then loses the original ExecutorFailureException because its own evidence append throws. The string-based failure classifier can label the secondary error LocalExecutor because the Mongo message contains `executor-failure-...`; that label is not evidence of a new executor fault.

## Executor and actual build-profile findings

The application was updated, but the separate host daemon was not: the active daemon loads `backend/.local-runtime/daemon/AIEngineeringPlatform.LocalExecutor.dll`, timestamp 2026-10-29 10:28, SHA-256 `080c79e4fe2fccf63a8bf5204bf3c526ad001c1822f4997ad5ac20eb48bf6a30`. It lacks the newer failure contract/capability advertisement. Merging application code does not republish or restart this independent trust boundary.

The configured build command is `dotnet build backend --no-restore`, pinned to image `sha256:ba3d80d3f5077a1eaac0a3627bb3b9723684f39d391f9cf996fc90dbf90e44be`. That image is absent. The real workspace contains README.md and only these generated project manifests:

- `src/DevPulse.Core/DevPulse.Core.csproj`
- `src/DevPulse.Api/DevPulse.Api.csproj`

There is no backend directory, root solution, restored assets, or API entry point. The two successful WriteFile receipts remain unchanged:

| Operation | Revision |
| --- | --- |
| b4f25ea95ff0ee9289415f045143f33439467c587fd71b61997e0c0198436a43 | 7145a08f5ef7b94a19c51ff0792a67d2025c452e72537d4e14b79bf931bf4963 |
| dc71cde913cbadc2abd8909e9d236adc4b15a9a8a49a7e1aab1e3791d0e33ad7 | c9ed95ec95c2e2cffcff605690ff2f2c963a25907268f57465ff842be1d12cb2 |

The existing daemon binary, reviewed profile and copied workspace were exercised in **four isolated directories** with independent state/socket/token, no App credentials, no changes to the original run, and no generated code executed on the workstation. Workloads used the daemon's Docker isolation and archive input:

| Experiment | Observed result |
| --- | --- |
| Original image and command | HTTP 409 ProblemDetails: Docker could not create workload because the pinned image does not exist |
| Available image, original command | HTTP 200 failed tool result; MSB1009, missing project target backend |
| Available image, explicit Core target with offline restore | HTTP 200 successful build, zero warnings/errors |
| Available image, explicit API target with offline restore | HTTP 200 failed tool result; CS5001, missing Main/entry point |

The available verified image was `sha256:7742feb1d0b1193a8a6af2efa05805032d1f30c50d0f3dbba4460909f81e48a9`. All four experiments retained source revision `c9ed95ec95c2e2cffcff605690ff2f2c963a25907268f57465ff842be1d12cb2` when a tool response was produced. The original daemon/configuration reproduce the recorded rejection exactly; the historical daemon error text itself was not retained. Missing image, invalid target and incomplete source are separate issues. Core compilation does not establish a working DevPulse application, passing tests, QA approval or delivery.

## Platform corrections

- Append compares immutable JSON semantics while ignoring only append-assigned Sequence and retry-local CreatedAt. First record/time/sequence remain authoritative. Different kind, payload, identity, status or metadata still conflict. Indexes are unchanged; no existing data is modified. A private insertion result preserves webhook deduplication, including concurrent identical deliveries.
- Distinct executor attempts use failure-event IDs including the optimistic run version. Operation IDs, idempotency keys and successful tool IDs stay unchanged. Legacy failure records remain readable. Repeated same-attempt appends are idempotent.
- Secondary evidence persistence failures carry the original executor failure plus sanitized Mongo code/index/conflict information. Worker logs these structured fields before making further database calls, and pauses with the primary category and both failure descriptions. Driver messages and rejected document contents are not logged.
- Fixed .NET profiles must match their actual project/solution/directory target, for commands as well as tests. Profile prerequisites are enforced. A missing target is a known-not-started 422 `profile_target_missing`. Pinned-image inspection before Docker create yields a known-not-started 503 `image_unavailable`. The adapter retains these bounded machine reasons without copying arbitrary error text.
- Unix readiness requires contractVersion 2. Implementation checks readiness so an outdated daemon cannot silently continue after an application-only upgrade. The authenticated readiness response remains bounded.
- Explicit resume cannot resend an operation whose persisted failure has an uncertain outcome. The guard reads all blocked-event pages and honors old event IDs. An existing successful tool receipt is reused first. Pure inspections retain their existing bounded retry behavior. Known-not-started errors can be retried explicitly after administrator correction; no automatic command retry was introduced.

## Recovery instructions and limits

1. Review and merge/deploy the platform correction through the normal human approval process. Publish the host daemon from the same revision to a separate directory, stop/restart it through its owner-managed service, and confirm authenticated readiness reports contractVersion 2. Preserve its state directory, receipts, ownership controls and socket permissions. Do not replace only the application container and assume the daemon changed.
2. Provision the exact missing pinned image if it is still the intended reviewed image, or explicitly review an available replacement. Never fall back to a mutable tag or automatic pull. The verified replacement above is local evidence, not a portable deployment promise.
3. Review `devpulse-359-build-profile.json` as a **build-profile replacement candidate**, not a full executor configuration. It targets the actual API project and permits the isolated framework restore needed by a fresh archive. It is expected to fail CS5001 until the engineer implements the API entry point. Existing backend/all/frontend test profiles also need administrator review against the eventual real project/test layout; a build is not test evidence. No owner configuration was silently overwritten.
4. For this legacy run, preserve the existing failure event and both writes. A missing receipt alone does not prove that a historical side effect never happened. The new guard will explain why a plain resume cannot replay its uncertain operation. This PR does not introduce an authenticated owner-attestation/receipt-import workflow; such reconciliation requires a separately reviewed recovery action. Do not edit/delete the blocked event, reset the workspace, mint a replacement operation ID, or manufacture a successful receipt to get past the guard.
5. After a safe recovery decision, continue implementation in the existing workspace/revision, then require actual API/frontend/test source changes, isolated tests, QA, diff review and human delivery approval. No DevPulse commit/PR delivery or live model cycle was performed by this investigation.

Verification results, source fingerprint and local evidence paths are recorded in verification.md. The production run remains Failed at version 61; its execution records, successful writes, state and receipt files were preserved. No production daemon restart, profile update, database delete, workspace reset, deployment or automatic merge was performed.
