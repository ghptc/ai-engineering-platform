# ADR 012: Bounded runtime invocations and versioned engineering messages

Status: M2 implementation under verification. Baseline is merged M1, `057a40f`. This milestone adds V2 alongside the existing engineering workflow; it does not silently migrate running executions or claim independent role workers before M3.

## Runtime boundary

Application owns `IAgentRuntime`, input/output contracts, context references, message validation and evidence resolution. Infrastructure provides `OpenAIResponsesAgentRuntime` over the existing official Responses SDK provider and a Mongo invocation checkpoint store. Agent output validation is shared with the legacy role adapter. Domain remains independent of runtime SDKs and infrastructure.

Every invocation binds project, run, task, role, instruction revision, objective, acceptance criteria, allowed tool subset, context SHA-256 and bounded model/tool/input/output/time/cost allowances. The runtime cannot execute arbitrary tools: it returns proposals to the orchestrator's existing authorized gateway. A completed invocation means a model operation completed, not that implementation or delivery is certified.

Task context is stored as an immutable bounded frame and retrieved by scoped reference. The runtime never rebuilds a complete conversation. Existing protected-instruction/context budgeting remains authoritative; source evidence continues to use bounded previews and original references. No authentication/session material is included in frames.

The Responses provider enforces per-invocation model-request and cumulative cost allowances before reservation. Global model overrides and adaptive recovery cannot raise the invocation's output ceiling. Existing execution budgets, provider charge records, strict output schemas and legacy recovery behavior remain in place.

## Communication contract

Version 1 supports exactly `EngineeringTask`, `EngineeringHandoff`, `ImplementationResult`, `TestReport`, `ReviewResult`, `ReworkRequest`, `ClarificationRequest` and `ApprovalRequest`. Envelopes identify message/project/run/task/sender/recipient, source revision, objective, acceptance criteria, artifact/evidence references and bounded metadata. Each kind has a closed typed payload. Owner-authenticated schema discovery is available at `/api/v1/runtime/contracts/{kind}`.

Parsing rejects unknown fields, missing required fields, numeric/coerced enums and numbers, duplicate keys, noncanonical message/role values, incompatible versions, oversized/deep input and unauthorized sender/recipient combinations. Only the orchestrator assigns tasks and requests owner approval. A model-generated approval request cannot grant approval or schedule a worker.

Valid messages persist under `agentMessageV2`, with unverified proposal status. Persistence does not turn text into evidence. `RuntimeEvidenceResolver` reads actual tool records, checks execution/project/operation/revision identity, rejects duplicate evidence and requires matching nonzero executed test counts for passing test reports. Actual tool outcomes remain authoritative; model counts and summaries cannot override them. Implementation results explicitly say `ProposedComplete`.

## Checkpoints, recovery and cancellation

Mongo collection `runtime_invocations_v2` atomically claims an invocation identity before a provider call. Identical concurrent/repeated dispatches wait for or reuse its completed result. A changed identity/fingerprint cannot overwrite the invocation. Context hashes are checked on retrieval. Completed results can be read after budget exhaustion without charging another request.

Cancellation, provider ambiguity or an expired in-flight deadline produce an unknown outcome that requires reconciliation; the runtime does not automatically reissue it. Provider receipts/reservations remain the budget authority. Definite invalid outputs are rejected and checkpointed as failures. Explicit retries must have a new bounded attempt identity and retain the original checkpoint/charges. No database transaction spans model or sandbox calls.

## Codex candidate

Official documentation verifies Codex SDK and noninteractive interfaces with API-key automation: https://developers.openai.com/codex/sdk and https://developers.openai.com/codex/noninteractive . This does not verify a safe replacement for AEP's idempotent, fenced tool gateway. No Codex CLI embedding, borrowed ChatGPT authentication, host shell or unverified runtime capability is introduced. Responses is the first supported implementation; live certification must exercise the selected real runtime after worker migration.

## Migration and rollback

All collections/record kinds and optional AgentContext fields are additive. Existing runs still use the legacy adapter in M2. Disable/remove V2 registrations to roll back without rewriting history, receipts, budgets or source. Keep context/checkpoint/message records for reconciliation. A rollback must not reinterpret an unknown invocation as permission to repeat a call. M3 will migrate Backend/Test scheduling first; M4 will migrate remaining roles and expose actual persisted worker state.
