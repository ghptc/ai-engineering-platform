# DevPulse ReadFile failure — 2026-10-30

## Persisted evidence

Run `8c0ff19bd60f4b3a98eb01b79ed2543e` (prefix `8c0ff19bd60f`), project `96657ac37b2a4eb39968cff7e714d37a`, repository `ghptc/devpulse`, installation `176070757`, default branch `main`. Read-only MongoDB inspection found Paused, resume stage Implementing, iteration 1, version 85, no active store lease. BA and Architect completed. 47 tool receipts comprise BA 4, Architect 6 and Backend 37; 47/300 is the execution-wide count. Ten Backend model requests were persisted. Recorded cost $1.008264, tokens 324605, budget $10, duration 60 minutes from 2026-10-30T23:01:33.350Z.

The final Backend task is `524fccc450af52f52b23b1c00fa32a7dba05b2c79b57433bddcb52ff21e1e90e`, response `resp_0b3a4e7ea26bdee5016ae522e67e3887d281d1cf4a9a7a5380`, persisted at 23:06:17.819Z. Its zero-based call 4 is exactly:

```json
{"Name":"ReadFile","Arguments":{"path":"src/DevPulse.Api/Infrastructure/GitRepositoryInspector.cs"}}
```

SHA-256 of its compact argument JSON is `f9a20372ac9c8ca25ca05f38c3359fd8f6e1fd63bfc2858098a363243c478ef0`, matching the failure event. SHA-256 of `<run>/Implementing/1/<task>/tool/4` is operation `7d5aeda6d0e0f294ec6fb2a240ebaa6d9ee183fb575a717266f8d75f9c5be5db`.

Event `executor-failure-7d5aeda6d0e0f294ec6fb2a240ebaa6d9ee183fb575a717266f8d75f9c5be5db-84`, sequence 3551 at 23:06:19.931Z, records InvalidArguments, HTTP 400, outcomeUnknown False, reason Unspecified, attemptVersion 84. The daemon does not preserve the underlying FileNotFoundException text in this response, so no historical exception message is claimed.

Immediately preceding ListFiles receipt `fdfe7893c086c98bce3cd928e8416741b13a96c6be82372e8590d540385bcd6d` lists README.md and eight added backend files, excluding GitRepositoryInspector.cs and Program.cs. Previous call 3 read AuthorizedPathPolicy.cs successfully (receipt `2323a99da11a40dc94870c28a5341c60fb0497b5872334ffeafe9088c5f95741`). Call 5 requests Program.cs, which is also absent and was not reached. No daemon receipt exists for the rejected operation.

## Verified cause and contracts

This is a nonexistent file, not malformed JSON, schema disagreement, traversal, secret filtering or a symlink rejection. The path is a valid relative source path with existing normal ancestors. The model-facing EngineeringAgents instructions and OpenAIProvider authoritative protocol both specify ReadFile {path}; the strict output schema carries arguments as a JSON-encoded object, parsed into the persisted object. EngineeringTool accepts that shape. LocalExecutionService requires a string path, resolves it with WorkspaceFiles.Resolve, then accesses FileInfo.Length before reading. For this absent target that access throws FileNotFoundException, mapped to HTTP 400 by Program.cs. The error's historical reason remains Unspecified; the missing-file diagnosis is established by the exact arguments, pre-failure inventory, unchanged workspace and source contract, rather than a preserved exception trace.

The local workspace is `backend/.local-runtime/state/runs/<run>/repository`. HEAD/base is `0c6c5415864bc0f9ccb56959477b98d2d9f4fb97`, branch engineering/<run>. Eight additions remain: Application/{Ports,RepositoryService}.cs, Contracts/RepositoryDtos.cs, DevPulse.Api.csproj, Domain/RegisteredRepository.cs, Infrastructure/{AuthorizedPathPolicy,InMemoryRepositoryRegistry,RepositoryOptions}.cs beneath src/DevPulse.Api/. Its independently computed revision is `b51b7416f101f9bae612e18fccce4111fc81026a56d752e3ae41395eb94ccc7f`, matching the last successful receipts. Daemon state retains fence 84 and null tested/committed revisions and commit. Reconciliation returned no feature branches or PRs. No generated source was executed during this investigation. Existing profiles advertise a build command but only missing-infrastructure placeholders for tests; successful implementation verification remains outstanding.

## Smallest platform correction

The model is told tool failures are corrective feedback, but all executor exceptions previously escaped the loop and paused before a tool result was persisted. EngineeringTool now converts only matching-operation ReadFile InvalidArguments HTTP 400 with outcomeUnknown=false into a failed result containing locally authored guidance. The normal workflow persists it, charges usage and adds it to context. No remote detail, fabricated evidence or workspace revision is returned. Authentication, paths, sandbox limits, allowlists and all mutations retain existing checks. All other failures still propagate. This fixes recovery without changing the daemon contract or interpreting every 400 as a missing file.

## Safe recovery (requires explicit owner action)

1. Review and deploy the application fix through the normal delivery process. The running owner application and daemon were not replaced by this investigation.
2. Keep this run paused until ready. Recheck state/receipt revision and elapsed-duration policy. Paused time counts toward StartedAt: after the 60-minute limit, an explicit supported limit extension is required; do not reset StartedAt, budgets or counters. The maximum supported duration is 240 minutes, so a run older than that cannot be resumed under the current policy; do not bypass that guard.
3. Explicitly resume the existing run using its normal UI/API only after deployment and policy checks. The workflow loads persisted model tasks and tool results; completed calls/writes are returned from their existing records, not re-executed. The unreceipted read may be attempted again with the same arguments and fresh fence, then persisted as a failed tool result. The next absent Program.cs read can likewise produce feedback. A subsequent model turn can ListFiles and issue a new valid ReadFile with a new operation ID. Missing planned implementation must be written and verified rather than assumed present.
4. Never edit a persisted task, change arguments under an operation ID, insert synthetic successful receipts, delete failure events or reset checkpoints. Do not create empty placeholder files to trick the old read into succeeding. Existing model/tool budgets and approval gates remain in force. No automatic resume or delivery approval was performed.

## Validation

Solution restore and build passed, zero warnings/errors. Full backend suite: 258 passed, 8 opt-in skipped, using isolated MongoDB localhost:27029 with nofile=65536. Focused 8-case regression passed, including persisted failure accounting and new corrective read without replayed writes. An initial full-suite attempt was interrupted after the separate test MongoDB on port 27028 crashed from too many open files; its container/data were preserved. No owner service was stopped. Real Docker workloads and live model/GitHub execution remain unverified for this change. Source diff reviewed and whitespace checks passed.
