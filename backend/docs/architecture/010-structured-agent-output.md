# ADR 010: One validated structured decision per Responses request

Status: Implemented; human PR review pending.

## Persisted investigation

The prefix `f3eddefcbce2` uniquely matches run `f3eddefcbce24d5e8cf5494ca77a8224`. Its BackendEngineer provider request reservation `0d47865b7039411689f72725aa360588` and result `0d47865b7039411689f72725aa360588-result` share that requestKey. The result and separately persisted ProviderRequestFailed event correlate:

| Field | Persisted value |
| --- | --- |
| Model | `gpt-6.1-sol` |
| Request ID | `req_9d9fcff64cfc4a7ab58974494c29de7e` |
| Response ID | `resp_08db747b2677b673016ae4193ef32087d1bf3091c1d1b8ec12` |
| HTTP / response status | 200 / completed |
| Input / output / requested maximum | 12,488 / 529 / 4,000 tokens |
| Error / attempt | MalformedJson / 1 |
| Request reservation / settled charge | $0.143024 / $0.030266 |

The run failed in implementation, with its implementation resume checkpoint retained. Its earlier two BackendEngineer requests completed successfully. No tool instructions from the failed response were accepted. This is not evidence of output-token exhaustion.

The historical diagnostics did not retain output bytes, item/message counts, requested schema hash, parsing stage or JSON error coordinates. No claim is made about the exact malformed bytes, prose, code fences, partial tool arguments or multiple messages in that response. The old category could arise while reading the response envelope or parsing the SDK-aggregated text; DTO/schema validation has a separate InvalidSchema category. The persisted evidence therefore cannot conclusively distinguish model text syntax from extraction ambiguity. It does not establish a schema mismatch.

## Findings and decision

1. The production provider already calls the official .NET Responses SDK with `ResponseTextFormat.CreateJsonSchemaFormat(..., jsonSchemaIsStrict: true)`. Wire contract tests now explicitly assert `text.format.type=json_schema` and `strict=true`. Historical diagnostics alone do not prove the exact historical request body because it was not retained.
2. The SDK's GetOutputText selects output_text from message content, but concatenates it across messages. A deterministic test against the installed OpenAI 2.14.0 SDK reproduces two individually valid JSON objects becoming invalid aggregated JSON. This is an identified code risk, not a proven historical root cause.
3. Replace cross-message aggregation with explicit extraction from exactly one completed assistant decision message. Respect the API phase: explicitly marked commentary is validated as text but never parsed as the decision; final_answer identifies a final candidate. Preserve compatibility with one unphased assistant message. Concatenate output_text segments only within that selected message. Allow reasoning items without reading their text as the decision. Reject multiple decision candidates (including mixed unphased/final candidates) as AmbiguousOutput rather than selecting an arbitrary first/last result; reject unknown phases and never promote commentary-only output to a decision. Reject refusals, incomplete messages, non-text parts and unrelated tool output. Never salvage an object from prose or remove fences/suffixes.
4. Preserve the explicit strict schema for decisions, summaries, criteria, evidence, assumptions, tool calls, nextContext, planning, requirements, reviews, checks, questions and ADRs. All fields are required on the wire; the adapter maps them explicitly to AgentResult and its nested DTOs. Test exact top-level DTO/schema field parity for all seven roles. Keep full recursive validation, required/unknown/duplicate properties, types, enums and numeric/array constraints before DTO acceptance. Parse all tool argument strings as bounded JSON objects, reject unauthorized names and names absent from the registered production tool catalog, and return nothing if any call fails. Existing operation-specific validation, sandbox restrictions and delivery authorization remain additional independent boundaries.
5. Official documentation lists structured outputs as supported for gpt-6.1-sol. Classify explicit HTTP 400/422 schema/format parameter rejections as StructuredOutputIncompatible; never silently fall back to JSON mode, prose or relaxed validation. Other model errors remain generic HTTP failures when the API does not provide a structured schema/format indication. Do not maintain a stale model-name allowlist.
6. Build the schema once per provider invocation, reuse the exact schema for sending and validation, and fingerprint it. No extra schema copy or rejected response is appended to context. Existing protected-context compaction, artifact retrieval and concise planning instructions remain in force; no historical data is deleted.

## Recovery and safe diagnostics

LanguageModel.MaxMalformedResponseRetries defaults to 1 and is clamped to 0–2, additionally bounded by LanguageModel.MaxRetries and run Policy.MaxRetries. Only JSON syntax failures in the selected text are eligible. Repeat the same strict request and output allowance, without appending rejected output. Before another request, include settled failed charges and uncertain reservations in remaining budget and persist a new full reservation. Retain the original request timeout, run deadline and validated context. Without a persistence store, no automatic retry occurs. Schema/tool errors, ambiguous output, refusals, extraction errors, incompatible configuration and uncertain timeouts require inspection. Output-limit recovery remains reason-specific and separate.

Diagnostics record only generated stage labels (ResponseEnvelope, OutputExtraction, JsonSyntax, SchemaValidation, ToolValidation, UsageValidation, Validated), item/message/text-part counts observed before failure, selected message phase, text character count, zero-based JSON line/byte coordinates for syntax errors, requested strict mode and SHA-256 of the schema. They never include prompts, text snippets, JSON property paths, parser exception messages, reasoning text, credentials or provider error bodies. Provider Diagnostics displays those safe fields and actionable guidance. Historical malformed records receive guidance without inventing missing bytes/stages or rewriting persisted records.

All seven EngineeringAgent roles share this provider pipeline. Existing durable task/tool IDs, accounting, lease ownership, remote fencing, checkpoint replay and human approval remain unchanged. Automatic retries repeat a model request only; an invalid response returns no executable batch. Existing explicit failed-stage retry preserves completed tools and artifacts. No production run was retried, changed or deployed during this investigation.

## Verification and references

Deterministic SDK/provider tests run first. The optional live probe requires explicit opt-in and configured positive prices/budget; it does not run merely because production credentials exist. This session found key/model configuration but no RUN_LIVE_AGENT_TESTS opt-in or LIVE_MODEL_INPUT_PRICE, LIVE_MODEL_OUTPUT_PRICE or LIVE_MAX_BUDGET_USD. Live verification is therefore skipped, not claimed. No production secrets are copied to tests or output.

See the current structured-output section of [verification.md](../verification.md) for final results and source fingerprints.

- [Official OpenAI Structured Outputs documentation](https://developers.openai.com/api/docs/guides/structured-outputs?api-mode=responses)
- [Official assistant message phase guidance](https://developers.openai.com/api/docs/guides/deployment-checklist)
- [Official gpt-6.1-sol model capabilities](https://developers.openai.com/api/docs/models/gpt-6.1-sol)
