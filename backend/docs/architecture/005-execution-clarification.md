# ADR 005: Run-scoped human clarification checkpoints

Status: Accepted

## Finding

The project conversation endpoint creates a separate, tool-free analyst response and usage record. It has no execution RunId, does not persist an execution backlog and cannot move the execution state machine. ContextBuilder reloads recent client messages on each worker invocation while preserving the project's configuration snapshot. Previously, retrying the same stage replayed saved task results identified only by run/stage/iteration/role/turn, so fresh client messages could not change a cached BLOCKED result. BLOCKED was thrown as a failure and consumed the normal retry allowance. CONTINUE with tools is an intermediate tool-loop response; a final validated tool-free CONTINUE advances analysis to Planning.

Read-only inspection of the affected local MongoDB confirmed one failed Analyzing run with two retries consumed. Its final worker response was BLOCKED. The later five-requirement, zero-question CONTINUE response had a null RunId and belonged to the project conversation. Earlier worker CONTINUE responses requested tools and were not final stage results. Private message contents and credentials were not copied into this repository.

## Decision

Add AwaitingHumanInput as a non-runnable state, appended to the enum to preserve persisted numeric enum values. A pending request records its ID, originating stage/role, questions and timestamp on the run. BLOCKED persists the structured result as an artifact, then atomically saves the request and state transition. The Mongo outbox persists HumanInputRequested alongside the transition; replay and SignalR use the same durable delivery mechanism.

Only the authenticated owner may submit POST `/api/v1/runs/{id}/clarifications/{requestId}/response`. The request must belong to that run and be its pending request. The answer, owner identity, timestamp, context revision and resumed state are committed in one optimistic conditional update. HumanInputReceived is part of that same outbox write. An identical response replay is harmless; conflicting or stale responses are rejected. This endpoint never creates runs. Ordinary conversation messages are not interpreted as execution control or approval.

The ContextBuilder explicitly includes persisted run clarifications. Agent checkpoints include context revision and retry count, preventing a cached blocked response from hiding new input. Completed stages are retained; only the stage awaiting input is resumed. Valid final BA requirements/artifacts are validated and persisted before advancing to Planning. Clarification responses do not reset cost, usage, tool counts, iterations, retries or elapsed time. Waiting counts toward the existing wall-clock duration limit. At most ten requests are allowed per run; each owner response is bounded to 20000 characters. Cancellation remains available while waiting.

POST `/api/v1/runs/{id}/clarifications/recover` explicitly converts an old Failed clarification to AwaitingHumanInput only when the failure has the legacy clarification marker and the final persisted task is BLOCKED by the role matching ResumeStage. It preserves exhausted retry counters and all budgets. Ordinary failures cannot use this recovery path. No production records are automatically rewritten, no owner response is inferred, and no run is restarted.

## User experience and validation

The execution page shows the question, answer form and persisted clarification history. Conversation-only advice is labeled as such and cannot resume an execution. Legacy clarification failures offer Recover clarification; normal failures retain bounded Retry stage. Operational failures identify the checkpoint and retain actionable validation/configuration errors. Malformed model JSON has a role-specific error.

Deterministic integration tests cover five-requirement CONTINUE progression, conversation isolation, answer context, checkpoint preservation, concurrency/replay, persisted history/outbox, authorization, invalid output, fresh-context retries and legacy recovery with exhausted retries. An opt-in live test uses real OpenAI only for the BA clarification and CONTINUE calls, then deliberately stops at an architect test checkpoint; it does not claim code execution, GitHub operations or a complete live engineering cycle.
