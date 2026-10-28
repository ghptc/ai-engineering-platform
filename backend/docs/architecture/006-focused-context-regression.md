# ADR 006: Focused Architect input instead of protected transcripts

## Evidence

Read-only MongoDB inspection of failed execution `15c637b82b814be282a96d4056de9d82` found the persisted Planning inventory:

| Component | Conservative serialized token upper bound |
| --- | ---: |
| ConversationDecisions | 43,568 |
| Decisions | 19,455 |
| Requirements | 9,515 |
| LargestArtifacts | 1,709 |
| HumanInstructions | 1,190 |
| OriginalContext | 614 |

The last successful Architect preflight measured 77,832 context bytes. The next tool turn failed at 98,239 input-token upper bound including 1,479 bytes of system instructions, 12,000 protocol/schema allowance and 1,024 framing allowance. Thus that failed turn's context body was 83,736 bytes, not 98,239 actual model tokens. The estimator remains UTF-8 byte count, deliberately pessimistic for byte-level BPE; it does not measure repository size or actual tokenizer usage.

The first fix prevented recursive DevelopmentContext bodies, but still treated every intermediate historical agent response as protected ConversationDecisions, preserving repeated 3.7–4.1 KB requirement proposals on every turn. Decisions also included Requirements artifacts, repeating the same backlog beside selected requirement records. Removing only History could never fix this because History was already empty. References were JSON strings serialized inside JSON, adding escaped quotes.

## Change

Selected requirement records are the single source for full requirements and acceptance criteria. Unselected backlog is not sent when explicit selection exists; missing selected records fail closed. Architecture artifacts project only their structured architectural decisions, deduplicated by decision content. Latest final analysis for the current execution retains additional acceptance criteria, open questions and assumptions without repeated requirement proposals or free-form transcript summaries. Historical intermediate proposals, transcripts and tool output are retrieved through ReadProjectMemory rather than injected. All original project context, human instructions and run safety policies remain protected. Human instruction copies with identical sender/content are deduplicated, with the original source reference retained.

Planning receives no historical QA/code reports or tool bodies. Downstream validation/review keeps current-run evidence summaries and their actual evidence IDs/revisions. Source references are structured JSON rather than escaped JSON strings. Source inventory and original artifact records remain traceable and unchanged.

The preflight compaction trigger now includes framing, consistent with final validation. Budget telemetry separates context-body estimates, instruction estimates and reserved protocol/framing space; failures record ContextBudgetExceeded before rethrowing. Existing model/input/output settings, financial budgets and persisted runs are not modified.

Official [GPT-6.1 Sol API documentation](https://developers.openai.com/api/docs/models/gpt-6.1-sol), checked 2026-10-28, lists a 1,050,000-token model window and 128,000 maximum output. The platform's existing 128,000 configured window / 96,000 input / 4,000 output defaults are stricter application limits; this fix does not increase them. Operators may configure stricter per-agent limits within the actual model window. Exact tokenizer integration remains a future improvement; no undercounting ratio is introduced.

Regression coverage exercises a tiny JavaScript answer fixture with 500 long historical agent messages, repeated proposals, unrelated backlog and a 25,000-token input allowance. Protected instructions and selected acceptance criteria remain present while old transcripts/proposals are absent. Complete approval-policy workflows verify progress through implementation and QA with deterministic adapters, independently of live external integration evidence.
