# ADR 007: Lightweight local task orchestration

Status: Implemented incrementally on the existing workflow, following ADR 006.

## Scope and boundaries

Keep .NET 10/C#, MongoDB, React/TypeScript/Ant Design, domain transitions, application use cases, OpenAI Responses SDK, GitHub App and the external local Docker daemon. Add no new workflow engine, service bus, repository abstraction or deployment tier. Existing projects, conversations, artifacts and recoverable executions retain their data and legacy route. No migration rewrites prior runs or budgets.

## Task execution

`EngineeringTask` is an explicit bounded description, acceptance criteria and optional references to selected requirements/human instruction messages. A task declares whether BA and/or architecture input is needed and selects implementation specialists. The default simple plan is Developer → Test Engineer → QA → Reviewer; complex tasks opt into BA → Architect → Developers → Test Engineer → QA → Reviewer. Test execution and reviewed human-approved delivery remain mandatory. A developer works in the connected repository's existing language; a JavaScript fixture does not require adding .NET or frontend scaffolding.

`POST /api/v1/projects/{id}/tasks` accepts a bounded authorized task queue. Tasks share one immutable run policy, start time, cumulative model cost, usage ledger, tool-call ceiling and correction/iteration allowance. Next-task progression occurs only after actual source changes, tests at a verified workspace revision, QA, actual diff review, explicit delivery approval and confirmed commit/push/PR/review artifacts. Progression increments the iteration and task index rather than resetting budgets. A remaining task that cannot fit the iteration budget stops with an explicit failure. New task scope is never invented from untrusted model text. Automatic continuation means continuing the authorized queue; it does not mean unbounded new work, automatic merging or implicit approval of another delivery.

The existing project-wide `POST /runs` route remains available for planning new projects and preserves old stored behavior. The UI makes focused task launch the primary action and retains the older execution action. Both routes use the same scheduler, lease ownership, optimistic versions, idempotent checkpoints, remote fences and secure executor.

## Three context concepts

- **Project knowledge:** repository metadata, stack, saved standing human instructions and a reference to original project context. Large project descriptions and conversations remain in MongoDB, retrievable by bounded project-scoped `ReadProjectMemory` calls. Original context is read from the immutable project snapshot when explicitly requested.
- **Current task:** only its description, acceptance criteria, selected requirement records, selected human message references, relevant current-task BA/architecture constraints, selected specialists, stage and unchanged safety policy. Synthetic requirement IDs map explicit task criteria to QA without duplicating the entire task body as another requirement document.
- **Execution evidence:** a few current-iteration test/diff/PR evidence references and their persisted statuses/revisions. Full output is retrieved when needed. QA receives the exact required test checkpoint ID explicitly; older preliminary tests cannot replace it.

Focused contexts never query the project's full message history. Existing free-form conversations are knowledge, not automatically promoted to permanent instructions. Owners save lasting constraints once through Project instructions, or explicitly attach selected human message IDs through the task API. Legacy runs continue protecting their existing human-message context. Selected human instructions are project-checked and never summarized away. All references, previews, handoffs and repository text remain data and cannot authorize sensitive operations.

Each specialist starts from newly assembled task knowledge instead of inheriting another specialist's entire transcript. A persisted `AgentHandoff` carries Summary, CompletedWork, RelevantArtifacts, ValidationResults, Blockers and NextAction. A bounded preview of the latest completed current-task handoff accompanies the next specialist, with a reference to the full original. Current analysis questions/constraints remain explicit. Handoffs do not establish passing tests or authorization. Full collaboration messages remain observable in the existing UI, alongside a new handoff view, selected specialists, task queue position and existing durable timeline.

## Live-discovered contract fixes

Node's default test reporter emits `ℹ tests N`; the executor now recognizes it and TAP's `# tests N`, including ANSI decoration, while still rejecting zero tests and checking exit code/time/output/isolation. This keeps the existing sandbox boundary intact.

QA originally returned descriptive sentences in its evidence array despite exact-ID approval checks. The schema and QA context now explicitly require exact bare evidence IDs, keeping explanations in check details. `QualityGate` retains strict ID matching, success, workspace revision and selected requirement coverage; prose claims do not pass. Required test metadata survives specialist context rebuilding.

Provider monetary reservation now includes actual request instructions and schema plus framing, as well as the explicit maximum output allocation. Model-window/input/output settings remain configurable and unchanged for existing executions.

## Verification boundary

See the current entry in [verification.md](../verification.md). It separates deterministic regression workflows, browser tests, real Docker/Git isolation tests and the successful real OpenAI + GitHub App + Unix-daemon workflow. The owner application and daemon are not silently redeployed by verification. A dedicated temporary verification daemon uses the existing allowlisted disposable repository, pinned image and fixed `node --test` profiles with separate state/socket; it never mounts a Docker socket into the application or workloads.
