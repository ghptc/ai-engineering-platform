# Verification report — 2026-10-28

The implementation was resumed from preserved uncommitted files after the host restart. No reset, source deletion, or repository recreation was used. The recovery audit found only the initial commit. Milestone history is available through `git log`.

## Executed commands and final results

Commands were executed from the repository root unless stated otherwise. Final results are summarized below. Raw command outputs remain local in the ignored `verification-evidence/` directory; generated logs and test artifacts are intentionally excluded from source control.

| Command | Final result |
| --- | --- |
| `git status --short`, `git log -5 --oneline` | Inspected initial commit and preserved uncommitted source |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed; all projects up to date |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings, zero errors |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore --logger 'console;verbosity=minimal'` | Passed: 45 tests; 14 Domain, 6 Application, 2 Architecture, 23 Integration; none skipped |
| `npm ci --prefix frontend` | Passed from the lockfile; 0 vulnerabilities reported |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | Passed: 7 Vitest/React Testing Library tests |
| `npm run build --prefix frontend` | Passed; lazy Monaco bundle produces a size warning |
| `npm run e2e --prefix frontend` | Passed: full Playwright browser workflow; final clean-install run 1 passed in 14.7 seconds |
| `npm audit --prefix frontend --json` | Zero known dependency vulnerabilities |
| `dotnet list backend/AIEngineeringPlatform.sln package --vulnerable --include-transitive` | No known vulnerable packages reported for any project |
| `docker build -t ai-engineering-platform:local .` | Passed; final image `sha256:c6ada59f5e964953e90918648a4c613b6395877f0b14e669b2c799dc8e203c8f` |
| `python3 /tmp/aep/container-smoke.py` | Passed: 13 deployed-container checks, listed below |
| `docker exec aep-app-smoke id -u` | UID 1654 (non-root) |
| `docker exec aep-app-smoke sh -c 'if command -v node; then exit 1; else echo "Node runtime absent"; fi'` | Node runtime absent |
| `docker inspect aep-app-smoke-final --format '{{.Config.User}} {{.State.Health.Status}} {{.Image}}'` | `app healthy` with the final image identity above |
| `git diff --check` | Passed |

The initial smoke container used the same runtime base/user as the final image. The final rebuilt image was separately started as `aep-app-smoke-final` and all 13 HTTP checks repeated successfully. Temporary verification containers were stopped after verification; their MongoDB data and application source were retained.

## What the tests establish

MongoDB tests use a real separate MongoDB 8 container, unique test databases, the official driver, indexes, serialization, optimistic versions, duplicate prevention, lease fencing, atomic transition outbox recovery, conversation persistence, approval decisions and PR webhook deduplication. Both review-before-commit and review-after-PR paths run through the complete workflow with deterministic external adapters.

Provider contract tests exercise the actual official OpenAI Responses SDK against a fake HTTP transport, including strict output schemas and usage parsing. GitHub HTTP contract tests exercise RSA App JWT generation, repository-scoped installation tokens, PR creation and review deduplication. Sandbox contract tests inspect bounded requests and fail-closed configuration. These tests do not perform live external operations.

The Playwright browser journey signs in, creates a project with initial context, connects a simulated repository, persists a client conversation, starts a run, observes stages/messages, inspects artifacts and review records, approves delivery, reaches completion and confirms history after refresh. Its AI, sandbox and GitHub adapters are explicitly test-only fixtures. Production source contains no selectable fake integration mode.

Deployed-container checks verify liveness, MongoDB readiness, SPA root and deep links, anonymous API rejection, unknown API 404 behavior, owner authentication with Secure/HttpOnly cookies, authenticated project creation/read against MongoDB, disabled unconfigured sandbox, aggregate dashboard statistics, authenticated OpenAPI and SignalR negotiation. The smoke harness used an ephemeral local test credential, not a production owner secret.

## Resolved failures and remaining verification boundary

Earlier failures exposed outdated/vulnerable package versions, a Monaco worker export change, test-host content-root configuration, artifact/tool ID collisions, brittle browser locators and an event replay request storm. They were fixed before the passing results above. Vitest emits jsdom getComputedStyle pseudo-element notices; the production build emits a lazy-editor chunk-size warning. Neither is a failing check.

Live OpenAI model execution, GitHub App installation access, real branch/commit/push/PR/review operations and execution isolation on a production remote sandbox remain unverified because no live credentials or executor deployment were supplied. The production adapters and explicit configuration exist, but successful fake calls are not evidence of live service behavior. The external executor must implement and enforce [the sandbox contract](sandbox-contract.md). Without it the platform rejects execution instead of running untrusted code locally.

The complete production Definition of Done remains contingent on those external prerequisites and opt-in verification. The platform does not automatically merge or launch unlimited additional runs. The next development context is persisted for the next explicit cycle. See README for actual capabilities, deployment commands and operational limits.

## Adaptive context management — 2026-10-28

Verified the feature-branch source/test delta with SHA-256 `df4dd1e4a1b922c54b89e5f9285e6549074bb9f5aa83666b7d1b5d6082257957` (`git diff --cached --binary -- backend/src backend/tests frontend/src | sha256sum` before the milestone commit). No application source changed after these final checks; subsequent edits only record this evidence.

| Check | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors on final build |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1` | 63 passed; 5 opt-in tests skipped: 14 Application, 14 Domain, 2 Architecture, 25 Integration, 8 LocalExecutor passing |
| `npm ci --prefix frontend` | Passed, zero reported vulnerabilities |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 8 passed |
| `npm run build --prefix frontend` | Passed; existing large-chunk warning remains |
| `npm run e2e --prefix frontend` | 1 passed on dedicated non-reused test ports; latest run 9.7 seconds |
| `docker build -t ai-engineering-platform:local .` | Passed; image config `sha256:2bed89df8a6191eb314e0cf7592c976a1e1c359ec8da9e142d764d42943b45fa` |
| `git diff --cached --check` | Passed |

New regression tests verify Unicode token bounds, output reservations, explicit protected overflow, long tool loops, original requirement/human-instruction preservation across four stages with oversized legacy snapshots, bounded project-scoped memory retrieval, provider rejection before transport, and test evidence/revision preservation. Both complete workflow approval paths now start with 200000-character legacy snapshots and completed-history artifacts, pass implementation/QA/review, await human approval and complete with deterministic external adapters backed by isolated MongoDB.

Read-only inspection identified the actual matching local failed run and artifact sizes; [ADR 005](architecture/005-adaptive-context.md) records that inventory and the exact old guards. No production run, project, message, approval or artifact was changed or retried. The running owner application and its MongoDB remained intact. The isolated MongoDB test container exited during initial verification; restarting only that test container restored the passing runs. An intermediate regression exposed the deterministic fixture's dependence on full repeated TestReport content; summaries now preserve evidence/revision and the fixture resolves the explicit required-test reference.

Sizing deliberately uses a conservative byte-level BPE token upper bound, not exact model tokenization. Live OpenAI/GitHub App engineering execution and opt-in real Docker executor tests were not run for this change (2 live-agent and 3 executor tests skipped). Docker image build success and the feature-branch Git/PR delivery are separate from those external integration boundaries. No deployment or automatic retry of the preserved failed run is claimed.

Diff review checked protected-content retention, original-reference/project isolation, model preflight/output reservation, persist-before-publish telemetry, unchanged approval/lease/fence/idempotency gates, source layout and absence of secrets/generated build artifacts in the staged change.

## Architect context regression — 2026-10-28

Source/test delta SHA-256: `3169dce80f51bb8049121fb4c308de01d33509324e149aebd02f9b1c4eb2d904` from the staged binary diff of backend/src and backend/tests before commit. Final backend build passed with zero warnings/errors. Full backend tests passed: 64 passing (15 Application, 14 Domain, 2 Architecture, 25 Integration, 8 LocalExecutor) and 5 opt-in external tests skipped. Integration workflows were rerun after adding 80 distinct long historical conversations to each approval-policy scenario; both reached implementation, QA and approved completion. The new focused-context regression retains selected JavaScript fixture criteria and human safety instructions with 500 long historical agent messages while fitting below 10000 conservative context tokens under a 25000-token input limit. Missing selected records fail closed; financial/model budgets remain untouched.

Playwright full workflow passed (1 test, 9.5 seconds). Docker packaging passed, image config `sha256:332ffdde40fa6372c3ff43e70617b1e9e49c9e8c769105dc035c8c91f2fdee07`. Frontend source/dependencies were unchanged; their checks remain as recorded for the preceding milestone. Staged diff/whitespace review passed and found no secrets or generated artifacts. No production execution was retried or mutated. [ADR 006](architecture/006-focused-context-regression.md) contains the actual failed-run inventory and official model-window reference. No live OpenAI/GitHub App or real executor workload success is claimed by this regression milestone.

## Lightweight local task workflow — 2026-10-28

Final source/test delta SHA-256: `fb01286e0aba304b837807450a361725d8453f48dd3e64084f20b25f490072cf`, computed from `git diff --cached --binary -- backend/src backend/tests frontend/src frontend/tests | sha256sum` relative to the focused-context milestone. Application source was unchanged after the successful live workflow; additional scope-isolation and specialist-routing test cases then passed in the final suite. Documentation changes only record these results.

| Check | Final result |
| --- | --- |
| Backend restore | Passed |
| Backend build, `--no-restore -m:1` | Passed, zero warnings/errors |
| Full backend suite with `RUN_LOCAL_EXECUTOR_TESTS=1`, immutable `EXECUTOR_TEST_IMAGE` and isolated MongoDB | **83 passed**: 19 Application, 17 Domain, 2 Architecture, 29 Integration, 16 LocalExecutor. Two opt-in live tests skipped in this invocation; the complete real workflow was explicitly run separately below. |
| Real Docker executor tests | All three opt-in tests passed within the full suite: Node/.NET workload edits and assertions, feature-branch commit/push/default-branch protection, isolation/resources/network/output/deadline/cleanup, authenticated Unix daemon and durable replay/restart |
| `npm ci --prefix frontend` | Passed, zero reported vulnerabilities |
| Frontend typecheck / unit tests / production build | Passed / **8 passed** / passed; existing large-chunk warning remains |
| Browser workflow | **1 passed**, 11.2 seconds; focused task launch, saved standing instructions, chosen specialists, handoffs, collaboration, artifacts, persisted timeline and delivery approval |
| Docker application image | Built successfully; config `sha256:8d40f4f8c0426b5a09d3909745aa404c727106a97be99d4435a4862cbed6efcf` |
| Staged diff, whitespace and secret-pattern review | Passed; no generated build output, credentials, .env, daemon configuration or runtime state staged |

Regression coverage exercises all four independent BA/Architect selection combinations, optional frontend participation, two-task automatic continuation with unchanged cumulative budgets, human delivery approvals, exact QA evidence matching, refusal to import another project's requirement/human-message references, no project-wide history reads for focused tasks, and Node default/TAP/ANSI test-reporter counts with zero-test rejection. Existing broad project workflows and both review policies remain covered.

### Real OpenAI, GitHub App and local Docker result

The explicit `RUN_LIVE_AGENT_TESTS=1 LIVE_FOCUSED_TASK=1` workflow passed using real credentials, model `gpt-6.1-sol`, positive configured prices (USD 2 input / 10 output per million tokens), an explicit **USD 1 run cap**, real MongoDB and a separately authenticated host Unix daemon. No deterministic model, sandbox or GitHub adapter was used in this run. The separate daemon reused only the owner's existing disposable-repository allowlist and pinned runtime image, with its own state/socket and fixed `node --test` profiles. The owner's running application, daemon, project database, profiles and failed executions were not changed or retried.

| Evidence | Confirmed value |
| --- | --- |
| Run / status | `f90535012966492c97c04698c84803cb` / Completed |
| Verification database | `aep_test_c7d54e45a38d4dfaab4b3773744d3c29` on dedicated test MongoDB port 27029 |
| Specialists actually called | BackendEngineer (Developer), TestEngineer, QAEngineer, CodeReviewer; no BA, Architect or FrontendEngineer |
| Initial context / maximum turn body | **3,269 / 13,783** conservative input token upper bounds; no full historical conversations |
| Actual usage / cost | 53,530 total reported model tokens over 11 model calls / **USD 0.146036**, 24 tool calls |
| Source changes | `feature.mjs`: answer() 1→2; `feature.test.mjs`: existing strict assertion 1→2; exactly two files changed |
| Required passing test evidence | `local-ee41e3a5567c6d59f8d884ec02a30b9b33c264cf83bd40ec0f82fa892240a549` |
| Tested and committed workspace revision | `987f8a8c0454441834b304fe3a57d6d84a559788433e09de60d73318ad9e5dae` |
| Git commit / pushed branch | `1eb536ce57b8ebc0f5346f7d6f5572f142abdd85` / `engineering/f90535012966492c97c04698c84803cb` |
| Actual draft PR | [Disposable fixture PR 1](https://github.com/ghptc/aep-sandbox-m8-20261028-3a0ed9/pull/1), open and unmerged |
| Published GitHub review | [Review 5688623915](https://github.com/ghptc/aep-sandbox-m8-20261028-3a0ed9/pull/1#pullrequestreview-5688623915), COMMENTED against the delivered commit; local model diff review is recorded separately, not represented as independent human review |
| Delivery approval | Persisted Approved decision `9f64622191ee7cdda0690bd16bdd59589257f2528dce3c7666b36a6d5fe49da9`, explicitly authorized by the owner-invoked disposable live test; production delivery approval remains manual |
| Default branch before / after | Both `46f5f71a228dfe46d72d0f04c85118dc8620b2ab`; remote SHA independently read back after PR creation |

The standalone real API contract probe was not separately run; the complete workflow exercised the real Responses API and recorded successful usage. The final full-suite opt-in skips must not be confused with the separately passing complete live workflow.

### Failures investigated and retained

The first real attempt correctly failed closed because Node's default `ℹ tests 1` reporter was counted as zero; the executor now supports default and TAP output and has regression coverage. A later attempt completed real tests and QA but returned explanatory evidence strings rather than an exact evidence-ID array item; the strict gate rejected it. The schema/context now explicitly require bare IDs and preserve the required checkpoint through specialist handoff. These failures were fixed without accepting prose as evidence or dropping test/review/approval requirements.

The pre-existing isolated test MongoDB container exited repeatedly with a server segmentation fault (exit 139, not an OOM-killed report). Its data was retained; final verification used a new dedicated container, `aep-focused-mongo-verification`, with bounded resources/cache on port 27029. Failed attempt receipts, logs and workspaces remain under `/tmp/aep-focused-live-20261028/` and their original test databases; successful receipts/state remain alongside them. No production data was reset, discarded, migrated or overwritten. The temporary verification daemon and MongoDB service are stopped after verification; the test container/volume and local evidence files are retained for inspection.

Final review covered domain transition restrictions, project-scoped references, persisted instruction revisions, shared queue budgets, strict QA IDs/revisions, real diff review, owner approval before protected Git actions, persist-before-publish telemetry/handoffs, the existing host-daemon trust boundary and lack of automatic merging. No deployment or replacement of the owner application/daemon is claimed.

## Idempotent task execution and stack-aware tests — 2026-10-29

Verification used an isolated snapshot at `/tmp/aep-idempotent-verification`: base commit `54d13df2f62a1164b65f2a3be45b416603ac4dd4` plus only the 19 bug-fix source/config/test files. Concurrent owner API/UI changes were excluded and preserved. The sorted path-to-SHA256 manifest has SHA256 `e280b482df4cb8f8147ff08be3832f59ab22845e596ed01378013a23399b95e2` (canonical JSON with sorted keys). Workspace files were compared byte-for-byte with that manifest before staging. Subsequent documentation updates do not change the verified source. Local logs are `/tmp/aep-reconcile-final-tests.log`, `/tmp/aep-reconcile-browser.log` and `/tmp/aep-reconcile-image.log`; they are not committed.

| Check | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed in isolated snapshot |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors |
| Full backend suite with real executor/disposable flags | 93 passed: Application 19, Architecture 2, Domain 17, Integration 32, LocalExecutor 23; two live OpenAI opt-in tests skipped |
| Real Docker executor verification | Four real tests passed: mixed-stack delivery, existing feature-branch reconciliation, Unix authentication/restart, isolation/time/output/cleanup; pinned image `sha256:ba3d80d3f5077a1eaac0a3627bb3b9723684f39d391f9cf996fc90dbf90e44be` |
| Real disposable repository verification | GitHub App reads, fresh clone, source/test inspection, empty actual diff, fixed `node --test`, exact source revision and existing merged PR all confirmed; no remote mutation |
| `npm ci`, frontend typecheck, Vitest and production build | Passed against unchanged committed frontend; 8 tests; existing bundle-size warning |
| Playwright | Passed, one workflow; dedicated API/frontend ports 16988/16973; deterministic test-only adapters |
| Docker production build | Passed under separate tag `ai-engineering-platform:idempotent-verification`; image `sha256:f8d224e3aeaac828c9284186e4a438c46568576f4eb8e8446b527371bf9e9f30` |
| Diff review | Reviewed reconciliation authorization, dedicated-branch transfer, no-op revision/QA/review gates, duplicate-delivery guard, instruction migration and stack prerequisites; `git diff --check` passed |

Regression coverage includes already-completed criteria, no-op implementation, existing matching PR, existing feature-branch source, incompatible .NET/Node profiles, missing npm lockfile, exact evidence revision mismatch and unchanged protected default branch. No-op integration tests require fresh successful tests and QA, assert `AlreadySatisfied`, and reject commit/push/duplicate PR operations. The optional live test harness now accepts existing verified delivery rather than requiring artificial changes.

The real repository was `ghptc/aep-sandbox-m8-20261028-3a0ed9`, current default/merge commit `eb4462114a9c7c5f95b4db99ee50013d5ea3f94a`. Source `feature.mjs` returns numeric 2; the existing Node assertion expects numeric 2. One test passed at source revision `987f8a8c0454441834b304fe3a57d6d84a559788433e09de60d73318ad9e5dae`. [Existing PR #1](https://github.com/ghptc/aep-sandbox-m8-20261028-3a0ed9/pull/1) is merged; its feature head is `1eb536ce57b8ebc0f5346f7d6f5572f142abdd85`. Repository `pushed_at` was unchanged across this check. Disposable fixture tests ran only inside the hardened Docker workload; no generated repository code ran on the workstation host.

Reproduce the remote read-only check using `VERIFY_DISPOSABLE_REPOSITORY=1 EXECUTOR_CONFIG_PATH=<owner-managed-config> EXECUTOR_TEST_IMAGE=<immutable-image-id>` and the local executor test project. Add `RUN_LOCAL_EXECUTOR_TESTS=1` to include the four isolated Docker regressions. No OpenAI call or new sandbox delivery was needed or claimed. Owner development services and existing data remain intact; deploying the new daemon and configuring its `node`/composite profiles is separate from this verification. Further edits on an existing reconciled feature branch pause for explicit delivery handling instead of opening another PR.

## Engineering workspace UX redesign — 2026-10-29

Implemented in isolated worktree `/tmp/aep-ux-worktree` on `feat/engineering-workspace-ui`, preserving concurrent unrelated changes in the owner's original worktree. Audit preceded implementation; see [ux-audit.md](ux-audit.md). No production database migration, live provider call, deployment or automatic merge was performed.

Verification of the UX milestone based on `54d13df`:

| Check | Actual result |
| --- | --- |
| .NET restore and single-worker build | Passed; 0 warnings, 0 errors |
| Full backend suite with isolated MongoDB | **84 passed, 5 explicit opt-in skips**: Application 20, Domain 17, Architecture 2, Integration 32, LocalExecutor 13 |
| Frontend clean install | Passed; npm reported 0 vulnerabilities |
| TypeScript typecheck | Passed |
| Frontend unit/component tests | **25 passed**, three files |
| Playwright browser suite | **2 passed**, 17.6 seconds, dedicated ports 16088/16173 |
| Production React build | Passed; existing large Monaco/vendor chunk warning remains |
| Production Docker build | Passed; separate tag `ai-engineering-platform:ux-review`, image config `sha256:6e9ff79594b13e9575d269ee71a777e185d49a4b552e5725161ec4818dd86739` |
| Whitespace/source review | `git diff --check` passed; reviewed persisted-before-publish, owner authorization, scoped evidence, Markdown sanitization, price/history isolation and original budget/approval checks |

Regression evidence includes 1,002+ persisted events replayed through real hub logic across multiple 500-record batches, reconnect from the completed watermark, no duplicates, cold snapshot behavior, latest-first cursor pagination, a preserved 100,000-character diagnostic original behind a preview, scoped evidence 404s, historical receipt arithmetic, price-only updates and owner authentication. Frontend tests cover shared subscriptions, out-of-order/deduplicated replay, initial connection retry and cleanup, selected specialists, recorded handoffs, scrolling/auto-follow freeze and Jump to Latest, readable Markdown, safe lazy JSON, cost separation, clarification and terminal states. Browser tests create a project, save instructions/messages, launch a focused four-specialist task, inspect actual persisted deterministic evidence, approve delivery through the existing backend and reload completed state; they also inspect 1,000 history events and mobile light-mode navigation without page overflow.

The first final repetition failed when isolated `aep-ux-mongo` exhausted file descriptors and exited 139 (`OOMKilled=false`), producing Mongo connection/500 failures. Its container/data were retained. The complete successful repeat used fresh `aep-ux-final-mongo` at localhost:27031 with a 65,536 file-descriptor limit and bounded memory/cache. Test fixtures use unique databases. Owner services and production data were not restarted, replaced or modified. Vitest emits existing jsdom/Ant Design CSS and pseudo-element warnings; no test or unhandled-error failure is hidden.

Screenshots from that successful deterministic browser run: [dashboard](ux-evidence/dashboard-dark.png), [execution/team/approval](ux-evidence/execution-dark.png), [usage/pricing separation](ux-evidence/usage-dark.png), [large diagnostic Drawer](ux-evidence/evidence-drawer-dark.png), [mobile light-mode team](ux-evidence/mobile-light.png). These are persisted test-host interactions, not new live OpenAI/GitHub/sandbox success. The two live integration tests and three real executor tests were deliberately skipped in this UX milestone; previous live evidence above remains historical evidence only.

Remaining limits: dashboard/budget panels summarize recent runs and link to full history; legacy missing model/task/tool-input metadata is unavailable; usage totals stream the existing receipt ledger rather than introducing an analytics subsystem; existing Monaco chunks remain large. Full originals, old events and artifacts remain accessible. No imaginary activity, billing confirmation or source-control success is shown.

### Compatibility with current protected master

Integrated `baf7b72` (PR 7 execution reconciliation/profile fixes) using merge commit `90b5d74`, retaining both sets of implementation and verification notes. This PR does not reimplement or remove those execution fixes. The merged .NET restore/build passed with zero warnings/errors; the full merged backend suite passed **92 tests with 7 explicit opt-in skips** (Application 20, Architecture 2, Domain 17, Integration 35, LocalExecutor 18). The seven skips comprise two live-provider and five real-executor opt-in tests inherited from current master. Merged Playwright passed **2 tests in 16.6 seconds**, again using dedicated ports 16088/16173 and MongoDB 27031. Updated screenshots reflect that merged run. A final focused regression also checks artifact-reference navigation back to the source event and ensures displayed metadata comes from the fetched original.

Final delivery repetition after the artifact metadata correction: TypeScript and production build passed; **26 frontend tests passed**, and Playwright **2 passed in 17.8 seconds**. Final Docker image config: `sha256:039acd2ce21a7257e277f2409b0c3e1477c4b5e4d6c2b6fa701139abceea6f9e`. The temporary verification MongoDB service was stopped after testing; both test containers/data remain retained. No owner service was stopped.

## Agent runtime reliability and execution capacity — 2026-10-29

The current milestone's source manifest, complete test results, real local Docker/Git evidence, browser coverage, infrastructure retry notes and live-provider limitations are recorded in [runtime-reliability-verification.md](runtime-reliability-verification.md). Its [architecture decision](architecture/008-agent-runtime-reliability.md) records the persisted context inventory root cause and context/rework/capacity contracts. Prior milestone evidence above remains historical.

## README-only planning regression — 2026-10-29

See [planning investigation](planning-handoff-investigation.md) for persisted run evidence, root cause, exact source/test manifest, isolated database setup, failed intermediate attempts and final results. Restore/build passed; backend suite passed 136 tests with 7 explicit opt-in skips; separate-tag application image build passed. No live agent cycle, production run resume or runtime deployment was performed. Frontend-specific checks were not rerun for this backend-only change.

## OpenAI parsing and cancellation recovery — 2026-10-29

See [the investigation, recovery policy and QA coverage](openai-provider-investigation.md) for read-only evidence from run `c4c85d98eec34bc199ca2b1551cacf37`, attribution limits and the reviewed implementation. Verified source manifest SHA-256: `3bfc1c80dd665acf06ba899ac0faeceac4e98644d2a97164a07edcb4c40d9957` (algorithm and source scope in that report).

| Check | Final result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings/errors |
| Full backend suite, `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27029 RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:7742feb1d0b1193a8a6af2efa05805032d1f30c50d0f3dbba4460909f81e48a9 dotnet test backend/AIEngineeringPlatform.sln --no-restore` | **169 passed**: Domain 18, Application 46, Architecture 2, Integration 79, LocalExecutor 24. **3 skipped**: two live-agent checks and one disposable GitHub repository check. Four real Docker workload tests passed. |
| `npm ci --prefix frontend` | Passed; audit reported zero vulnerabilities |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | **32 passed** across 5 files |
| `npm run build --prefix frontend` | Passed; existing bundle-size advisory remains |
| `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27029 npm run e2e --prefix frontend` | **2 passed**, 18.7 seconds, dedicated non-reused ports |
| `docker build -t ai-engineering-platform:local .` | Passed; final image config `sha256:5ff131f598c40ba435e890e68210b0df5486b1b2c3c1add4b5d2534098f174f9` |
| Source diff review, `git diff --check` and staged artifact/secret inspection | Passed; no runtime configuration, credentials, raw provider output or generated screenshots staged |

The final backend run follows the last provider/content-validation changes. Frontend verification uses the exact delivered frontend sources. Integration infrastructure failures and corrected intermediate test failures are documented in the investigation rather than counted as successes. Existing production MongoDB data, owner servers, local daemon/configuration, original run state and artifacts were preserved. Only disposable test database containers were created. The application image was built without replacing or restarting the running owner application. Live OpenAI/GitHub App execution and a new live DevPulse compile are not verified. Human review and merge approval remain pending on the feature-branch PR; no automatic merge or deployment is authorized by this delivery.
