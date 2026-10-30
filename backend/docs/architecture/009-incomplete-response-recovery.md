# ADR 009: Bounded incomplete-response recovery

Status: Accepted for implementation; human PR review pending.

## Evidence and decision

Read-only inspection uniquely resolved run prefix `170acc642ef3` to `170acc642ef34f25abe49f5cae4852f7` in the local persisted store. Provider result `72ba1c702a9e43258f8daecb8baf56bd-result` reports `incomplete`, reason `max_output_tokens`, model `gpt-6.1-sol`, input usage 9,755 and output usage 4,000. The preceding persisted ContextBudgetEvaluated event reserved 4,000 output tokens, and the Architect definition has MaxOutputTokens 4,000, instruction revision 4, configured context window 128,000. The historical provider diagnostic did not itself contain the configured allowance; the preceding event establishes it. This is evidence of exhausted output allowance, rather than an assumption based on an incomplete status.

Classify the actual reason as OutputLimit, ContentFilter, Steered or Unknown. Never infer truncation from status alone. New reservations/results persist the requested maximum output allowance. Unknown reason values remain sanitized; no response text, partial JSON or exception body is recorded in diagnostics.

Retry only the current model request when the reason is `max_output_tokens` and reported output usage reaches the requested allowance. Double that allowance, bounded by configured recovery ceiling, conservative remaining model context and remaining monetary budget after all settled/uncertain charges. Reserve the entire next request before sending it. Default to one output recovery, at most two if configured, also bounded by the run's MaxRetries. Keep the original linked request timeout and run deadline across attempts. No persistence store means no automatic retry. Missing usage or other incomplete reasons do not automatically retry.

An incomplete response is rejected before output parsing, schema validation or construction of application tool instructions. Partial output is neither appended to the next input nor treated as valid evidence. Successful responses still require complete message envelopes, schema-valid output, valid tool arguments and usage. The existing workflow persists successful task and tool checkpoints, uses deterministic operation IDs and remote fencing, and reuses them on explicit retry of the failed stage. Recovery adds no tool execution path and cannot replay side effects directly.

Use concise structured output: intermediate tool requests should defer final planning arrays; avoid repeating the same plan in summary, next context and ADRs. Existing context compaction preserves protected requirements and retrieves original artifacts/evidence by persisted ID. No source data is deleted or rewritten.

## Configuration and operation

The existing agent definition MaxOutputTokens remains the default. Optional administrator settings under LanguageModel:

```json
{
  "MaxOutputRecoveryRetries": 1,
  "MaxRecoveryOutputTokens": 16000,
  "AgentOutputTokens": { "Architect": 8000 }
}
```

AgentOutputTokens overrides the provider's initial allowance for the named role, subject to final context validation and full cost reservation. An 8,000-token Architect allowance is justified for multi-ADR/bootstrap/dependency/test plans by this run's observed 4,000-token exhaustion; it is an opt-in configuration example, not a migration of existing definitions. Model window and prices remain administrator-configured; this change makes no claim about an external model's actual published capacity.

Provider Diagnostics displays the reason, classification, requested allowance, usage and actionable recovery guidance. Historical records retain their stored fields and receive client-side reason-based guidance without migration. Use the existing Retry action for a failed Architect checkpoint, not a new run: completed BA artifacts and valid operations remain intact. If duration, budget or retry capacity is exhausted, explicitly extend limits first; automatic recovery does not extend them. To increase allowance after bounded recovery fails, configure the role allowance or agent definition, or scope a smaller focused task.

The investigated run has not been retried, deployed, modified or delivered by this investigation. Its original deadline is now elapsed; an owner-authorized duration extension is necessary before meaningful live retry. All existing human delivery decisions and sandbox isolation remain mandatory.

## References and verification

[Official OpenAI reasoning documentation](https://developers.openai.com/api/docs/guides/reasoning) explains that `max_output_tokens` includes reasoning/output and can also indicate context-window exhaustion. Both reason and available context are therefore checked.

See the current incomplete-response section of [verification.md](../verification.md) for contract, checkpoint, frontend and build evidence, including live verification limitations.
