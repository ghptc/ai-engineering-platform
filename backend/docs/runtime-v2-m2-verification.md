# Runtime V2 M2 verification

M2 adds the runtime boundary and strict communication contracts alongside the legacy execution path. It does not yet schedule independent workers or certify autonomous application delivery. Baseline: merged M1 at `057a40f`; branch `feat/runtime-v2-m2`.

The solution build passed with zero warnings/errors. Focused strict-message tests and Mongo-backed runtime tests passed before the complete regression. The final complete suite passed 312 tests: Application 71, Architecture 2, Domain 18, Integration 188 and LocalExecutor 33. Nine opt-in tests were skipped (two live provider tests and seven Docker/Git checks). Safe source hashes and final TRX counters/hashes are recorded in `evidence/runtime-v2-m2/verification.json`. No skipped test is certified as passing.

Coverage includes all eight schema kinds; unknown/missing/duplicate fields; version, sender and recipient authority; canonical enums and strict numeric types; payload bounds; zero-test and duplicate-evidence rejection; scoped context persistence; atomic duplicate dispatch; checkpoint reuse across runtime recreation; no new provider request when reading a completed result after budget exhaustion; interrupted-call reconciliation; and actual-SDK request/output/cost allowance enforcement through deterministic HTTP transport fixtures.

Real MongoDB is isolated on localhost:27048. Provider HTTP fixtures and test adapters remain in test assemblies only. No live OpenAI or GitHub call, customer source mutation, production data migration or run resume is performed for this milestone. M1's Docker/browser/session evidence remains separate; M2 does not repeat that certification as a substitute for new runtime tests.

Security review checks closed messages, authority derived from the orchestrator/assigned role, evidence identity/revision checks, no tool execution inside the runtime adapter, immutable context/checkpoint ownership, cumulative provider allowances, no automatic retry of uncertain invocations and compatibility with legacy role validation. Migration/rollback and remaining work are in ADR 012.
