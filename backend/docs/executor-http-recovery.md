# Executor HTTP failure recovery — 2026-10-30

## DevPulse investigation

Run `4822e0ce83ee4b7ab4ad4cfca2c8238c` was inspected read-only in the local MongoDB, application container logs, executor configuration and durable receipt directory. No run resume, lease change, workspace write, daemon restart or owner configuration change was performed.

Persisted backend task response `resp_01b145197b15624d016ae4264f9f2087d1ab072b738f0b03fa` requests `RunCommand` with `{"profile":"build"}` after inspecting the four project manifests. Tool-start events at sequences 1562, 1580 and 1598 identify the same operation. Worker logs confirm `EnsureSuccessStatusCode` failed three times; the old generic HTTP stage retry repeated it twice.

**Historical HTTP status cannot be established from surviving evidence.** The original adapter discarded the status/body and worker logs recorded only the exception type/stack. Available daemon logs do not contain the incident interval. Do not present a reconstructed status as observed evidence. The configured build profile points to pinned image `sha256:ba3d80d3f5077a1eaac0a3627bb3b9723684f39d391f9cf996fc90dbf90e44be`, which Docker currently reports missing. The old daemon maps Docker creation `InvalidOperationException` to 409. This is a plausible explanation, not proof of historical status or image availability at failure time. Administrator diagnostics from that interval are needed to close this evidence gap.

The four successful WriteFile receipts remain intact:

| Operation ID | Workspace revision |
| --- | --- |
| fa57d94c2a4abe8a3c40feab695ae393af0b088a13056055a79499107e6fe04d | 6d5db09a3fae1d844cf05fde5971f40854fc48962a1e309aa7a6cee680515a26 |
| 7b304d5473957ff2698ac993dd166c4b36c1b485efe3f9e25dd17336f299b1cd | 446ce0de2ea83a2bab43579d2a1217092785f0981a36cf3cd2f9d202d16c8a30 |
| 18cee16d400895bd46473a5b3d2a0a684fafea8793d7b35c8f036677c2c1c769 | 5975a8093e847a032581114fbeb5ce54e23c29b3c63b623e2dec85c2b1d15c22 |
| 0cddbb60ca56de8b1d56591f32e1b2d9ece774dde600d2fa4be346ddc862fa8a | c1ef94223a16832e5e7d2588c4dc8e893acacc09ca73948ba74de85b31b864cb |

## Contract and correction

The actual daemon authenticates every request, requires the Idempotency-Key to match OperationId, and returns successful durable receipts as SandboxResponse. Previously it mapped invalid arguments/JSON/missing files to 400, authorization to 403, configuration to 503 and InvalidOperationException to 409. These ProblemDetails included exception text; 401 has no body.

The adapter now reads at most 4097 error bytes, accepts only recognized machine codes, discards all arbitrary details, and emits locally authored typed errors with status and operation ID. No raw paths, headers, HTML or exception messages reach run errors. Categories distinguish arguments, authorization, repository access, profiles, workspace conflicts, timeout, rate limiting and infrastructure. New daemon profile restrictions return 422; repository failures have a repository_access code; Docker infrastructure failures return 503. Existing invalid-argument, auth and conflict behavior remains classified by status. Structured daemon logs retain operation ID/status/category for typed rejections.

Executor failures persist an ExecutorOperationBlocked event before the worker pauses the run with actionable UI guidance. They never enter generic automatic stage retries. Pure inspection requests alone can retry 429/502/504, or 503 with Retry-After, at most twice and never above the run's configured retry allowance. Retry-After over two seconds stops automatic retries. All attempts share the original request, operation ID, idempotency key, fence, owner and total timeout. Default retry allowance is zero for callers that do not opt in.

Mutating operations, tests and commands never automatically retry, including transport errors and ambiguous timeouts. Human recovery must reconcile durable receipts, workspace revision and any remote Git outcome before explicit resume. Missing receipts alone do not prove absence of side effects. No new operation ID should be minted to bypass ambiguity. This change does not automatically repair the owner profile/image, replay the DevPulse run or deliver its unfinished work.

## Verification

See the dated entry in verification.md for results. HTTP-handler tests use test assemblies only; real Docker/Unix-daemon tests use disposable local Git repositories and a pinned image. No live OpenAI or GitHub agent cycle is claimed. Human review and approval remain required before merging or deploying.
