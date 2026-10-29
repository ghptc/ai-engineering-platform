# Recovery and implementation status

On recovery, git contained only the initial commit plus the preserved uncommitted .NET/React implementation. Temporary logs were lost, but source files were intact. The existing MongoDB test container was restarted without replacing its data. No repository reset or source discard was performed.

Completed during recovery: reconciled source and architecture; repaired browser selectors and event refresh throttling; strengthened role-specific outputs, requirement traceability, QA/test revision gates, delivery review timing, durable events and approval reconciliation; implemented analyst conversation replies, aggregate statistics and idempotent PR webhook state; hardened external adapters and tool paths; added contract/recovery tests; completed README, ADRs, sandbox/API documentation and engineering conventions; rebuilt and smoke-tested the final production image.

Final local results: backend builds with zero warnings/errors; 45 backend tests pass; TypeScript and production React builds pass; seven frontend tests pass; the Playwright engineering workflow passes; dependency audits report no known vulnerabilities; the production image passes 13 HTTP smoke checks and runs as a healthy non-root container without Node.

Live external verification remains blocked on OpenAI credentials/model access, a GitHub App installation/private key, and a separately provisioned secure remote executor. No live operation or isolation guarantee is fabricated. See [verification.md](verification.md) for exact evidence and [README](../../README.md) for setup and limitations. Verified milestones are finalized through Conventional Commits; inspect `git log` for source-control history.

## Adaptive context management (2026-10-28)

Implemented configurable model-window and agent-input limits with explicit output reservation, conservative token upper-bound sizing and final provider validation. Replaced recursive context snapshots with references; retained protected requirements, architecture, policy and human instructions; summarized execution/conversation history; added paged project-scoped original retrieval, bounded tool previews and context telemetry. Original execution data is retained. See [ADR 005](architecture/005-adaptive-context.md) for defaults, sizing tradeoffs and regression scope. Live model execution remains unverified by this milestone.

## Architect protected-context regression (2026-10-28)

Replaced project-wide protected agent transcripts and repeated backlog artifacts with selected requirement records, structured architecture constraints and current final analysis fields. Long historical conversations remain in MongoDB and are fetched on demand. Existing input/output/model-window settings and monetary budgets are unchanged. [ADR 006](architecture/006-focused-context-regression.md) records the matching persisted inventory, estimate arithmetic and regression scope.

## Lightweight local engineering tasks (2026-10-28)

Added bounded focused tasks, optional BA/Architect participation, selectable developers, persisted structured handoffs and automatic progression through an authorized task queue with cumulative budgets. Project knowledge, current task and execution evidence are separate context sections. The UI launches tasks, saves standing instructions and displays task position, selected specialists, handoffs and the existing durable collaboration/timeline. Legacy projects/runs remain intact. Strict QA evidence IDs, actual test revisions, human delivery approval and sandbox/Git fencing remain enforced. Node default-reporter compatibility was fixed from real execution evidence. [ADR 007](architecture/007-lightweight-task-orchestration.md) documents the minimal change and scope.

A real focused run completed against the allowlisted disposable JavaScript repository: answer() and its existing test changed from 1 to 2, node --test passed in Docker, QA and actual diff review passed, and owner-authorized disposable delivery produced a real commit, pushed feature branch, draft PR and published review. It did not run BA, Architect or Frontend Engineer, mutate the default branch or merge. Detailed identifiers and verification caveats are in the current verification report. The owner's running application/daemon are preserved; deploying the reviewed changes is a separate operation.

## Engineering workspace UX redesign (2026-10-28)

Completed the route/component/contract audit before edits; priorities and rationale are in [ux-audit.md](ux-audit.md). The dashboard now leads with active engineering, owner actions, deliveries and persisted team activity. Usage & Costs owns receipt totals, dimensions, trends, pricing and budget alerts; model behavior and security/runtime status stay in Settings & Agents.

Execution and collaboration now show the selected specialists, factual states, task-scoped recorded handoffs, readable saved messages, current/next work and direct owner approval/clarification. Event activity is newest-first, bounded to 50 rows in a viewport, cursor-paged and frozen while inspecting older activity. A lazy evidence Drawer exposes the full original, recorded tool arguments/output and artifact references. Shared SignalR subscriptions batch/deduplicate updates and recover complete persisted replay. Project Markdown/context disclosure, compact requirements, safe reviews, distinct client conversations, responsive navigation and keyboard focus were improved using the existing React/TypeScript/Ant Design architecture.

Backend changes are bounded read projections, complete replay, scoped pricing and an authorized clarification use case. No orchestration, sandbox, GitHub delivery gate or budget policy was replaced. No existing data was migrated or deleted. Concurrent unrelated owner/backend work was preserved in its original worktree; this milestone was isolated in a feature worktree.
