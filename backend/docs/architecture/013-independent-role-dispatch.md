# ADR 013: Durable Backend and Test worker dispatch

Status: M3 implementation. Baseline: merged M2, `7cbf52d`.

## Scheduling and authority

New executions optionally persist `AgentRuntimeVersion=2`; absent historical values remain version 1. `AGENT_RUNTIME_V2_NEW_RUNS` defaults to false and affects creation only. Backend Engineer and Test Engineer use independent role loops consuming a Mongo queue. Other roles retain their compatibility adapter until M4. There are two bounded consumers, not one worker per possible role.

The orchestrator retains dependency scheduling, iteration/rework transitions, run ownership, sandbox calls, repository fencing, accounting, progress detection, QA, review and human delivery approval. Workers perform bounded model invocations and return proposals. They never directly invoke repository tools. Thus no concurrent modifications of a shared execution workspace are introduced. Conditional parallelism across isolated independent workspaces remains future work; this milestone does not claim concurrency from role names.

A stable task/attempt identity binds an immutable context frame, source revision from actual tool records, assigned role/instruction snapshot, objective, criteria and a subset of existing permissions. Context is frozen before queue visibility and reused on checkpoint recovery. The compatibility workflow builds specialist context at stage entry and adds bounded tool evidence between turns; the runtime does not reconstruct a transcript. Defaults are three provider attempts and 90 seconds, configurable through `AGENT_RUNTIME_TASK_MAX_REQUESTS` (1–10) and `AGENT_RUNTIME_TASK_TIMEOUT_SECONDS` (5–300). Existing role token limits and remaining execution cost/tool allowances cannot be raised by a worker.

## Durable lifecycle

`agent_dispatch_v2` stores Scheduled → Running → Completed or OutcomeUnknown/Expired. Claiming is an atomic status compare-and-set with a worker identity. Indexed role/status/creation ordering bounds queue selection; expired and previously claimed tasks are never automatically reclaimed for another provider call. No Mongo transaction spans a model request.

The processor checks runnable state, exact stage/iteration/task, live orchestrator lease and unchanged role authorization before calling the runtime. Run control changes cancel the invocation. A completed runtime checkpoint can repair the crash gap before queue completion only when the original context/fingerprint matches; this is evidence reconciliation, not another provider call. Reading that result checks current role authorization again. Failed/uncertain invocations retain source, context, original worker, task and runtime identities and usage receipts.

Versioned EngineeringTask and EngineeringHandoff records are persisted alongside the legacy operational history. Handoffs remain unverified model summaries and reference the complete original task record. On recorded test/QA/review rejection, a versioned ReworkRequest references actual tool records at the source revision and exactly the responsible selected developer(s). Existing targeted rework routing then reruns tests, QA and review. No model decision grants verification, approval or permission.

## Completion and security review

A worker's Completed status means only that its bounded model operation returned. Existing workflow gates still require actual source/no-op reconciliation, required tests, revision-consistent diff, requirement-mapped QA, review and owner approval before delivery. The runtime evidence resolver separately rejects passing zero-test reports, duplicate receipts and mismatched scope/revision. Production uses the official Responses provider, Mongo persistence and the controlled executor; deterministic adapters remain test-only.

Reviewed boundaries: no API Docker socket; no new agent tools, host commands or repository permissions; no automatic retry of ambiguous calls; immutable frame/message identities; atomic task claims/completion; current role/run ownership checks; retained budget reservations and tool IDs; unchanged inspection-feedback/stop thresholds and resume behavior. No session material is added to task context. API-only containers do not host role consumers.

## Migration, recovery and rollback

Deploy the compatible M3 build to API and worker before enabling `AGENT_RUNTIME_V2_NEW_RUNS=true` for new runs. Keep role definitions/models/prices configured through existing authorized settings. There is no migration of existing executions, executor workspaces, receipts or checkpoints. Disabling the flag creates legacy runs again while persisted V2 runs continue on V2.

For interrupted work: inspect the run, dispatch identity, frozen frame, runtime checkpoint and provider accounting. A matching completed runtime result is automatically reconciled without spending again; resume is still an explicit owner action if the run is paused. Unknown outcomes remain blocked. Do not delete records, reset the inspection guard, reuse an identity with different input, refund unknown reservations, or increase limits to escape the blocker. Owner-facing explicit-attempt reconciliation controls are a subsequent UI/reliability increment; M3 does not authorize a blind retry of an unknown call.

For binary rollback, pause V2 runs and retain all collections and keys; restore the M3-compatible image before resuming them. Do not let an older binary that ignores AgentRuntimeVersion resume a V2 run. Historical legacy runs retain their original route. No customer run is resumed or migrated by deployment.
