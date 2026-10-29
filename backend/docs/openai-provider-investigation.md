# OpenAI provider parsing and cancellation investigation — 2026-10-29

## Persisted evidence and limits of the conclusion

Run `c4c85d98eec34bc199ca2b1551cacf37`, project DevPulse, was inspected read-only in the existing `aep-local-mongo` database, together with the existing application container's worker diagnostics. The source baseline is `d891a3a`. Neither the run nor the owner runtime configuration was changed or resumed.

At that baseline, `OpenAIProvider.cs:74` awaits `ResponsesClient.CreateResponseAsync`; line **79 parses `response.GetOutputText()` as the top-level structured agent result**. Line 81 separately parses the JSON-encoded strings inside `toolCalls[].arguments`. The Implementing stack trace points directly to line 79, without the LINQ projection frames associated with line 81. This is an agent output parsing failure, not evidence of a nested tool-argument failure or DevPulse compilation failure.

The failed output, failed response ID, request ID, response status, token usage and parser message were not persisted. `StoredOutputEnabled = false` was already configured. Consequently, the precise bytes cannot be reconstructed: malformed syntax, truncation, empty output and unexpected text cannot be distinguished retrospectively. A schema incompatibility or particular truncation reason is **not established**. The old incomplete/error check had been passed before parsing, but did not verify completed status or output item types.

Planning timing (UTC):

| AgentStarted | RunFailed | Elapsed |
| --- | --- | --- |
| 17:46:31.617 | 17:48:01.620 | 90.003 seconds |
| 17:48:03.645 | 17:49:33.646 | 90.001 seconds |

Both worker warnings report `TaskCanceledException` at line 74. The provider creates its own linked cancellation source with a default 90-second deadline. The run started at 17:45:10.891 and had a 60-minute duration limit; exhaustion of that limit is excluded by timing. The worker's caller-cancellation filter did not handle these exceptions, and stage retries proceeded twice. These observations strongly support the provider deadline rather than caller cancellation. The old logging omitted cancellation source/inner exception and request metadata, so an SDK/HTTP timeout or underlying transport condition cannot be conclusively separated retrospectively. A subsequent Architect request succeeded in 83.145 seconds, at 17:51:00.812.

The Backend Engineer's final request started at 17:51:16.081 and failed parsing at 17:51:28.156. Persisted inventory: 8 completed model tasks, 8 messages, 8 handoffs, 3 artifacts and 16 successful tools. Every tool was an inspection (`ReadRepository`, `ReconcileRepository`, `ReadProjectMemory`, `ReadFile`, `ListFiles`, `InspectDiff`); there was no `WriteFile`, `RunCommand`, `RunTests`, commit or PR operation. All recorded non-null workspace revisions were `78ed296a2960f8a48d50231ae1fbd5dfd6fd3806da75079e63b6f35d910b3fec`. The last successful tool operation was `584d9b16f1072e095ee96a1ff2ba935ff1a10c871e71b5b3761995b0d948a8c6`. Existing recorded estimates cover only successful tasks: USD 0.218204 and 74,942 tokens; historical failed-request costs remain unknown.

## Implementation and recovery behavior

All seven engineering roles continue through the same provider. Strict Responses JSON schemas were already enabled and remain enabled. Validation now checks response completion, errors, incomplete reason, message/content types, nonempty output, required properties, types, allowed decisions/tools, object members, array bounds and numeric bounds before constructing results. JSON-encoded tool arguments must independently parse to objects. The entire batch is validated before returning any call for execution; partial valid batches are not executed. Refusals and unexpected content are rejected.

Application-owned `ProviderException`/`ProviderDiagnostics` distinguish malformed output, invalid schema, invalid tool JSON, incomplete response, empty/unexpected content, missing usage, HTTP errors, transport errors, provider timeout, SDK timeout and run deadline. Explicit caller cancellation propagates as cancellation, with a separate diagnostic category. Metadata includes configured model, response/request IDs, status, incomplete reason, attempt, HTTP status and usage when available. No raw provider output, SDK exception message, prompt, credential or tool arguments enter these diagnostics. IDs and status/reason values are bounded/allowlisted.

Retries apply only to model requests: transport failures and HTTP 429/500/502/503/504, within configurable retry count, the shared provider deadline, remaining run duration and remaining budget. SDK automatic retries are disabled. There are no built-in remote tools in these requests. Parsing failures, incomplete responses and ambiguous timeouts are **not** automatically repaired or retried; the owner inspects the visible typed reason and explicitly retries the failed run. This avoids replaying a stage due to response parsing. Existing task/tool checkpoint keys, operation IDs, revisions, artifacts, lease ownership checks and remote fencing remain unchanged.

Before sending, the provider persists a small `providerRequest` reservation. A separate immutable outcome settles it to known token cost or a conservative maximum when usage is unavailable. An interruption retains the reservation. Recovery uses persisted charges; successful tasks and their provider receipt are not charged twice. The usage page includes failed/uncertain requests. These are configured-price estimates, not confirmed billing. The global dashboard retains its existing run-checkpoint aggregation; an abruptly interrupted request may appear there only after a workflow usage refresh, while the usage ledger and provider budget checks already read the durable reservation.

Failure events are persisted before publication through the existing event path. The execution failure banner shows the typed reason; the provider panel reads sanitized durable metadata through an authenticated activity endpoint. Historical missing metadata remains unavailable. Current run values are refreshed before saving provider failures. No historical response reconstruction or automatic resume is claimed.

The official [Structured Outputs documentation](https://developers.openai.com/api/docs/guides/structured-outputs) informed strict-schema and incomplete/refusal handling. Structured output does not remove the need to check response status, refusals, or nested JSON strings.

## Verification and review

Source manifest SHA-256: `3bfc1c80dd665acf06ba899ac0faeceac4e98644d2a97164a07edcb4c40d9957`. Computation: sort changed/untracked `.cs`, `.ts`, `.tsx` paths; hash each UTF-8 path, NUL, file bytes, NUL in order. Documentation and generated screenshots are excluded. The delivery commit contains these exact sources.

The regression suite covers malformed/truncated/empty output, invalid nested argument JSON and non-object arguments, missing fields, incomplete/failed/nonterminal Responses, refusal, no messages, absent usage, all seven roles, explicit cancellation, provider timeout, SDK timeout, run deadline, transient transport, bounded HTTP retries, budget reservation, and successful structured outputs. A MongoDB-backed workflow test injects a provider failure after a successful write, explicitly retries, verifies the same write content/operation ID and artifacts, completes QA/review/approval, and asserts exactly one commit and PR operation. A separate persisted accounting/API test verifies outcome settlement and successful-task deduplication. Frontend tests verify metadata display and historical missing diagnostics.

Initial integration attempts caught test mistakes (failed runs use `retry`; approval delivery is asynchronous), a missing activity-kind allowlist entry, and a case-sensitive expected billing description; all were corrected. An intermediate disposable MongoDB exhausted its file-descriptor limit and stopped accepting connections. Final integration verification uses a fresh isolated MongoDB on `127.0.0.1:27029` with `nofile=65536`; owner databases and runtime configuration were untouched. Browser tests use dedicated, non-reused test-host ports. Generated screenshot updates are excluded from this fix.

Final command results and image identity are recorded in the verification report. Diff review covered cancellation filters, retry boundaries, conservative accounting, immutable reservations/outcomes, API allowlists, all-or-nothing batch parsing, existing checkpoint keys and the new UI. Staging excludes secrets, runtime configuration, generated artifacts and raw failed responses. Human PR review/merge approval remains pending; no automatic merge or deployment is performed.

Live OpenAI/GitHub App engineering cycles are unverified for this fix: the two explicit live-agent tests are skipped. Real local Docker workload success and feature-branch Git/PR delivery are separate evidence. The original failed response cannot be recovered, unknown charges remain conservative, and no new live DevPulse compilation result is claimed.
