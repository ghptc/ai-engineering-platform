# README-only planning investigation — 2026-10-29

## Evidence and root cause

Platform baseline: latest fetched `origin/master`, `ff528ba7a85e4617625c6372e86d69f3fbf896cf`. Feature branch: `fix/readme-planning-handoff`. Original untracked `backend/docs/certification/` work was preserved and excluded.

The UI's paused run is `036881e63cbe4aedb9a6bdc97dcc4c18`, project DevPulse, target **ghptc/devpulse**, main; it is not a run targeting the platform repository. Read-only MongoDB queries inspected the run, task checkpoints, messages, tools, handoffs/events and persisted BA definition. Local executor configuration and source were inspected without invoking the production executor or editing its state.

The run transitioned Pending → Analyzing → Paused, resume stage Analyzing, outcome Blocked. BA made three model requests and five tool calls, cost $0.093054. ReadRepository returned only `README.md`, no source changes and `TestProfiles: []`, at base/head `0c6c5415864bc0f9ccb56959477b98d2d9f4fb97`, workspace revision `78ed296a2960f8a48d50231ae1fbd5dfd6fd3806da75079e63b6f35d910b3fec`. Evidence: `local-0d09b4999a59ca163c8433bd4a5dd4acc2c0abaf4e9425556e8767a677b12409`. Reconciliation reported no branches/PRs. ReadFile returned `# DevPulse`, evidence `local-3812652a770eb1c360f9e7d3c7f43daee1a8c17cdcd933352522d4a8243cbc85`. Two other calls retrieved prior architecture and development context. The architecture preview itself described SDK/cache availability as unverified.

The final BA output had a structured backlog and an explicit next task for Architect, but decision BLOCKED because verified delivery lacked executable profiles. It correctly said SDK/dependency availability was unverified, not proven absent. There was no Architect transition, source edit, test execution or delivery in this run.

| Candidate cause | Finding |
| --- | --- |
| BA instructions/schema | Contributing cause. Persisted BA instruction revision 3 had generic missing-capability blocking language and conflicting business-ambiguity guidance. The output schema also directed critical open business questions to BLOCKED. Neither represented blocker stage or separated risks/prerequisites structurally. |
| Workflow | Direct routing cause. Every BLOCKED decision immediately threw TaskReconciliationException, irrespective of safe planning work or future-stage scope. The worker paused at Analyzing. |
| Bootstrap | File bootstrap already exists through bounded WriteFile/ApplyPatch. No arbitrary scaffolding shell or network installer exists or is added. A repository without manifests is a valid planning input. A repository without any Git commit still needs an owner-provided initial commit for the existing checkout/revision protocol; this change does not claim unborn Git checkout support. |
| Profiles/offline dependencies | Real unresolved execution prerequisites. TestProfiles contains only profiles applicable to current files, so an empty list on a README checkout does not mean no configured commands. SDK/cache availability was not measured by those tools. |

The inspected allowlist includes devpulse with fixed backend (`dotnet test backend --ignore-failed-sources -p:NuGetAudit=false`), aggregate, frontend and build commands, pinned to image `sha256:ba3d80d3f5077a1eaac0a3627bb3b9723684f39d391f9cf996fc90dbf90e44be`. The backend selector can become applicable after a project/solution file exists. Composite aggregate/frontend commands have no RequiredFiles, so the current fail-closed selector will never advertise them. This is an administrator configuration prerequisite, not permission for the agent to choose commands or images. Image source includes .NET 10 and Node; that alone does not prove the deployed immutable image contains all required offline packages, Git or browser binaries. No production image workload was run to establish that.

## Implementation and review

- Strict model outputs now include planning risks, prerequisites, blockers with stage/reason/evidence, bootstrap strategy, dependency strategy and test strategy. Assumptions remain separate. New final planning CONTINUE outputs require a planning assessment; Architect must supply all three strategies.
- Default instructions and context explain README/empty planning inputs and current-file applicability. Existing stored agent definitions receive a versioned, archived instruction migration on a future deployment. No deployed definitions were changed during investigation.
- Routing defers only explicit future execution blockers with a complete BA backlog or Architect strategies. Original model decisions remain persisted in task/message evidence; a deferred-blocker event records routing. Legacy, ambiguous and current-planning blockers remain fail closed. Human clarification is never overridden.
- Confirmed deferred blockers are checked before implementation, across paginated persisted task records for the current task. They cannot be cleared by a developer suggestion. A new owner-reconciled execution is needed after provisioning prerequisites; cached legacy BLOCKED checkpoints are deliberately not rewritten.
- ReadRepository adds configured profile descriptors, current applicability, pinned images, fixed commands, file bootstrap operations and explicit unverified dependency status. It preserves TestProfiles semantics and network denial. No new execution permissions or dependency installer were introduced.
- When metadata explicitly shows no configured test profile, implementation stops before developer edits. Inapplicable/unsupported profiles still cannot execute; the required-test gate stops before QA. QA requires successful evidence and workspace revision. Human delivery approval, budgets, leases, fences, idempotency and revision checks remain enforced.

Manual diff review covered routing scope, retained original evidence, blocker persistence/pagination, old checkpoint compatibility, instruction archival, unchanged sandbox permissions and QA gates. Staging excludes local config, credentials, generated evidence and the owner's certification documents.

## Verification

Final backend source/test manifest SHA-256: `e0be3b79b7eb2487be171d7aa8a5e5c88c5438b6ffef984adc5ae070363757e0`. Computed by sorted tracked `backend/src` and `backend/tests` paths plus the three new source/test files, hashing each UTF-8 path, NUL, file bytes, NUL. This binds results to the committed source/test contents independently of documentation changes.

- `dotnet restore backend/AIEngineeringPlatform.sln`: passed.
- `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1`: passed, zero warnings/errors.
- `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27030 dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1 --logger 'trx;LogFilePrefix=planning-final' --results-directory /tmp/aep-planning-test-results`: passed, **136 passed, 7 skipped** (46 Application, 2 Architecture, 18 Domain, 50 Integration, 20 LocalExecutor).
- TRX evidence: `/tmp/aep-planning-test-results/planning-final_net10.0_20261029104727.trx` through `20261029104749.trx` (five assemblies).
- Deterministic coverage: empty/README applicability and future bootstrap; successful BA→Architect planning and required tests/QA reaching human approval; unsupported test profiles; no configured test profiles before editing; genuine infrastructure blockers deferred through planning but stopped before implementation; genuine planning blockers; legacy/ambiguous blockers and human clarification; incomplete Architect strategies; QA rejection of failed/missing/revisionless evidence; official SDK parsing of the strict planning contract.
- `docker build -t ai-engineering-platform:planning-regression .`: passed. Image config `sha256:d1bd8e42f5af0a1d9bef49c8e3a6a17fed5849d9cf85d0444b9c3a322dff2777`. Separate tag; no runtime replacement. Frontend build layers were cached.
- `git diff --check`: passed. Frontend-specific checks/browser regression were not run because no frontend files changed.

The first sandboxed integration attempt could not connect to MongoDB. An SDK contract fixture initially lacked the new required planning field and was corrected. A later rerun lost its disposable MongoDB 8.0.34 server to signal 11 (exit 139, not OOMKilled); those failures are not counted as passing evidence. The final run used a fresh disposable server on 27030 with an explicit 0.25 GB WiredTiger cache and passed. Existing owner services were left intact.

Five opt-in real Docker/executor tests and two live OpenAI/GitHub tests were skipped. The application image build is real Docker build evidence, not a workload isolation test or live agent delivery. No live end-to-end success is claimed.

## Remaining prerequisites and rollout

An administrator must verify the actual pinned runtime's .NET/Git/frontend offline dependencies, supply appropriate RequiredFiles for approved composite profiles (including an aggregate covering backend and frontend), and provide a separate browser-capable image/profile if browser tests are required. Keep no-network workloads, no mounts/credentials, fixed argument arrays and immutable image IDs. Missing dependencies must be provisioned through reviewed offline image preparation, never agent egress.

Deploy the reviewed platform and daemon changes explicitly; this investigation did not restart them, resume the paused run, change its repository snapshot or grant delivery approval. After provisioning and deployment, use an authorized disposable repository and budget to execute a live validation if desired. Production delivery remains pending actual source/test evidence, QA, real diff review, human approval and confirmed delivery artifacts.
