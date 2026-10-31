# Runtime V2 M4 verification

M4 migrates the remaining specialists to durable V2 tasks, adds opt-in Security/Infrastructure capabilities, bounded independent-run scheduling, strict actual-test reports and safe paginated worker observability. Branch: `feat/runtime-v2-m4`, based on M3 commit `4d27527`. Existing runs and owner/review services remain untouched.

The final solution build passed with zero warnings/errors. The final complete backend suite passed **350 tests** (Application 80, Architecture 2, Domain 19, Integration 216, LocalExecutor 33), with nine opt-in skips (two live-provider tests and seven Docker/Git checks). Final source/test hashes and TRX counters/hashes are recorded in `evidence/runtime-v2-m4/verification.json`.

Frontend typecheck, **55 component tests**, production build and **five Playwright scenarios** passed. The production image `aep-platform:m4-verified` built successfully, including backend Release publish and frontend production build. The existing large-chunk advisory remains. New worker screenshots are [desktop](ux-evidence/runtime-v2-worker-desktop.png) and [mobile](ux-evidence/runtime-v2-worker-mobile.png); the task table has a 320px scroll viewport and at most 25 rows per page. Generated baseline screenshots were retained separately and their tracked originals preserved.

Final backend verification used fresh isolated MongoDB on localhost:27058. The earlier test server on 27048 was confirmed OOM-killed (exit 137) after repeated retained-fixture suites; its container/data and the unsuccessful run are retained, with sanitized Docker state in `evidence/runtime-v2-m4/test-mongo-oom.json`. No owner/review database was modified. The new browser scenario initially navigated before login completed; its test wait was corrected, and both focused and final full browser runs passed. No failed/interrupted run is counted as certification.

No real OpenAI/GitHub certification has run; the M5 budget spent remains USD 0. Deterministic model/sandbox adapters and browser fixtures are confined to test assemblies and are not autonomous-delivery evidence.

An intermediate M4 test run was interrupted after four observed lifecycle timeouts. Review identified an accidentally removed `finally` lease-cleanup block during scheduler formatting. It was restored before the corrected build; the affected lifecycle/rework cases passed in the corrected run. The interrupted run is not counted as success, and its isolated databases/evidence remain retained.

Coverage includes all-role dispatch/recovery; additive historical scope indexing without payload rewriting; safe bounded history projection; stable cross-page identity/order and cross-scope cursor rejection; all nine configured specialists; Security-to-Infrastructure rework; actual test counts; explicit concurrent independent project/run claims and same-project rejection; legacy routing; unchanged progress guard; owner delivery boundaries; and distinct model-summary/tool-output UI behavior.

See [ADR 014](architecture/014-collaborative-runtime-observability.md) for security review, migration and rollback. Live certification, deployment of these changes and the greenfield approved frontend lock/bootstrap and aggregate .NET/React test-profile capabilities remain separate work; no customer source was authored by Codex.
