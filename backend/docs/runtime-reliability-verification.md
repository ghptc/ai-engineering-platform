# Runtime reliability and live engineering flow verification

Date: 2026-10-29. Branch: `feat/agent-runtime-reliability`. Scope and root-cause evidence: [ADR 008](architecture/008-agent-runtime-reliability.md). This report describes the implementation committed with it; approval/merge remain owner decisions.

## Revision and evidence

Source/test manifest SHA-256: `c5810930518a557c3feb7a60ac154a10da9c2ccf3743a589a63c64c07435458c`. Compute by sorting `rg --files backend/src backend/tests frontend/src frontend/tests` plus `frontend/playwright.config.ts`, deduplicating, and hashing each UTF-8 path, NUL, file bytes, NUL in order. Documentation/screenshots are excluded. The commit carrying this report identifies the reviewed source revision.

| Verification | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings/errors |
| Full .NET solution tests, isolated MongoDB | 123 passed; 7 opt-in tests skipped |
| `npm ci --prefix frontend` | Passed; audit reported zero vulnerabilities |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 30 passed |
| `npm run build --prefix frontend` | Passed; existing large Monaco/application bundle warnings remain |
| `npm run e2e --prefix frontend` | 2 browser workflows passed, including console tool evidence and responsive history navigation |
| Opt-in real local executor suite | 22 passed; 1 credentialed disposable GitHub test skipped |
| Application Docker build | Passed using dedicated tag `ai-engineering-platform:runtime-reliability`; owner local image/container not replaced |
| Live OpenAI and credentialed disposable GitHub agent cycle | Not run; no live provider success is claimed |

Final raw command logs were retained locally under `/tmp/aep-runtime-*.log`. Integration/browser commands used `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27029`, with unique test database names on a newly created disposable temporary-storage server. Browser ports were dedicated and non-reused. Real executor invocation used `RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:ba3d80d3f5077a1eaac0a3627bb3b9723684f39d391f9cf996fc90dbf90e44be dotnet test backend/tests/AIEngineeringPlatform.LocalExecutor.Tests --no-restore --no-build`, after compiling that revision. Real local Git, Docker isolation, limits, cleanup, Unix transport/authentication and restart tests passed separately from deterministic provider/GitHub tests.

An earlier shared test MongoDB exited with code 139 after logging `Too many open files` during index creation. Its data was not cleared or modified to pass tests. Verification moved to a fresh disposable MongoDB with temporary storage and a bounded higher file-descriptor allowance. An overlapping .NET rebuild also encountered a locked generated static-assets cache; the executor rerun used the already verified build. These failed attempts are not reported as successful test evidence. The owner application and production MongoDB were neither restarted nor changed.

## Coverage and review

Application regression exercises every specialist through repeated model replies and 400 distinct tool evidence records, preserving current acceptance criteria, approval policy, output reservation and explicit overflow diagnostics. Existing focused-task/memory retrieval, source snapshot, QA and instruction-revision tests remain passing.

Integration tests exercise QA → Backend, QA → Frontend, Review → Backend, Review → Frontend, developer-specific correction followed by repeated tests/QA/review, persisted plan/counter recovery using a new store, published-review rework with another owner decision, limit exhaustion/extension/continuation with stable checkpoint IDs, failed/retried tool counts, secret redaction and large separated stdout/stderr transport limits. Domain tests cover iteration limits, bounded retries per stage and policy safety. Existing persisted ordered replay, SignalR reconnect/deduplication, usage and revision-evidence tests pass.

Frontend tests cover persisted return paths, failed states without working animation, capacity warnings, actual tool evidence attribution, paged large output and absence of arbitrary shell controls. Browser tests exercise focused execution, owner delivery approval, opening a specialist's read-only console, persisted WriteFile output, bounded history disclosure, responsive SVG navigation, dark/light views and reload recovery.

Manual diff review covered domain dependency direction, optimistic versions, extension outbox events, deterministic operation keys, targeted routes, success-only QA transitions, protected context diagnostics, bounded output projections and sanitization at tool/detail/live/error boundaries. No allowlist, credential file, production adapter selection, generated-code host execution, Docker isolation flag or protected-branch merge behavior was weakened. Refreshed browser screenshots are in [ux-evidence](ux-evidence/).

## User-visible changes and limits

Execution and collaboration use AgentFlow and AgentConsole rather than static tiles. The primary workspace leads with the flow/console; saved conversations are collapsed, timelines/handoffs are bounded, full JSON remains on demand, and capacity breakdown/extensions live in a separate Capacity tab. Tool failure diagnostics include actual available profile/command, revision, exit and streams. The task launcher offers explicit presets without changing saved project defaults or silently increasing spending.

Operations update on completion; subprocess bytes are not streamed live. Legacy records may lack reliable agent attribution, separate stdout/stderr, exit code or explicit transition metadata; the UI reports missing evidence instead of inventing it. Per-agent budgets are advisory and share the execution pool. Counters describe durable completed model/tool receipts; an external request failing before a receipt cannot provide authoritative provider usage. Legacy unfocused runs with intrinsically oversized protected requirements/instructions still need a smaller focused task. Independently reconciled external feature branch delivery remains blocked pending explicit update handling. Real provider quality, billing and credentialed remote delivery were not verified by this milestone's deterministic adapters.
