# ADR 008: Bounded agent context, persisted workflow routes and execution capacity

Status: Accepted for implementation; owner review required before merge.

The local-first modular monolith, specialist roles, MongoDB checkpoints, official Responses SDK, GitHub App and isolated executor remain in place. No graph engine or background orchestration framework is introduced.

## Evidence and context decision

Read-only inspection of local persisted run `c8413d772ebd4246816ce89aef159163` found an initial implementation inventory of roughly 33 KB, including 12,632 requirement bytes and 10,815 architecture bytes. The eventual Backend Engineer context reached 95,908 conservative input tokens before 2,136 instruction tokens and protocol/framing reserves. Growth occurred within the tool loop, not solely in initial assembly. Agent replies repeatedly carried acceptance criteria, requirements, decisions, checks and findings. Unfocused specialists shared accumulated context; focused contexts included repeated task artifact revisions. Historical context snapshots were separately persisted and must never become nested input.

Every specialist now rebuilds its persisted task envelope before starting. Current requirements, acceptance criteria, standing/human instructions and security/approval boundaries remain protected. Latest applicable analysis and architecture revisions replace repeated revisions. Model replies remain durable task/message/artifact records instead of being appended as protected context. Handoffs contain bounded previews and source references. Tool evidence is deduplicated by operation ID, retaining eight recent entries and up to four recent failures; older bodies/records remain available through project-scoped paged ReadProjectMemory. Larger previews are compacted before enforcing the configured model window, input allowance and output reservation. Overflow reports component byte/token upper bounds and requests a smaller task; it never truncates protected requirements. The estimator deliberately remains a conservative UTF-8 byte bound rather than assuming an unavailable model tokenizer.

Legacy unfocused runs retain their explicit requirements and human instructions. They can still require a smaller task if that protected scope itself is too large. Focused tasks retrieve original project knowledge on demand. No historical records are migrated or deleted.

## Workflow and rework decision

Run state now carries forward AgentTransitions and an explicit bounded ReworkPlan. QA/code review structured output identifies ReworkTargets from the selected developers. A sole selected developer is unambiguous; multiple selected developers require explicit targets. Both targets execute in selected order. Finding references point to the originating result artifact and JSON field/index. Each return records source, target, reason, task, iteration, tested revision and timestamp. Successful QA creates a forward transition only after QualityGate validation. A proposed model approval cannot create a successful QA arrow.

Rework consumes the existing iteration budget and runs only responsible developers, then test generation/execution, QA and review. Prior return metadata stays persisted. Published review rejection also creates targeted rework on the run-owned workspace and requires another owner delivery decision before updating its branch/PR. Independently reconciled external feature branches still require explicit existing-branch handling; duplicate delivery and protected-branch merges remain blocked. Limit checks, lease ownership, operation keys, remote fencing and human delivery approval are preserved.

## Capacity decision

Presets are explicit choices: Quick Fix (30 tools/3 iterations/1 retry/30 minutes/USD 2), Standard Development (100/5/2/90/USD 5), Extended Autonomous Development (300/10/3/180/USD 10). Existing project defaults are unchanged. Server ExecutionCapacity configuration independently caps requested limits (defaults: 500 tools, 10 iterations, 3 retries, 240 minutes, USD 10), within domain hard maxima. Configure nested settings using `ExecutionCapacity__MaxToolCalls`, `ExecutionCapacity__MaxIterations`, `ExecutionCapacity__MaxRetries`, `ExecutionCapacity__MaxDurationMinutes`, `ExecutionCapacity__MaxBudgetUsd`.

Policy AgentToolBudgets are advisory per-role thresholds. Unused capacity remains available to other agents; the shared execution allowance is the hard stop. At 80% the model is prompted to checkpoint and avoid oversized work; a tool batch exceeding remaining capacity pauses before starting. Exhaustion pauses execution with checkpoints preserved. Retries are bounded per stage, with legacy retry counts retained conservatively. Model and tool receipts rebuild per-agent/per-task/per-execution usage after recovery; a cached model request is not charged again. Failed tool results and subsequent identical-argument retries are counted separately.

Owner-authenticated POST `/runs/{id}/limits` requires optimistic version, explicit policy and reason. Extensions only increase limits and cannot alter review/merge policy. An atomic run outbox records the approved policy change. Extensions do not resume work automatically or reset usage, revisions, operation IDs or checkpoints. Time extensions are total elapsed duration from original start. Successful read-only inspections are reused within one leased stage until any non-read operation; external GitHub facts and tests are not cached.

## UX and security

A lightweight keyboard-accessible SVG shows selected specialists, persisted forward/return paths and factual agent states. Animation applies only to active transitions and respects reduced motion. Panels scroll internally. Each agent opens a read-only console with paged tools/output, filtering, copy, follow/pause and jump-to-latest; diff inspection and structured operational activity are separate tabs. Model requests are not presented as shell execution. New executor results retain stdout/stderr, fixed profile command, working directory and exit code; legacy evidence explicitly lacks separate streams. Commit/PR artifacts describe shared execution delivery, not invented per-agent commits.

Tool output, detail projections, live delivery and actionable API errors are redacted. No terminal input, host shell endpoint, Docker socket mount, repository allowlist change or production test adapter is introduced. Provider, context, executor, profile, GitHub and quality failures retain failed/blocked states and safe diagnostic categories.

Limitations: operations update after completion rather than streaming subprocess bytes; older records lack reliable agent attribution or separate streams. Full source/diff originals remain on-demand and bounded by executor limits. Live provider verification requires its existing opt-in configuration and is not implied by deterministic browser/integration or real local Docker tests.
