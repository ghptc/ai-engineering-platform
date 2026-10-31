# Engineering Execution observability

The execution page is an operational workspace: a sticky run/control strip, compact diagnostics, a directed lifecycle map and bounded execution history. It uses the existing React, TypeScript, Ant Design and SignalR stack. SVG keeps the graph keyboard accessible without a new graph dependency. No production backend contract, workflow, executor, permission, budget or persistence behavior changed. The only backend edit seeds isolated browser fixtures in the test-only host.

## Evidence and lifecycle rules

The graph displays Planning (business analysis), Architecture (including context preparation), Implementation, Build, Tests (authoring and execution), QA, Code Review, Human Approval, PR Preparation, Delivery, and Control / Recovery. Backend and Frontend Engineers are individually selectable **inside** Implementation. Selected specialists are not labelled as concurrently running. The current backend processes them sequentially; no parallel edges or concurrent execution claims are introduced.

Edges come exclusively from `StageChanged` event payloads, the run's `agentTransitions`, and its responsible-engineer `reworkPlan`. Arrow titles and path records retain source labels, timestamps and available iteration attribution. QA rejection returns to the recorded responsible specialist; rework gets a dashed amber edge. Pause/failure/resume paths use a distinct control/recovery node. Older attributed iterations are dimmed and can be selected independently. Stage events with no iteration attribution are shown under All iterations rather than assigned a guessed iteration. At most 60 paths render; original events remain available in history.

Stage placement is a lifecycle map, not proof of execution. Unobserved nodes say **Not observed**. A past handoff is labelled Handoff recorded rather than verified completion. Tool states retain their recorded task/iteration association; older iterations are labelled explicitly. Completed delivery displays **Completed · run state**, which does not claim a PR exists for a verified no-op. Actual artifact details remain the source of delivery evidence.

The backend requires owner approval **before** commit/push/PR operations. Consequently the graph places Human Approval before PR Preparation, rather than inventing the opposite order. Approval controls remain the existing server-authorized workflow. Build has no standalone backend run stage: its node changes only on an actual `RunCommand` with profile `build`. No synthetic Build transition is drawn. The current and historical paths can therefore skip or omit nodes when that is what persisted state proves.

## Terminal and history

The workspace shows the selected task objective and a separately labelled **Model-authored progress summary** from the latest task/iteration handoff. It never displays private reasoning or turns requested tools into executed tools.

The operational terminal has Compact, Timeline and Detailed views. Compact groups consecutive records by attributed agent, stage and iteration, with an independent fold for each exact operation identity. Timeline uses actual start/end timestamps for command duration; missing measurements stay unmeasured. Adjacent, matching StageChanged boundaries establish stage/wait intervals. A wait crossing a page boundary has no invented end time. Recorded failed-stage restarts and paused-stage resumes are labelled Retry and Resumed. Detailed shows original sequence, record/operation identity, attribution and retry markers.

Sources stay distinct: Tool executions, System events, Progress summaries and Evidence artifacts. Successful TestReport artifacts require a successful persisted result, evidence ID and source revision before receiving the **Verified evidence** label; this does not substitute for QA or human delivery approval. Tool results retain their separate execution label, diagnostics stay diagnostic, and arbitrary artifacts are not automatically verified.

Opening a tool fold calls the existing details API. The UI shows safe bounded arguments, the actual recorded command/working directory when present, exit status, separate stdout/stderr when recorded, evidence ID and source revision. Arguments permit path/profile/query/branch/commit/reference/range fields, cap string values, replace source/patch bodies with their lengths, and omit unknown values. Output renders escaped text with simple JSON-key/diff/error colouring. ANSI/control sequences are removed; no HTML or shell executes. Output pages contain at most 8,000 source characters and copy only the displayed source page. Full original evidence stays behind the existing drawer. Older records with missing arguments or streams say so explicitly.

Each server window contains at most 50 records and remains in ascending persisted sequence order. Search and status/recovery filters apply to that page, as the toolbar states. Older/newer cursors do not accumulate an unbounded DOM. A single result fold can be open; its full output is never rendered all at once. Artifact/source bodies are not expanded in list rows. Empty sources and unmatched filters have separate honest empty states.

Follow mode scrolls on new identities; pause-follow or inspection freezes the visible snapshot. Cursor pagination disables following. Jump to latest releases the snapshot. Existing SignalR batching/replay updates event windows; persisted tool completions/blocks also invalidate the latest tool window. Historical cursor windows are not refetched by that completion path. Existing reconnect reconciliation and polling fallback remain intact.

## Layout and accessibility

The graph supports Fit to view, bounded zoom, pointer pan, native scrolling and keyboard-selectable nodes. Fit accounts for both viewport dimensions. Graph and terminal heights are adjustable; pipeline and operation/path panels collapse independently. Diagnostics fold without concealing the paused/failed status. Dense rows use text labels in addition to status colour. Ant Design theme tokens control SVG backgrounds, text and state colours in both themes. Terminal colours maintain contrast on its fixed dark surface. Focus outlines, names, expansion state, stream selectors and follow state remain accessible. The compact header is sticky on desktop and returns to document flow on mobile. The 390px regression asserts no document-level horizontal overflow.

## Existing data gaps and smallest optional contract improvements

No API change was needed for this delivery. The interface deliberately exposes these limits:

- `ToolInvocationStarted` does not consistently carry an operation ID or attributed agent. A start is displayed as its own system record; it is not joined to an unrelated completion by matching tool name. The smallest future improvement is adding `operationId` and `agent` metadata to the existing start/completion events. Existing readers would ignore the extra keys.
- Stream queries support one kind and a cursor, without cross-kind search. A future compatible addition could accept optional `kinds` and `q` parameters, returning the same sequence-ordered RecordWindow; it would enable whole-run search and unified grouping without client-side full-history downloads. This was not implemented or simulated.
- Some historical transitions lack task/iteration/attempt attribution. Add those metadata keys prospectively to transition events if exact historical attempt navigation is needed. Existing records cannot be backfilled reliably from their summaries. The UI shows recorded retries and aggregate run counts without inventing attempt ordinals.
- Build is an operation within an existing stage; absent command/stream timestamps cannot become a new lifecycle stage or a measured interval. No change to workflow semantics is proposed for visual symmetry.

## Fixtures, screenshots and verification

`tests/fixtures/devpulse-persisted.json` is a sanitized, read-only excerpt of actual execution `b09188be6e3743c797092d1e73ededd6`, linked to the existing incident report. Original identities, outcomes, timestamps and sequence ordering remain in the fixture. It contains no provider credentials, model tool-plan payloads or repository source bodies. The original production execution was not resumed, queried through its control API or altered for screenshots.

The test-only host loads that excerpt into its separate test database. MongoDB assigns new global sequence numbers in the same relative order; the UI always displays the sequence supplied by its API. Its deliberately omitted task records mean arguments cannot be recovered for that excerpt, and the terminal shows the honest missing-input state. The normal deterministic lifecycle browser test separately exercises recovered WriteFile results and the existing human approval boundary. A distinct deterministic contract fixture uses domain retry/rework transitions and separate project/idempotency identity for QA-to-frontend paths and measured waits. It is labelled as a fixture and is not represented as live model or external service evidence.

Review images:

- [Persisted incident, dark workspace](../../backend/docs/ux-evidence/execution-observability-dark.png)
- [Bounded tool fold](../../backend/docs/ux-evidence/execution-observability-tool.png)
- [Responsible-engineer rework](../../backend/docs/ux-evidence/execution-observability-rework.png)
- [Retry and waiting history](../../backend/docs/ux-evidence/execution-observability-history.png)
- [390px responsive workspace](../../backend/docs/ux-evidence/execution-observability-mobile.png)

Commands, final counts and source-manifest evidence are recorded in [the verification report](../../backend/docs/verification.md). Browser evidence uses only isolated test MongoDB and dedicated, non-reused ports. No live OpenAI/GitHub success, deployment or merge is claimed.
