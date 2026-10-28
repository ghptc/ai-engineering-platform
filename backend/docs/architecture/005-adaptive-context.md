# ADR 005: Adaptive execution context

Status: Accepted for implementation; external live-agent behavior requires opt-in verification.

## Problem and investigation

The previous `ContextBuilder.BuildAsync` rejected serialized JSON exceeding **160,000 .NET UTF-16 code units**. The agent loop separately rejected **240,000 code units**. These were application guards, not OpenAI model limits. The builder embedded the original project context, up to 30 full artifacts, up to 100 requirements and 20 recent messages. PreparingContext persisted that whole envelope as DevelopmentContext; subsequent stages embedded that snapshot again. NextContext repeated the same pattern. Structured outputs were also stored as both messages and artifacts. Tool outputs, prior agent outputs (including nextContext and generated tool arguments), review diffs and PR evidence accumulated in the agent loop, sometimes twice.

Read-only inspection of local failed execution `be670ca71a2449dbba1ebefea60d00db` confirmed this failure at Implementing (iteration 1). Its source inventory was:

| Source | Raw content characters |
| --- | ---: |
| Original project context | 602 |
| Five requirement records | 3,861 |
| Nine conversation messages | 51,978 |
| Requirements artifact | 6,787 |
| ArchitectureDecision artifact | 6,007 |
| DevelopmentContext artifact | 87,991 |

The artifact bodies alone total 100,785 characters; together with messages, requirements and original context the raw strings total 157,226 **before** JSON escaping, metadata and framing. The 87,991-character DevelopmentContext already contained those earlier messages/requirements/decisions. Its ClientMessages component alone was at least 57,775 JSON characters (JavaScript serialization lower bound; .NET escaping adds more). This proves the recursive artifact plus repeated conversation content caused the 160,000 guard to fire; the 602-character original project description was not the main consumer. No source bodies, credentials or execution records were changed, deleted or retried by this investigation. New ContextSourceInventory telemetry reports counts, original character/token bounds by artifact type and the ten largest artifact IDs/hashes. ContextInventory reports the actual serialized input components.

## Decision

Agent configuration stores the model's `ModelContextTokens` and the agent's `MaxInputTokens`, alongside its existing `MaxOutputTokens`. Defaults are 128000/96000/4000; operators must set the actual window for the selected model. Settings and configuration API expose both limits and persist instruction revisions. Different agents using the same model may use different input budgets.

Input allowance is `min(MaxInputTokens, ModelContextTokens - MaxOutputTokens)`. Sizing uses UTF-8 bytes as a conservative upper bound on byte-level BPE tokens, rather than an unsafe characters/token ratio. This is deliberately **not an exact tokenizer count** and can underutilize the model window. It works without downloading an encoding or calling another model. The workflow additionally reserves 12000 tokens for protocol/schema and 1024 for framing; the provider independently checks the actual instructions and output schema plus framing immediately before sending a request. Output capacity and monetary reservation remain explicit.

Context is assembled on demand. Original project instructions, active/selected requirements and acceptance criteria, policy, architecture decisions, human messages and structured unresolved questions remain protected. Requirements explicitly selected by a run survive verification/supersession filtering. Agent conversation copies identical to artifacts are deduplicated. Structured summaries retain acceptance criteria, proposals, decisions, questions, assumptions, findings, QA checks and evidence, excluding echoed nextContext and generated tool arguments. Completed execution artifacts use summaries/references; current execution artifacts stay available. Legacy DevelopmentContext and NextContext snapshots contribute inventory metadata, never recursive content. New DevelopmentContext artifacts contain source references.

When over budget, completed history becomes a retrievable reference; earlier tool output bodies become references while retaining success/failure, evidence and revision. The last two tool results retain previews. Oversized individual tool results use bounded previews, explicitly marked incomplete. Full originals remain persisted. ReadProjectMemory lists paginated references and retrieves project-scoped artifact/tool/task/message/requirement ranges, with IDs, status, SHA-256, offset and total size. Repository files are requested with ListFiles, SearchCode and ReadFile rather than preloaded into the project envelope. Large ReadFile results can be paged from their persisted tool record.

Compaction never promotes repository text, tool results or historical summaries to system instructions, changes authorization or weakens approval/version/lease/idempotency/fencing checks. A reference or preview is not passing test evidence. ToolResult summaries preserve actual evidence IDs and workspace revisions. If protected content still exceeds the budget, fail explicitly without truncating it. The operator must increase the configured model/input capacity or explicitly scope the task; safety limits remain enforced.

## Telemetry and verification

ContextBudgetEvaluated records role/model, conservative before/after size, input limit, reserved output and whether request preparation compacted context. ContextCompacted records oversized tool source IDs and strategy. Inventory and events contain sizes/references, not source bodies or credentials, and use existing persist-before-publish delivery.

Regression coverage includes oversized legacy snapshots across stages and both complete approval workflows, Unicode sizing, output reservations, preserved human instructions and requirements, explicit protected overflow, long tool loops, tool failure/evidence preservation and test-report revision linkage. Production model execution and production delivery remain separate opt-in verification boundaries.
