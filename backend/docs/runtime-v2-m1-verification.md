# Runtime V2 M1 verification

M1 is implemented and verified; protected-branch review/merge remains a human decision. M2–M5 are still separate work. Baseline: `8c05538` on master. Work was isolated in `/tmp/aep-runtime-v2`, branch `feat/runtime-v2-m1`; the owner's original checkout, running stack and execution data were preserved.

## Executed evidence

| Check | Actual result |
|---|---|
| Solution restore/build | Passed; zero build warnings/errors |
| Complete backend regression with real executor opt-in | 293 passed, zero failed, 3 explicit external opt-in skips |
| Breakdown | Application 55, Architecture 2, Domain 18, Integration 179, LocalExecutor 39 |
| Frontend typecheck/component tests/production build | Passed; 50 tests passed |
| Playwright | 4 passed on non-reused ports 18188/18173; deterministic test assembly only |
| Compose `up -d --build` | New API, worker, Mongo and executor healthy; runtime image service completed successfully |
| Real HTTP session/restart check | Login, persistent cookie, UI during worker stop, same session after API restart, logout and captured-cookie rejection all passed |
| Source review | Layer boundaries, cookies/CSRF, revocation, key/token volumes, Docker authority, pins, cleanup ownership, global event order and diff reviewed |

Complete backend command:

```sh
TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27048 RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:b66f58d9e568bcf58e5552bb8087be6b1d98024c6bd1b912cd54e9ffc5d72c04 dotnet test backend/AIEngineeringPlatform.sln --no-build --no-restore -m:1 --logger 'trx;LogFilePrefix=runtime-v2-m1-complete'
```

Safe test names/counts, raw TRX hashes and tested source hashes are in `evidence/runtime-v2-m1/`. Full raw TRX and earlier failed-attempt evidence remain in ignored local TestResults directories. Generated browser screenshots are preserved under `/tmp/aep-runtime-v2-generated-screenshots/`; existing tracked screenshot artifacts were restored rather than mixing unrelated visual updates into this milestone. Frontend emits existing large-chunk/jsdom CSS notices; Playwright emits a color-environment notice. None were hidden or counted as passes in place of executed tests.

The disposable Compose project uses private named volumes and loopback port 18089. It contains no live OpenAI key or customer execution. The existing owner stack on 8080 was not stopped. Tests used a new isolated MongoDB on 27048 with nofile=65536; previous stalled test databases and their evidence were retained. Only the stalled test container created during this work was stopped.

## Corrections proven during verification

- Cookie persistence needed an actual persistent browser cookie, retained key ring and Mongo session authority. TTL cleanup alone does not authorize expiry: every access checks the eight-hour absolute deadline. Activity never extends authentication.
- Reauthentication through ASP.NET's ITicketStore renewal path reused a revoked key. Explicit cookie signing/validation/sign-out events now issue a fresh session every login, revoke the previous authority record and validate each request. Regression tests verify both replacement-cookie acceptance and old-cookie rejection.
- Live SignalR connections close at authentication expiry, are aborted on local logout, and are checked against shared session authority before publication; session material never enters agent contexts.
- Grouping relay reads by event kind could reorder tools/messages. A bounded indexed global-sequence window now feeds the API's live relay; an integration test verifies publication only after persistence and the exact cross-kind order.
- Rebuilding a moving Compose image tag could remove the old digest used by a running executor. Managed startup retains an immutable owner pin. Real Docker tests verify that removing the moving alias does not remove the approved digest. New execution receipts include the image identity; old receipts remain readable.
- Container-local state paths are identical across separate Compose deployments. A persistent per-volume instance identity now isolates Docker cleanup ownership; legacy host-mode ownership remains compatible.
- A history test incorrectly assumed its large fixture was the last event despite asynchronous durable outbox publication. It now asserts the exact evidence ID while retaining ordering/pagination assertions.
- The strict Origin check exposed Vite's string-proxy Host rewrite; preserving the original browser Host fixed development/test requests without weakening CSRF policy.

## Limits and remaining work

There is no live autonomous application certification in M1, and no Codex runtime is embedded. The new stack has no production data migration, automatic run resume, merge or deployment. Existing manual/HTTPS executor modes remain compatible. Legacy conversational planning replies remain API-hosted until runtime migration. The owner must review/cut over production explicitly, retain named volumes and follow `runtime-v2-migration.md` for credentials, backup and rollback. Safe archival/capacity lifecycle and independently scheduled role tasks belong to subsequent milestones.

The owner has authorized M5 only for `ghptc/aep-sandbox-m8-20261028-3a0ed9`, installation `176070757`, aggregate OpenAI spend at most USD 10. Installation permissions and repository baseline have not yet been verified; no live certification was started and no customer source was authored during this milestone.
