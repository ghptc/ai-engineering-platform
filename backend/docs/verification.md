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

## Incomplete-response recovery — 2026-10-30

Read-only MongoDB inspection uniquely resolved `170acc642ef3` to `170acc642ef34f25abe49f5cae4852f7`. Its persisted provider result records `max_output_tokens`, `gpt-6.1-sol`, input 9,755 and output 4,000 tokens, cost $0.05951. The immediately preceding persisted context-budget event reserved 4,000 output tokens. Current Architect instruction revision 4 also configures 4,000. Thus the observed failure exhausted its allowance. The historical provider record has no allowance field; it was corroborated from the preceding event, not inferred from status alone. [ADR 009](architecture/009-incomplete-response-recovery.md) gives configuration and checkpoint recovery instructions.

Final verification on the source hashes below, based on master `b649be2`, feature branch `fix/incomplete-response-recovery`:

| Check | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore --blame-hang-timeout 2m` | Passed: Domain 18, Application 46, Architecture 2, Integration 90, LocalExecutor 20; 176 passed, 7 opt-in tests skipped |
| `npm ci --prefix frontend` | Passed |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | Passed: 34 tests in 5 files |
| `npm run e2e --prefix frontend` | Passed: 2 browser workflows on dedicated non-reused ports 15088/15173, test-only host |
| `npm run build --prefix frontend` | Passed; existing large-bundle warning |
| `docker build -t ai-engineering-platform:local .` | Passed; image config `sha256:ff5409a0759a14e157afa3e187a2121f1e2259ae9dcfaf4fd1c017089b2bedca` |
| `git diff --check` and actual source/test diff review | Passed; no secrets, generated outputs or production test adapters staged |

Provider regressions use the official Responses SDK with deterministic HTTP responses. They cover reason classification, exhausted output followed by validated success, a valid-looking Commit instruction rejected with the incomplete response, retained artifacts/tool evidence, accounted failed/successful attempts, insufficient remaining budget, context-window ceiling, recovery ceiling, disabled retries and bounded per-role output allowances. Existing schema/invalid-argument/cancellation/timeout/uncertain-accounting tests also pass. MongoDB checkpoint regression explicitly fails Architect after a completed planning tool checkpoint, retries the failed run, verifies BA is not called again, verifies original artifact/tool IDs and content remain unchanged, and reaches human approval without commit/push/PR effects. Frontend tests cover current diagnostics and historical reason-specific guidance.

An earlier full-suite attempt encountered `Too many open files` in the isolated MongoDB container and was stopped. The successful final suite used a fresh isolated server on port 27028 with `nofile=65536`; the previous container and data were preserved. No owner MongoDB/application/daemon was stopped. Browser screenshots regenerated by test execution were restored to their original tracked bytes and excluded from this change.

The seven skipped tests are five opt-in real local-executor/Docker/Git tests and two opt-in live OpenAI/GitHub agent tests. A successful production Docker build is recorded separately from those skipped execution tests. No live OpenAI recovery or external agent delivery is claimed. The investigated run/data were only read; its expired deadline requires an explicit authorized duration extension before live retry. Deployment, production retry and human delivery approval remain pending. GitHub authentication and protected-master status were verified for publication of this implementation's feature branch and PR.

Source revision fingerprint (SHA-256; documentation-only updates after verification do not alter these files):

| File | SHA-256 |
| --- | --- |
| `backend/src/AIEngineeringPlatform.Application/ProviderFailure.cs` | `39ff44927aa8556d91c2da5048620c74fe5ccba315ee2d42a035d08acabce9a4` |
| `backend/src/AIEngineeringPlatform.Infrastructure/OpenAIProvider.cs` | `9a46485781b69e5cc09920c3d83f34c353e32ec3c16173148012c59a8886cf4d` |
| `backend/tests/AIEngineeringPlatform.Integration.Tests/AdapterTests.cs` | `31ed77bbc45348aa55503c1b9b3b073fe3133ce8fce5b293f8f69a1ec298c8c7` |
| `backend/tests/AIEngineeringPlatform.Integration.Tests/PlanningFlowTests.cs` | `a9edef0de7a81a34e64f1264762ee1660d43c21d2ed2972a6aaf645ac6bac0d5` |
| `frontend/src/features/executions/ProviderDiagnostics.tsx` | `47b046ea4f09cdb1351c0ce8a405bd488db4a2b8ef3f96224d13b6cc11efc050` |
| `frontend/tests/provider-diagnostics.test.tsx` | `c6df56af7c6e00732b78d2cac66d2e092748a3187af5a1cfe071cad5c61b1a26` |

## Structured agent output reliability — 2026-10-30

Investigation uniquely resolved run `f3eddefcbce24d5e8cf5494ca77a8224`. BackendEngineer result `0d47865b7039411689f72725aa360588-result` and ProviderRequestFailed event correlate request `req_9d9fcff64cfc4a7ab58974494c29de7e` with response `resp_08db747b2677b673016ae4193ef32087d1bf3091c1d1b8ec12`: model gpt-6.1-sol, HTTP 200, completed, 12,488 input tokens, 529 output tokens, requested maximum 4,000, MalformedJson, attempt 1, settled cost $0.030266. The paired reservation shares the requestKey. No evidence indicates output exhaustion. No original malformed bytes or item shape were retained; exact syntax and historical extraction cause remain unknown. [ADR 010](architecture/010-structured-agent-output.md) answers the seven investigation questions and cites official model/structured-output/phase guidance.

Verified source is based on protected master `f23bcec`, feature branch `fix/structured-agent-output-reliability`. Source SHA-256 fingerprints below tie evidence to this workspace revision; subsequent report/source-control metadata changes do not alter these code/test files.

| Check | Final result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings/errors |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore --blame-hang-timeout 2m` | Passed: Domain 18, Application 46, Architecture 2, Integration 116, LocalExecutor 20; 202 passed, 7 opt-in skipped |
| Provider adapter regressions through installed official SDK, deterministic HTTP transport | Passed: 71 tests, including legacy provider protections |
| `npm ci --prefix frontend` | Passed |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | Passed: 35 tests across 5 files |
| `npm run e2e --prefix frontend` | Passed: 2 browser workflows; dedicated non-reused ports 15088/15173 and test-only host |
| `npm run build --prefix frontend` | Passed; existing large-chunk warning |
| `docker build -t ai-engineering-platform:local .` | Passed; image config `sha256:1d4b0a4d5f20bebe065c07f3e49d176e58519f7194722c3f913e21b07479a561` |
| Source/test diff review and `git diff --check` | Passed; strict schema validation retained, no rejected-output replay or execution path added |

Coverage includes valid strict wire output; malformed/truncated JSON; prose and extra JSON objects; empty content; reasoning plus segmented assistant text; explicit commentary/final phases in either order; commentary-only, unknown phase and ambiguous candidates; unrelated response items; schema/duplicate-property violations; unauthorized/unregistered tools and invalid arguments; incomplete responses; same-schema/allowance syntax recovery with independent reservations and settled charges; unchanged completed artifacts/tool checkpoints; budget and retry bounds; model/schema incompatibility without fallback; and DTO field parity for all seven roles. Existing MongoDB explicit failed-stage retry, revision-bound QA/review, delivery approval, outbox, lease/fencing and operation-idempotency regressions pass in the full suite.

The deterministic SDK test demonstrates that GetOutputText concatenates separate assistant messages into text which can fail JSON parsing. This proves an extraction hazard, not the historical response's exact contents. New extraction respects explicit commentary/final_answer phase and accepts one unambiguous decision; all selected text is syntax/schema/tool validated before returning any instructions. Diagnostics retain only safe stage/shape/phase labels, schema hash and numeric coordinates/counts. No prompt, reasoning, parser message/path, rejected text or credential is persisted.

The existing isolated MongoDB server on 127.0.0.1:27028 uses nofile=65536 and unique test databases. No owner development server, production MongoDB, application container or executor daemon was stopped or reconfigured. Browser tests regenerate documentation screenshots; those generated changes are excluded and restored to the original tracked bytes.

Seven skipped checks comprise five opt-in real Docker/Git/local-executor tests and two live OpenAI/GitHub tests. The production Docker build is verified separately. Production key/model configuration was detected without printing secrets; RUN_LIVE_AGENT_TESTS and explicit LIVE_MODEL_INPUT_PRICE, LIVE_MODEL_OUTPUT_PRICE, LIVE_MAX_BUDGET_USD were absent. A live request was not made; live parsing/recovery and agent external delivery remain unverified. This investigation reads the original run only; no production retry, data migration or deployment is included. Human delivery review remains required.

| Source/test file | SHA-256 |
| --- | --- |
| `backend/src/AIEngineeringPlatform.Application/ProviderFailure.cs` | `cfeffc2d8ec71f8125cffbb852f73242b4b2671c4bd185aa5318d19f41fde697` |
| `backend/src/AIEngineeringPlatform.Infrastructure/OpenAIProvider.cs` | `b1cc8006c76d48b47f9a67c4d066924143d7b9f7ca195b04a374b0a517337e10` |
| `backend/tests/AIEngineeringPlatform.Integration.Tests/AdapterTests.cs` | `43f66d5d75745d4f1524708af699b22f71127f1ac35eb7632948f06933e693f6` |
| `frontend/src/features/executions/ProviderDiagnostics.tsx` | `31456a08f2afbb2e7242cde7edb9cd1deb67b4d8795890c69b80637fd2c90dc6` |
| `frontend/tests/provider-diagnostics.test.tsx` | `27ac6532e50304d8df65d72b8b8c0b29ea86ded9d3a4a7c5535ef5735302ef54` |

## Executor HTTP classification and recovery — 2026-10-30

Feature branch: `fix/executor-http-recovery`, based on `efaf77b059fb0b09f77fac51f02e3d724b5541ef`. Source/test fingerprint SHA-256: `5e275d7f516080e7a21aeeac65e2339dcf69b5c66ce715447a883dc2486a2cf4` (sorted tracked and non-ignored source/test paths under backend/src, backend/tests, frontend/src and frontend/tests; each path, NUL, file bytes, NUL). Documentation-only finalization follows these checks.

- Restore passed; solution build passed with zero warnings/errors.
- Final sequential solution tests: 225 passed, seven opt-in cases skipped (18 Domain, 46 Application, two Architecture, 139 Integration, 20 LocalExecutor). MongoDB used a newly created disposable server on localhost:27028, with independent randomized test databases.
- Explicit real Docker run: 24 passed, one remote disposable-repository test skipped. Image: `sha256:7742feb1d0b1193a8a6af2efa05805032d1f30c50d0f3dbba4460909f81e48a9`. Includes Unix authentication, actual 400/401/422/503 errors, four successful writes surviving rejection and missing-image failures, receipt replay/restart, real Git cycles, timeout/output bounds and Docker isolation/cleanup checks. The final Docker-only run also covers the added missing-image case.
- HTTP regressions cover 400/401/403/404/409/422/429/500/502/503/504, malformed and oversized bodies, credential/path suppression, caller cancellation, ambiguous transport failure, unchanged idempotency keys, disabled/exhausted retries and Retry-After bounds. Worker regression verifies persisted actionable evidence, paused state, preserved writes and no stage retry.
- Clean `npm ci`, TypeScript, all 35 frontend tests and production build passed. Existing bundle-size warning remains. Both browser tests passed on dedicated non-reused test ports using the test-only host; regenerated screenshots were excluded.
- Production Docker build passed under a separate `ai-engineering-platform:executor-http-fix` tag; no deployment or owner service replacement.
- `git diff --check` passed. Manual diff review covered integration boundaries, raw-body suppression, status/category mapping, retry eligibility/limits, receipt identity, cancellation, persistence before notification and unchanged isolation/authentication controls. Review corrected a possible failure-event/successful-tool record-ID collision by giving failure events a separate prefix.

Initial broad runs encountered a disposable MongoDB file-descriptor limit; that server alone was recreated with nofile=65536 and the final suite passed. Earlier runs also identified old tests expecting generic exceptions; they now assert typed failures. One existing latest-event-history assertion failed during an intermediate run and passed in the final complete suite. Logs are retained locally under `/tmp/aep-executor-fix-*`; they are not committed.

Historical diagnosis remains limited: `RunCommand {profile:build}` is established from persisted evidence, but the incident HTTP status/body were discarded by the previous implementation. A missing configured image is observed now; HTTP 409 is a hypothesis under the old mapping, not a historically verified status. See [executor-http-recovery.md](executor-http-recovery.md). Four DevPulse write receipts and their final revision were preserved read-only. No live OpenAI/GitHub agent run, DevPulse recovery execution, human delivery approval, deployment or merge is claimed.

## DevPulse 359ac513 root-cause correction — 2026-10-30

Base: master `39ed6e7` (merged PR #15). Feature branch: `fix/devpulse-rejection-idempotency`. Final source/test fingerprint SHA-256: `e9d8e456b78ebab89efe9cd16777d88a1324a8576153c8ec7689071c7adcb4fb` using the sorted path/NUL/file/NUL procedure documented above. Only documentation and removal of test-regenerated screenshots follow this source verification.

Investigation first established the persisted HTTP 409, exact `build` operation ID, same-ID failure event, real collection indexes and two successful write receipts. An unchanged-code regression then reproduced MongoWriteException at AppendAsync line 104; an isolated replay of the exported event returned code 11000 on `_id_`. No production insert/update/delete was used for diagnosis. The actual old daemon binary/config and copies of the real DevPulse workspace reproduced missing-image HTTP 409, invalid-target MSB1009, successful Core compilation and API CS5001. See [the root-cause report](devpulse-359-root-cause.md) for exact evidence, distinctions and recovery instructions.

Final verification:

| Check | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings/errors |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1` | 246 passed, eight opt-in skips: Domain 18, Application 46, Architecture 2, Integration 154, LocalExecutor 26 |
| Explicit `RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:7742feb1d0b1193a8a6af2efa05805032d1f30c50d0f3dbba4460909f81e48a9 dotnet test backend/tests/AIEngineeringPlatform.LocalExecutor.Tests --no-restore` | 31 passed, one remote-only skip; real Docker/Unix/Git tests, original DevPulse layout, correct/wrong targets, known missing-image preflight, preserved receipts/revisions, authentication, readiness, resource limits and cleanup |
| `npm ci --prefix frontend` | Passed |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 35 passed |
| `npm run build --prefix frontend` | Passed; existing bundle-size warning |
| `npm run e2e --prefix frontend` | Both browser workflows passed on dedicated non-reused test ports |
| `docker build -t ai-engineering-platform:devpulse-359-fix .` | Passed; separate image, manifest `sha256:20a189b8d3d32f9f6c16316482fd1c46327efbcff6a7b8e8c11f49b6148360e0` |
| `git diff --check` | Passed |

Mongo integration used the disposable `aep-devpulse-359-tests` server on localhost:27028 with nofile=65536 and randomized test databases. Raw local evidence is under `/tmp/devpulse-359-*`, with four actual-workspace experiments under `/tmp/devpulse-359-isolated-sasd67k5/`. Original worker diagnostics are filtered in `/tmp/devpulse-359-worker.log`; the exported event used for isolated reproduction is `/tmp/devpulse-359-failure-record.json`. Logs, temporary copies, credentials and generated screenshots are excluded from the commit.

QA/regression coverage includes timestamp-only and concurrent append replay, unordered metadata, immutable-payload/kind conflicts preserving successful receipts, the external-ID index, concurrent webhook deduplication, the actual two-write/RunCommand build sequence, legacy failure IDs, explicit safe versus uncertain resume, real secondary Mongo 11000 without masking primary HTTP 409, structured worker log fields, and old/invalid daemon readiness versions. Manual diff review checked Clean Architecture dependencies, immutable record semantics, existing webhook insertion semantics, independent failure-event IDs, cancellation/lease/version behavior, paginated uncertainty guards, unchanged operation/idempotency keys and budgets, exact profile prerequisites, and all sandbox boundaries. An intermediate broad run exposed a webhook duplicate-return regression from append replay; the private insertion result and a concurrent webhook test corrected it. Initial test-host compilation checks also caught and corrected analyzer violations in the new tests; final checks passed.

No live OpenAI/GitHub agent cycle, owner daemon restart, runtime profile change, production deployment, historical-event rewrite, DevPulse source modification, or automatic merge was performed. The original run remains Failed at version 61 with both successful writes and their final revision intact. A reviewable profile candidate is provided, but the legacy uncertain operation remains blocked pending a separately reviewed reconciliation action. Human approval and confirmed deployment/delivery are not claimed by this PR.

## Implementation/verification lifecycle boundary (2026-10-30)

Source scope: EngineeringWorkflow, agent lifecycle instructions, PlanningFlowTests and the real DevPulse executor regression. Reviewed the actual diff: only Implementation blockers stop implementation; unavailable verification is reported, RunningTests gates remain strict, and no schema/security/idempotency changes were introduced.

- `dotnet restore backend/AIEngineeringPlatform.sln` and `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1`: passed.
- `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27029 dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1`: 248 passed, 8 opt-in skips (including two live-agent tests). Isolated MongoDB only. An earlier undersized disposable MongoDB crashed; those runs are not counted as successful evidence.
- `RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:cbebb41a763294eb825c06335fdaee98f56db305771f54c2de3fddbdbebebfd0 dotnet test backend/tests/AIEngineeringPlatform.LocalExecutor.Tests --no-restore`: 31 passed, one remote-only skip. Includes real Docker/Unix isolation, restart replay and an exit-zero build rejected as test certification with TestedRevision unset.
- Frontend typecheck, 35 unit tests and production build passed with installed dependencies.
- Browser regression on dedicated ports 15288/15373 against the disposable MongoDB: two passed. Generated screenshot changes were excluded.
- `docker build -t ai-engineering-platform:local .`: passed; did not replace the running owner application.

Lifecycle regressions cover README bootstrap, Delivery blockers with and without subsequent passing tests, missing profiles, genuine Implementation blockers, Test Engineer execution, absence of QA/delivery for missing tests, and resume preserving checkpoints without repeated writes. Original DevPulse workspace and existing receipts matched an isolated pre-verification snapshot. No live OpenAI/GitHub agent cycle, automatic resume, production deployment, human delivery approval or merge is claimed. See [incident evidence and recovery](implementation-verification-boundary.md).
