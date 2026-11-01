# ADR 014: Full specialist runtime and persisted worker observability

Status: M4 implemented and verified. Baseline is merged M3 (`ed6ebd9`, PR #25; source commit `4d27527`).

## Roles and workflow

All existing specialists now use durable dispatch for new V2 runs: Business Analyst, Architect, Backend Engineer, Frontend Engineer, Test Engineer, QA Engineer and Code Reviewer. Two additional roles have concrete bounded capabilities: Infrastructure Engineer edits explicitly requested repository-owned configuration through the existing controlled source/build tools; Security Engineer reviews the tested diff through read-only tools. Neither can deploy, access a host shell/socket/credentials, grant exceptions or approve delivery.

The new enum values are appended, preserving numeric identities of existing records. Existing/default seven-role plans remain unchanged. Focused tasks explicitly select `requiresInfrastructure` and/or `requiresSecurityReview`; these options require V2. New role definitions are seeded additively without replacing owner configuration and default disabled until configured/enabled through existing owner settings. Infrastructure runs in Implementation; specialist Security review runs after QA and before delivery approval, including when code review is deferred until the PR. Rejection uses actual findings to route only to selected responsible developers, including Infrastructure, and repeats required verification. Unnecessary planning is not repeated.

The shared role-consumer pool defaults to two model operations, configurable at `AGENT_RUNTIME_MAX_CONCURRENT_TASKS` (1–4); nine roles do not imply nine concurrent workers. `ENGINEERING_CONCURRENT_RUNS` defaults to one (1–4 supported) and permits distinct project/run workflows only. Existing one-active-run-per-project checks, per-run executor workspaces, run leases, operation IDs and fences remain authoritative. One run's dependent stages and shared workspace are sequential. Tests explicitly hold two independent Backend tasks and inspect their persisted overlapping claims; no UI concurrency is inferred from role availability.

## Contracts and certification

Versioned task/handoff, implementation proposal, executor test report, review, targeted rework, clarification and owner approval-request messages coexist with original operational records. Implementations remain `ProposedComplete` until verified. Model-authored summaries/findings remain unverified transport. Passing V2 test reports resolve actual scoped receipts at the exact revision and require exit 0 plus the executor's nonzero executed-test count. An apparently successful zero-count receipt blocks before QA/delivery. Compile-only success does not certify a feature.

The orchestrator retains all tool execution and all source/test/QA/review/human-delivery gates. No source-code generation, runtime budget or executor isolation policy moves into the API or UI. Old runs keep their persisted runtime generation and original checkpoint/receipt identities. No existing customer execution is resumed, cancelled or migrated.

## Safe task history and UI

Owner-only `GET /api/v1/runs/{id}/agent-tasks?limit=25&before={taskId}` returns bounded safe operational projections, never the queued request, raw context, system instructions or authentication material. It exposes task identity, assigned role/stage/objective, source revision, attempt and persisted schedule/start/result timestamps, a bounded redacted model-authored summary and safe failure category. Missing historical timestamps/revisions remain absent. A matching completed runtime checkpoint repairs the known queue-completion gap; failed/expired runtime checkpoints become Failed/OutcomeUnknown without another provider call.

Pagination uses exact UTC ticks and task ID for stable descending order and a scope-checked cursor. No browser renders the whole history. Additive scope/tick index fields are derived from historical task JSON in bounded batches; the original Data payload, identity, permissions, result and budget remain unchanged. The response is protected by existing owner authentication and request limits.

The compact execution workspace adds a collapsible worker-task table, 25 rows per page in a 320px viewport, follow/pause controls, older/latest navigation and expandable identity/revision/timestamp details. Model summaries are labeled explicitly; the panel directs users to operational history for actual commands, streams, receipts and verification. Legacy executions have an honest empty state. Existing SignalR invalidation and a bounded latest-page poll refresh the view; paused follow and historical pages avoid live invalidation. The graph preserves persisted directed transitions and shows Infrastructure only when selected, with compact specialist spacing. Security maps to the review phase; exact agent state is available in task history. No artificial stage output or concurrency is drawn.

## Security, migration and rollback

Reviewed: role/recipient authority, disabled-by-default additional capabilities, exact source/receipt matching, nonzero test evidence, current role/run ownership, immutable checkpoints, retained uncertain charges, same-project concurrency rejection, read-only security tools, safe history projection/redaction, bounded cursor scopes/rendering and no browser session material. Existing inspection guard thresholds and resume blocking remain unchanged. API-only containers do not host orchestrators/role consumers; only the executor retains its Docker trust boundary.

Deploy compatible API and worker together, leaving V2 opt-in explicit. Configure new role models/prices and enable them only if needed; select task options through the UI. Increase concurrency only for independent project/run workspaces within owner resource capacity. No tool or spending limits are increased by this setting. Reuse M1 volumes/keys and M3 context/runtime collections.

Rollback disables new-run V2 opt-in and additional specialist task options; keep compatible workers for existing V2 runs or explicitly pause them. Retain all queue/runtime/message/context/index data, receipts and source. Do not resume V2 records on a legacy binary that ignores their generation or new roles. No unknown operation may be retried merely because a worker restarted. The existing global operational relay still relies on persisted-history replay/polling; this milestone does not claim a cross-process exactly-once notification guarantee.
