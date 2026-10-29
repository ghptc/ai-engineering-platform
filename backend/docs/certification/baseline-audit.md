# Certification baseline audit — 29 October 2026

Status: **M0 acceptance pending**. The follow-up below verifies merged PR #9 on a stable snapshot: 123 .NET tests, 30 frontend tests and two browser workflows passed. Current authenticated runtime inventory and application failure events remain unavailable. No agent is certified; M1–M6 have not been executed. This report does not authorize paid model execution, delivery approval, deployment or merging. The original discovery sections below remain historical; the final M0 follow-up supersedes their regression and database-availability blockers.

## Scope and source identity

Repository: `ghptc/ai-engineering-platform`. Inspected HEAD: `dc73999fc4cace94730b4717b4ac7f7742d46b21`. Initial branch: `feat/agent-runtime-reliability`. The requested master baseline was not substituted for the actual working tree. No fetch, reset, stash, source edit, runtime restart or database mutation was performed during discovery.

The tree already contained 36 modified and 10 untracked files. The complete initial inventory is [initial-worktree.txt](reports/2026-10-29/initial-worktree.txt). In particular, ContextBuilder, EngineeringWorkflow, Model, MongoPlatformStore, OpenAIProvider, executor code and their tests contained owner work. Findings about those changes are observations of the uncommitted snapshot, not features proven to exist at HEAD.

The initial inspected 146-file manifest is [source-manifest.json](reports/2026-10-29/source-manifest.json), SHA-256 `8dc18b82cf8c4d41d752bf174b3dda66edc9d786a4c6300cb22b5539992e226b`. It records tracked and nonignored untracked files under backend/frontend, AGENTS.md and Dockerfile before audit documentation was created. **The final comparison detected concurrent changes, including application source and tests. The test invocations cannot be reliably attributed to this manifest or one unchanged source revision.** Generated files, secrets and ignored runtime state are excluded. Test artifacts are local under `/tmp/aep-certification-baseline-20261029/`; their absence on another machine must be treated as unavailable evidence. [test-receipts.json](reports/2026-10-29/test-receipts.json) records their hashes and results, with this attribution limitation explicit.

## Classification

Verified means directly observed source behavior with passing deterministic evidence in this audit, limited to the named fixture. Partially Verified means implementation or historical evidence exists but current execution or required challenge coverage is incomplete. Not Verified means competence or operational behavior has not been independently established. Not Implemented means the requested capability has no implementation in the inspected tree. A registry entry, prompt or passing platform test is not an agent competency certificate.

| Area | Platform support | Certification competence | Evidence and gap |
| --- | --- | --- | --- |
| Backend Engineer | Partially Verified | Not Verified | AgentCatalog specialization; historical two-file Node task; no BE-001 order/payment/inventory challenge or critical concurrency suite |
| Frontend Engineer / UX | Partially Verified | Not Verified | AgentCatalog and React workspace tests; no FE-001 checkout, accessibility scan, independent keyboard or screen-reader evaluation |
| Software Architect | Partially Verified | Not Verified | Structured ArchitectureDecision and context tests; no ARCH-001 variants or deterministic overengineering rejection |
| Business Analyst | Partially Verified | Not Verified | RequirementProposal, task routing and persisted requirements; no BA-001 ambiguity, capacity and traceability grading |
| Infrastructure Engineer | Not Implemented | Not Verified | AgentRole has only seven existing roles; no infrastructure specialization or INFRA-001 deployment/load/rollback/restore challenge |
| Quality: Test Engineer, QA Engineer, Code Reviewer | Partially Verified | Not Verified | Separate roles and evidence gate exist; no QA-001 seeded critical defects or known-correct false-positive controls |
| Cross-agent certification | Not Implemented | Not Verified | Existing workflow has deterministic/historical live evidence; TEAM-001 including Infrastructure and all requested artifacts is absent |

Sources: `backend/src/AIEngineeringPlatform.Agents/EngineeringAgents.cs` (AgentCatalog), `backend/src/AIEngineeringPlatform.Domain/Model.cs` (AgentRole, EngineeringTask), `backend/src/AIEngineeringPlatform.Application/Contracts.cs` (RequirementProposal, ArchitectureDecision), `frontend/tests/workspace.test.tsx`, and `backend/tests/AIEngineeringPlatform.Integration.Tests/LiveAgentCycleTests.cs`.

## Registered agent and prompt inventory

Source defaults, rather than the running database inventory, are established here. `AgentCatalog.Defaults(model)` enumerates all seven roles, uses role names as stable IDs, and supplies instruction version 1. `AgentDefinition` defaults to 4,000 output tokens, 128,000 context tokens and 96,000 input tokens, with zero prices. Model comes from configuration; zero prices deliberately prevent production provider execution. Actual persisted model IDs, prompt versions, enabled flags, prices and allowed tools remain **Not Verified** until an authorized read-only inventory is available.

| Agent ID / role | Responsibility | Source-default operations |
| --- | --- | --- |
| BusinessAnalyst | Requirements, assumptions, questions, acceptance criteria and backlog | Read tools and SaveArtifact |
| Architect | Domain, architecture decisions, boundaries and technical risks | Read tools and SaveArtifact |
| BackendEngineer | Repository implementation and backend invariants | Read tools, SaveArtifact, SelectBranch, ApplyPatch, WriteFile, RunCommand, RunTests |
| FrontendEngineer | Accessible responsive React/TypeScript UI | Same developer tools |
| TestEngineer | Write and execute acceptance tests | Same developer tools, independently registered |
| QAEngineer | Compare requirements with execution evidence | Read tools and SaveArtifact; no source mutation or test execution |
| CodeReviewer | Inspect actual diff and report material findings | Read tools and SaveArtifact; no source mutation |

Read tools are ReadRepository, ReconcileRepository, ReadPullRequest, ListFiles, SearchCode, ReadFile, InspectDiff and ReadProjectMemory. SaveArtifact persists output. Delivery tools are orchestrator-controlled after approval; they are not default model permissions. `EngineeringAgent.ExecuteAsync` checks enabled status, matching role, allowed tools and required structured outputs. Responsibility text does not prove effective defect detection.

`backend/src/AIEngineeringPlatform.Api/Program.cs` agent update routes save the prior definition as `instructionVersion` and increment InstructionVersion. Worker initialization also appends a revision before its reconciliation-tool migration (`backend/src/AIEngineeringPlatform.Worker/EngineeringWorker.cs`). Do not initialize the owner runtime merely to inspect it: startup can migrate persisted instructions.

## Actual workflow and evidence boundaries

`EngineeringTask.Plan` optionally includes BA and Architect, then selected Backend/Frontend developers, followed by the independent Test Engineer, QA Engineer and Code Reviewer. Legacy runs select all seven roles. Infrastructure cannot currently be selected. Task validators constrain developers to Backend/Frontend; adding an enum value alone would not implement safe routing.

`backend/src/AIEngineeringPlatform.Orchestration/EngineeringWorkflow.cs` coordinates preparation, analysis/planning, implementation, test generation, required test execution, QA, diff review, human approval and delivery. It requests actual repository facts and stack-appropriate advertised test profiles. RequiredTestsAsync rejects missing success, evidence ID or workspace revision. QualityGate requires exact required evidence IDs and passing checks covering selected requirement IDs. Review checks actual diff revision against required test revision; delivery checks it again. A no-op takes fresh tests, QA and review and avoids manufacturing a duplicate delivery.

`backend/src/AIEngineeringPlatform.LocalExecutor/WorkspaceFiles.cs` computes a SHA-256 over ordered source paths and bytes, excluding dependencies/build outputs/secrets. This is a **workspace content revision, not a Git commit SHA**. `LocalExecutionService.cs` sets TestedRevision only on successful tests, requires the same revision at commit and checks committed/tested/current revisions again at push. Certification must retain both identities and their binding. Existing task/tool artifacts alone are not a certification record.

`QualityGate.Approves` does not itself accept a current workspace argument or inspect tests semantically. Revision comparisons occur in workflow/delivery. Its checks still originate in model output, anchored to trusted required-test IDs. It does not establish that every critical security scenario was exercised. `QualityGateTests.QaApprovalRequiresExactRequiredEvidenceIdsRatherThanProseClaims` passed; stale-revision integration coverage exists in `ReconciliationTests.NoOpRequiresCurrentTestsAndQaAndReusesExistingDelivery` but was not executed in this audit.

In the uncommitted snapshot, `ReworkPlanner.Create` persists source/target, reason, finding references, task, iteration, revision and timestamp. Sole-developer routing is unambiguous; mixed-developer routing requires explicit authorized targets. Tests in RuntimeReliabilityTests passed for QA/reviewer returns and bounded ambiguity handling. `RuntimeFlowTests.ReworkRunsOnlyResponsibleDeveloperThenRepeatsTestsQaAndReview` exercises real MongoDB persistence and subsequent retesting, but remains **not executed** here. No claim of restart-safe rework is made from unit serialization tests alone.

`EngineeringWorkflow.AgentAsync` rebuilds specialist contexts from persisted task knowledge, emits context inventory/preflight events, saves model tasks/messages/artifacts/handoffs, and references originals through bounded ReadProjectMemory. RuntimeReliabilityTests cover 200 repeated replies and 400 distinct evidence records for every existing role. Domain policies and ExecutionCapacity retain shared duration, tool, iteration and cost bounds. Worker retries selected transient failures per stage; provider retries transient HTTP failures separately.

`PlatformService.EventAsync` appends before publishing; Worker flushes the persisted outbox before publishing. MongoPlatformStore uses optimistic versions, lease ownership/expiry checks, idempotency indexes, an embedded transition outbox and insert-only record append. These are existing persistence contracts to extend, not grounds for adding a second orchestration engine. Current MongoDB recovery behavior is Partially Verified because database tests were not run.

## Material findings

| ID | Priority / classification | Finding and required next step |
| --- | --- | --- |
| AUD-01 | P0 / Not Implemented | No versioned competency registry, trusted challenge catalog, deterministic certification evaluator, certification report persistence, hidden validation suite or three-variant eligibility aggregation exists in backend/src or backend/tests. Implement through existing boundaries after M0 acceptance. |
| AUD-02 | P0 / Partially Verified | AgentAsync task metadata saves model ID, response ID and duration, but omits exact InstructionVersion, immutable definition/configuration hash and full price/config snapshot. Historical instruction records are not a per-call binding. Certification must pin these, including replay/resume behavior when settings change. Source: EngineeringWorkflow.AgentAsync; AgentDefinition; API agent update routes. |
| AUD-03 | P0 / Partially Verified | OpenAIProvider.ExecuteAsync retries HTTP 429/5xx internally and records usage only for a successfully parsed complete response. Individual provider attempts and billed incomplete/invalid-output usage are not exposed in ModelExecutionResult. Workflow stage retries are a separate count. Certification must distinguish these and bound/account for uncertain provider consumption. No paid failure was induced. |
| AUD-04 | P0 / Partially Verified | QA evidence IDs prevent prose-only success, but do not provide independent criterion execution, hidden critical security cases, or calibrated false-positive scoring. No critical-defect detection certification is established. Sources: QualityGate.cs; QualityGateTests.cs; EngineeringAgents.cs. |
| AUD-05 | P0 / Partially Verified | Workspace hashes fence tests/commit/push, but certification needs explicit tested commit SHA, challenge/version, evaluator version and immutable evidence references in one traceable decision. Sources: LocalExecutionService.cs, WorkspaceFiles.cs, ToolResult and ModelExecutionResult. |
| AUD-06 | P0 / Partially Verified | MongoPlatformStore has existing records indexes by kind/project/run/sequence and append behavior. Certification-specific lookup, immutability permissions, retention and restart recovery are undefined and untested. Do not claim permanent immutable evidence from insert-only application methods alone. |
| AUD-07 | P1 / Not Implemented | Infrastructure role, stable versioned specialization, routing, permission boundary and architecture disagreement challenge are absent. Preserve seven historical role IDs and Test/QA/Reviewer separation when introducing it. Source: Model.cs; EngineeringAgents.cs. |
| AUD-08 | P0 / Partially Verified | Read-only executor receipts for c8413d772ebd4246816ce89aef159163 are now inventoried below. The actual current agent registry, application run status/events and protected-context diagnostic remain unavailable. ADR 008 is owner-authored uncommitted documentation, not fresh application-runtime proof. |
| AUD-09 | P0 / Not Verified | Required current full MongoDB/integration/browser regression is incomplete. Partial passing suites do not satisfy a stable baseline exit gate or justify production orchestration edits. Sources: verification table below; PlatformFactory.cs and playwright.config.ts prerequisites. |
| AUD-10 | P0 / Not Verified | Concurrent source/test edits were detected between initial manifest and final comparison. Process exit success is observed, but exact tested source identity is not established. Capture an isolated reviewed snapshot before repeating acceptance checks. See reports/2026-10-29/concurrent-changes.json. |

## Protected-context reproduction

**Passed, bounded deterministic fixture.** `ContextBudgetTests.ProtectedOverflowFailsExplicitlyInsteadOfTruncating` constructs 180,000 ASCII characters in OriginalContext with BackendEngineer configured at 96,000 maximum input tokens, 128,000 model window and 4,000 reserved output tokens. It asserts an explicit exception containing “No protected requirements were discarded.” No provider request, repository workload or historical record mutation is needed. The same suite verifies output reservation, Unicode sizing and evidence/revision retention. RuntimeReliabilityTests separately verifies repetitive reply/evidence growth for every existing role.

This reproduces fail-closed overflow, **not the exact historical BackendEngineer transcript**. `backend/docs/architecture/008-agent-runtime-reliability.md` reports run `c8413d772ebd4246816ce89aef159163`, approximately 33 KB initial inventory, 12,632 requirement bytes, 10,815 architecture bytes and 95,908 conservative context tokens plus instructions/framing. The proposed cause is tool-loop accumulation. Executor receipt inspection is recorded below; application context-event inspection and exact historical replay remain pending. ContextBudget uses a conservative UTF-8 byte upper bound rather than a provider tokenizer; those numbers must not be described as exact billed tokens.

## Historical real-run and GitHub permission investigation

`backend/docs/milestone-8-verification.md` records repository-scoped installation-token HTTP 422 for installation 176070757 and an earlier owner CLI HTTP 403 while changing repository access. It explicitly refused to substitute owner OAuth for App credentials. This establishes a historical permission blocker, not current installation authorization.

Later `backend/docs/verification.md`, “Lightweight local task workflow,” records successful run `f90535012966492c97c04698c84803cb`. Read-only inspection of `/tmp/aep-focused-live-20261028/test.log` independently found the run/PR/usage receipt, the named live test passing and “Test Run Successful.” It reports 53,530 tokens, USD 0.146036 and 24 tool calls, against the disposable `ghptc/aep-sandbox-m8-20261028-3a0ed9` PR 1. Historical documented model/configuration: gpt-6.1-sol, USD 2/10 per million input/output tokens, USD 1 run cap. The logged test performed a tiny Node answer() change using Backend/Test/QA/Reviewer, not commerce certification or the full seven-role route.

Earlier failed test logs remain beside it: `test-attempt1.log`, `test-attempt2-environment.log` and `test-attempt3-evidence-contract.log`. The verification report attributes failures to zero recognized Node tests, MongoDB availability, and explanatory strings instead of exact QA evidence IDs. Their presence and failed-test markers were observed; a fresh causal replay was not performed. No failed receipt was removed. The latest runtime failure cannot be inferred from these dated test logs.

Conclusion: the old permission blocker was followed by historical evidence of one successful authorized fixture delivery. Current installation permissions, current remote PR state and current live provider behavior remain Not Verified. No external requests or credential reuse were performed in this audit.

### Authorized local-location follow-up

The owner supplied `mongodb://127.0.0.1:27028/aep_certification_test` and `backend/.local-runtime/state/`, authorizing isolated test data and read-only receipt access, with instructions to mark unavailable locations BLOCKED. The receipt directory exists. A TCP connection to 127.0.0.1:27028 returned ConnectionRefusedError (errno 111), and the local socket table showed no listener. MongoDB-backed integration, registry inventory and browser checks are therefore **BLOCKED**. No database was created, no service was started and no credential file was accessed.

Read-only inspection was restricted to direct operation receipt JSON files under `state/runs/`; state.json, control-home, executor.lock, repository workspaces and credential/configuration files were not read. [runtime-receipt-summary.json](reports/2026-10-29/runtime-receipt-summary.json) contains receipt hashes, evidence IDs, revisions, output sizes and outcomes without output bodies. It inventories five run directories. By receipt modification time, c8413d772ebd4246816ce89aef159163 is most recent (2026-10-29 12:23:15 UTC), with 47 receipts: 43 successful and 4 failed. Three failures contain MSB1003; one contains MSB1001; all have exit code 1. The recorded hashes permit comparison to the local originals. Initial keyword categories in the summary are mechanical hints, not diagnosed root causes.

Executor receipts cannot establish the latest application run order, model/prompt configuration, context size, stage failure reason or successful certification. They corroborate real executor operations and failures for the documented BackendEngineer run. The application run/event ledger remains required to establish the reported overflow and its causal relationship to those operations. No prior run was retried or modified.

## Current verification

All successful current checks ran in the shared workspace. This audit made no application source edits, but concurrent edits invalidate attribution to one exact revision; these results are diagnostic observations, not certification-grade evidence. No challenge/model invocation occurred: challenge version N/A, provider tokens/cost N/A; no monetary cost measurement is claimed. Deterministic model fixtures do not certify a production model. Human delivery approval is pending; no certification delivery artifacts have been created.

| Command / check | Status | Exit / observed result |
| --- | --- | --- |
| git status --short; git branch --show-current; git rev-parse HEAD | passed | 0; initial identity and owner modifications captured |
| git diff --check | passed | 0 |
| dotnet restore backend/AIEngineeringPlatform.sln | passed after sandbox retry | Initial restricted attempt exited 1 without diagnostics; approved retry 0, all up to date |
| dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1 | passed | 0, zero warnings/errors |
| Four individual test project commands with --no-restore --no-build --logger trx | blocked by sandbox, then superseded | Each exit 1: test runner socket permission denied, no assertion failure |
| dotnet test backend/AIEngineeringPlatform.sln --no-restore --no-build -m:1 --filter 'FullyQualifiedName!~AIEngineeringPlatform.Integration.Tests' --logger trx --results-directory /tmp/aep-certification-baseline-20261029/non-database | passed, limited scope | Approved retry exit 0: Application 40, Domain 18, Architecture 2, LocalExecutor 18 passed; 5 Docker opt-in skipped; Integration excluded |
| npm run typecheck --prefix frontend | passed | 0, installed dependency tree |
| npm test --prefix frontend | passed | 0, 30 tests in 4 files |
| npm run build --prefix frontend | passed | 0, large-chunk warning remains |
| Full unfiltered dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1 | blocked | Owner-approved isolated endpoint on 27028 refused TCP connections; no database tests executed |
| npm ci --prefix frontend | not executed | Existing dependency tree used; clean-install verification remains pending |
| npm run e2e --prefix frontend | blocked | Approved isolated MongoDB unavailable; no browser host or owner service started/reused |
| docker build -t ai-engineering-platform:local . | not executed | No current packaging/isolation claim |
| Real Docker workload tests | skipped | No RUN_LOCAL_EXECUTOR_TESTS opt-in or immutable image selected |
| Live OpenAI / GitHub / daemon cycle | not executed | No live opt-in, budget, credentials, installation or disposable repository inferred |
| Manual checkout keyboard / screen-reader review | not executed | FE-001 application and validation do not exist |

## M0 acceptance and remaining uncertainties

Discovery made no application source changes and preserved prior modifications, commits, runtime configuration and execution data. Findings distinguish source, fixture, historical report and current observed evidence. The audit documentation adds only this report and its initial-state evidence. Source/diff review for the audit must verify that only these additions are staged; owner runtime changes must remain unstaged.

M0 remains pending for the following explicit prerequisites:

1. Read-only export of current agent definitions and revisions, model/configuration and tool permissions, without secrets. Source defaults cannot answer this.
2. Read-only latest application run/event/task/tool inventory, including the BackendEngineer failure's context diagnostics. Executor receipt inventory is available, but does not establish which application run is actually latest.
3. Restore availability of the approved disposable MongoDB endpoint and run current full integration/rework/restart regression; then dedicated browser regression and clean dependency verification. Connection refusal currently blocks these checks; no owner services were modified.
4. Independent review of the preserved runtime source diff and accepted baseline revision. Capture a stable isolated snapshot, then repeat tests against its exact revision: concurrent changes currently prevent that evidence binding. Passing unit tests on uncommitted owner work do not authorize committing that work.
5. Human review/delivery approval for subsequent implementation. This report grants no certification or automatic merge permission.

After M0 is accepted, M1 should add trusted versioned challenge/rubric definitions, pinned model/instruction/source identity, append-only evidence and deterministic decisions through Domain/Application/Infrastructure boundaries, with tests for missing evidence, failed critical gates, stale revisions, forged agent claims and three-distinct-variant eligibility. Do not modify production orchestration before its stable baseline is established. All specialty challenge execution and TEAM-001 remain downstream, unexecuted work.

## M0 stable-snapshot follow-up — 29 October 2026

This follow-up is limited to M0. It changes this audit and its evidence in PR #10 only; no production source, agent instructions, orchestration, credentials, installation permissions or historical runtime data were changed. No branch/PR was merged, reset, rebased or force-pushed. Existing owner worktrees and servers were preserved.

### Stable revision and test attribution

Fetched `origin/master` and the existing audit branch. GitHub's authenticated repository interface confirms PR #9 merged as **`ff528ba7a85e4617625c6372e86d69f3fbf896cf`**, with head `7f499c90710f8563ee05abf40ad7469de1a920b0`. A new detached worktree at `/tmp/aep-m0-stable-20261029` tests that exact master revision. The existing audit branch remains `feat/certification-baseline-audit`; it was not merged with master to obtain a test snapshot. Only audit documentation/evidence is committed on that branch.

The initial 151-file tracked manifest is [stable-source-manifest.json](reports/2026-10-29/m0-followup/stable-source-manifest.json), SHA-256 `150b5511b5084a868898d68174651e38ca12ed8f60b45befed1d2dd005f0559c`. The application/test/configuration subset is [tested-source-manifest.json](reports/2026-10-29/m0-followup/tested-source-manifest.json), SHA-256 **`ab872be2e20baae2104f053aa2608b6bed790031efc7f9a6ec29d50cb50dbc58`**. It includes tracked backend/src, backend/tests, frontend, AGENTS.md, Dockerfile and .dockerignore. Final byte comparisons found **no application/test/configuration source changes**. This resolves the original concurrent-source attribution blocker.

Playwright intentionally regenerated five tracked `backend/docs/ux-evidence/*.png` files in the detached worktree. These are generated screenshots, excluded from the application source manifest; their before/after hashes are recorded. They were retained in that isolated worktree and were not staged, copied to the owner worktree or added to PR #10. The report therefore does not claim an entirely unchanged documentation tree.

[verification-evidence.json](reports/2026-10-29/m0-followup/verification-evidence.json) records commands, exit codes, source identity, environment, storage boundaries and hashes of 22 local evidence files. [test-results.json](reports/2026-10-29/m0-followup/test-results.json) records individual .NET outcomes. Raw logs/TRX remain under `/tmp/aep-m0-evidence-20261029/`; hashes establish identity but do not make missing raw files available on another machine. No raw credentials, runtime output bodies or production records are committed.

### MongoDB refusal and disposable recovery

Read-only Docker metadata identified `aep-mongo-test` as the prior owner of loopback 27028. It had exited **139** at `2026-10-29T12:55:53.853254045Z`, with `OOMKilled=false` and no Docker state error. Its last 500 log lines contain **485 “Too many open files” markers**. This supports file-descriptor exhaustion as the immediate availability problem; it does not prove the underlying cause of the subsequent process crash. Its container, logs and volumes were not restarted, removed or modified. The production MongoDB containers were untouched.

Started only a new disposable container, `aep-cert-m0-20261029-1329`, ID `c963ca57e01049ccd2ad5d4d0074df04b86f70d4aa29f38c1005fc1c0df1762c`, using cached immutable image `sha256:b9924c330a8bbc5800fe9c69f04a84e221eca7b28fd049a0354147d9908fa142`. It bound `127.0.0.1:27028`, used fresh container-local tmpfs at `/data/db` (1 GiB) and `/data/configdb` (16 MiB), 2 GiB memory, two CPUs, `nofile=65536:65536`, and a 0.25 GiB WiredTiger cache. Inspection confirmed no mounts or existing volumes. Health ping passed before and after verification.

All database/browser commands explicitly set `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27028/aep_certification_test`. The test fixture creates uniquely named `aep_test_*`/other dedicated test databases on this new server; the URI database name is not a claim that all fixtures share one database. No test connected to the production server. After evidence capture, only this audit-owned disposable container was stopped. Its temporary test data expired; the stopped container metadata and test receipts remain. No production migration or database cleanup occurred.

### Current pass/fail matrix

Commands ran from the detached worktree. .NET and browser commands explicitly set `RUN_LIVE_AGENT_TESTS=0` and `RUN_LOCAL_EXECUTOR_TESTS=0`. Browser hosts used dedicated initially free ports **18088 / 18173**, with `reuseExistingServer=false`. Production adapters were not loaded with test fixtures; the deterministic adapters remain in test assemblies.

| Check | Status | Exit / evidence |
| --- | --- | --- |
| Fetch master; confirm merged PR #9; isolated stable source | **passed** | GitHub merge SHA equals tested commit; final source hashes unchanged |
| Restore solution | **passed** | 0; `dotnet restore backend/AIEngineeringPlatform.sln` |
| Build solution | **passed** | 0; `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1`; zero warnings/errors |
| Full solution test invocation, unfiltered | **passed with opt-in skips** | 0; `dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1 --logger trx --results-directory /tmp/aep-m0-evidence-20261029/trx`; **123 passed, 0 failed, 7 skipped** |
| Complete deterministic integration suite | **passed** | **45 passed**, 2 paid/live opt-in tests skipped; real disposable MongoDB |
| Other .NET suites | **passed** | Application 40, Domain 18, Architecture 2, LocalExecutor 18 passed; 5 real-workload opt-in tests skipped |
| Locked frontend dependency installation | **passed** | 0; `npm ci --prefix frontend`; 212 packages installed, reported 0 vulnerabilities |
| TypeScript | **passed** | 0; `npm run typecheck --prefix frontend` |
| Frontend unit/component tests | **passed** | 0; `npm test --prefix frontend`; **30 passed** in four files |
| Frontend production build | **passed** | 0; `npm run build --prefix frontend`; existing large-chunk warnings |
| Complete browser suite | **passed** | 0; `npm run e2e --prefix frontend`; **2 passed** in 17.9 seconds |
| Application Docker packaging | **passed** | 0; unique audit tag `ai-engineering-platform:m0-audit-20261029`; image `sha256:ba7cf04ae3b0d7e871187b01c631d7c5fdc62abad931b39492e8667033901ae0`; not started/deployed |
| Current authenticated agent configuration metadata | **blocked** | Existing API health 200; `/api/v1/auth/me` 401 without a supplied session; no authentication bypass or credential access |
| Persisted application run/event evidence | **blocked** | Same authentication boundary; direct production database access was not substituted |
| Historical executor failures | **passed, limited scope** | Original 47 receipt hashes/diagnostics independently inspected read-only; not application context-event evidence |
| Current platform GitHub App repository permission | **blocked / not verified** | No approved platform-authenticated read-only access check available; connector identity is a different authorization path |
| Real OpenAI / GitHub engineering cycle | **not executed** | No paid request or disposable remote delivery authorized or attempted in this M0 follow-up |
| Real isolated generated-workload tests | **skipped** | Five explicit opt-ins; Docker image packaging success does not prove workload isolation |

These are platform regression results, not specialty challenge execution. Model/challenge/instruction revision for certification: N/A. No live model was invoked; no provider token usage or estimated live cost is claimed. Test-only approval actions belong exclusively to the deterministic fixture, not production human approval or certification.

The integration results now directly exercise QA → Backend, QA → Frontend, Review → Backend and Review → Frontend rework, retesting and persistent plan/counter recovery (`RuntimeFlowTests.ReworkRunsOnlyResponsibleDeveloperThenRepeatsTestsQaAndReview`), published-review rework with another owner decision, MongoDB lease fencing/idempotency, committed-transition recovery, stale-revision no-op rejection and approval workflows. The bounded BackendEngineer protected-overflow regression and every-role long-loop tests also passed at the exact tested revision. This closes the original unexecuted deterministic rework/restart/regression uncertainty, without establishing agent competence or live provider behavior.

### Historical BackendEngineer failure: two distinct evidence classes

[backend-failure-receipts.json](reports/2026-10-29/m0-followup/backend-failure-receipts.json) records direct receipt hashes, outcomes, evidence IDs, workspace revisions, output lengths and diagnostic markers for run `c8413d772ebd4246816ce89aef159163`. Access was read-only and restricted to direct operation receipts; no state.json, credentials, control-home or repository code was read/executed.

Of 47 receipts, 43 succeeded and four failed with exit code 1. Three failures (`ccaaa985340f…`, `6338fee8602a…`, `a283cafb6701…`) contain **MSB1003** and the standard “Specify a project or solution file” message. The fourth (`af5597a84b95…`) contains **MSB1001** and “Unknown switch.” These establish MSBuild project/argument execution errors, not provider context-window errors. Full receipt names and hashes are retained in the evidence JSON. No context-budget marker was present in these executor outputs; this absence is expected to be inconclusive because context assembly runs in the application before a provider request, not in MSBuild.

At tested commit `ff528ba`, `backend/docs/architecture/008-agent-runtime-reliability.md` and `backend/docs/runtime-reliability-verification.md` document a separate application failure: roughly 33 KB initial context grew through repeated model output to 95,908 conservative context tokens plus instructions/framing. These are now committed historical engineering reports, corroborated as a documented rationale by merged PR #9. They are **not a newly retrieved persisted ContextBudgetExceeded event**. The current passing bounded fixture proves explicit fail-closed overflow behavior, not exact replay of the historical transcript or proof that an MSBuild error caused the context overflow. That causal chain remains **not verified** until the authenticated run/event ledger can be inspected.

### Agent metadata and GitHub App permission boundary

The running platform is reachable at loopback port 8080: `/health/live` returned 200 and `/api/v1/auth/me` returned 401 without a session. An approved authenticated read-only interface/session was requested; none was supplied during this verification. No password, token, private key, browser credential store, production database or container environment was accessed. Consequently the current persisted agent definitions, exact instruction/model/pricing revisions, run terminal state, event ordering and context-budget payload cannot be reported as observed facts. Source-default inventories above remain explicitly separate from runtime metadata.

The old installation-token 422/owner CLI 403 blocker is historical. Via the connected GitHub read interface, the historical disposable fixture [PR 1](https://github.com/ghptc/aep-sandbox-m8-20261028-3a0ed9/pull/1) now reports **closed and merged**, authored by **`ai-engineering-platform-local[bot]`**, with head commit `1eb536ce57b8ebc0f5346f7d6f5572f142abdd85`. This corroborates that platform App access worked at that earlier delivery. It also supersedes the original report's historical “open and unmerged” state for that PR; this audit did not merge it.

A connected GitHub identity reading that PR does **not** establish current repository-scoped installation-token access for the running platform App. Its current installation/repository mapping and permissions remain unverified. The old blocker must therefore not be presented as a confirmed current failure, nor declared permanently resolved. No installation permissions, credentials, repository settings or remote artifacts were changed to test access.

### M0 disposition and remaining blockers

**M0 remains pending.** Stable-snapshot attribution, clean installation, complete deterministic backend/integration/browser regression and Docker packaging are verified. The database refusal was investigated and safely bypassed using disposable infrastructure; it no longer blocks these completed checks. Existing source architecture and seven independent roles remain intact. No M1–M6 work was performed.

Remaining mandatory evidence:

1. Approved authenticated read-only export of current agent IDs/roles, enabled state, prompt revision/hash, model/configuration/pricing and tool permissions. Do not infer runtime state from AgentCatalog defaults.
2. Authenticated persisted run/events for the historical BackendEngineer failure and latest relevant execution, including ordering, stage/terminal failure, ContextBudgetExceeded inventory and instruction/model identity. Executor MSBuild receipts alone cannot satisfy this.
3. A current read-only repository-access result from the actual running platform GitHub App identity, without changing its credentials or installation permissions. Historical bot delivery and separate connector access are insufficient.
4. Human review/acceptance of the updated audit. PR #10 remains unmerged; no merge or agent certification is requested or claimed.
