# Certification baseline audit — 29 October 2026

Status: discovery report delivered; **M0 acceptance pending**. No agent is certified. M1–M6 have not been executed. Missing runtime inventory and current integration evidence prevent declaring a stable production baseline. This report does not authorize paid model execution, delivery approval, deployment or merging.

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
| AUD-08 | P0 / Not Verified | Actual current runtime registry, latest persisted real-run failure and original receipts for c8413d772ebd4246816ce89aef159163 have not been independently inspected. ADR 008 reports the run but is owner-authored uncommitted documentation, not fresh runtime proof. |
| AUD-09 | P0 / Not Verified | Required current full MongoDB/integration/browser regression is incomplete. Partial passing suites do not satisfy a stable baseline exit gate or justify production orchestration edits. Sources: verification table below; PlatformFactory.cs and playwright.config.ts prerequisites. |
| AUD-10 | P0 / Not Verified | Concurrent source/test edits were detected between initial manifest and final comparison. Process exit success is observed, but exact tested source identity is not established. Capture an isolated reviewed snapshot before repeating acceptance checks. See reports/2026-10-29/concurrent-changes.json. |

## Protected-context reproduction

**Passed, bounded deterministic fixture.** `ContextBudgetTests.ProtectedOverflowFailsExplicitlyInsteadOfTruncating` constructs 180,000 ASCII characters in OriginalContext with BackendEngineer configured at 96,000 maximum input tokens, 128,000 model window and 4,000 reserved output tokens. It asserts an explicit exception containing “No protected requirements were discarded.” No provider request, repository workload or historical record mutation is needed. The same suite verifies output reservation, Unicode sizing and evidence/revision retention. RuntimeReliabilityTests separately verifies repetitive reply/evidence growth for every existing role.

This reproduces fail-closed overflow, **not the exact historical BackendEngineer transcript**. `backend/docs/architecture/008-agent-runtime-reliability.md` reports run `c8413d772ebd4246816ce89aef159163`, approximately 33 KB initial inventory, 12,632 requirement bytes, 10,815 architecture bytes and 95,908 conservative context tokens plus instructions/framing. The proposed cause is tool-loop accumulation. Original receipt inspection and exact historical replay remain pending. ContextBudget uses a conservative UTF-8 byte upper bound rather than a provider tokenizer; those numbers must not be described as exact billed tokens.

## Historical real-run and GitHub permission investigation

`backend/docs/milestone-8-verification.md` records repository-scoped installation-token HTTP 422 for installation 176070757 and an earlier owner CLI HTTP 403 while changing repository access. It explicitly refused to substitute owner OAuth for App credentials. This establishes a historical permission blocker, not current installation authorization.

Later `backend/docs/verification.md`, “Lightweight local task workflow,” records successful run `f90535012966492c97c04698c84803cb`. Read-only inspection of `/tmp/aep-focused-live-20261028/test.log` independently found the run/PR/usage receipt, the named live test passing and “Test Run Successful.” It reports 53,530 tokens, USD 0.146036 and 24 tool calls, against the disposable `ghptc/aep-sandbox-m8-20261028-3a0ed9` PR 1. Historical documented model/configuration: gpt-6.1-sol, USD 2/10 per million input/output tokens, USD 1 run cap. The logged test performed a tiny Node answer() change using Backend/Test/QA/Reviewer, not commerce certification or the full seven-role route.

Earlier failed test logs remain beside it: `test-attempt1.log`, `test-attempt2-environment.log` and `test-attempt3-evidence-contract.log`. The verification report attributes failures to zero recognized Node tests, MongoDB availability, and explanatory strings instead of exact QA evidence IDs. Their presence and failed-test markers were observed; a fresh causal replay was not performed. No failed receipt was removed. The latest runtime failure cannot be inferred from these dated test logs.

Conclusion: the old permission blocker was followed by historical evidence of one successful authorized fixture delivery. Current installation permissions, current remote PR state and current live provider behavior remain Not Verified. No external requests or credential reuse were performed in this audit.

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
| Full unfiltered dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1 | not executed | Isolated MongoDB endpoint not confirmed; existing tests target 27028 or TEST_MONGODB_CONNECTION_STRING |
| npm ci --prefix frontend | not executed | Existing dependency tree used; clean-install verification remains pending |
| npm run e2e --prefix frontend | not executed | Requires isolated MongoDB and dedicated non-reused ports; no owner service reused |
| docker build -t ai-engineering-platform:local . | not executed | No current packaging/isolation claim |
| Real Docker workload tests | skipped | No RUN_LOCAL_EXECUTOR_TESTS opt-in or immutable image selected |
| Live OpenAI / GitHub / daemon cycle | not executed | No live opt-in, budget, credentials, installation or disposable repository inferred |
| Manual checkout keyboard / screen-reader review | not executed | FE-001 application and validation do not exist |

## M0 acceptance and remaining uncertainties

Discovery made no application source changes and preserved prior modifications, commits, runtime configuration and execution data. Findings distinguish source, fixture, historical report and current observed evidence. The audit documentation adds only this report and its initial-state evidence. Source/diff review for the audit must verify that only these additions are staged; owner runtime changes must remain unstaged.

M0 remains pending for the following explicit prerequisites:

1. Read-only export of current agent definitions and revisions, model/configuration and tool permissions, without secrets. Source defaults cannot answer this.
2. Read-only latest run/event/task/tool inventory, including the BackendEngineer failure's original diagnostics and receipts. Establish which run is actually latest.
3. Confirmed disposable MongoDB endpoint and current full integration/rework/restart regression; then dedicated browser regression and clean dependency verification.
4. Independent review of the preserved runtime source diff and accepted baseline revision. Capture a stable isolated snapshot, then repeat tests against its exact revision: concurrent changes currently prevent that evidence binding. Passing unit tests on uncommitted owner work do not authorize committing that work.
5. Human review/delivery approval for subsequent implementation. This report grants no certification or automatic merge permission.

After M0 is accepted, M1 should add trusted versioned challenge/rubric definitions, pinned model/instruction/source identity, append-only evidence and deterministic decisions through Domain/Application/Infrastructure boundaries, with tests for missing evidence, failed critical gates, stale revisions, forged agent claims and three-distinct-variant eligibility. Do not modify production orchestration before its stable baseline is established. All specialty challenge execution and TEAM-001 remain downstream, unexecuted work.
