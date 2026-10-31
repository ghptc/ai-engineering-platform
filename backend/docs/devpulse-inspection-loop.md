# DevPulse inspection loop and receipt retention conflict

Investigation of execution `b09188be6e3743c797092d1e73ededd6`. Production MongoDB, executor receipts and repository files were read only. No resume, limit extension, configuration change, daemon restart, delivery approval or DevPulse PR was performed.

## Identity and exact 409 cause

The operation ID supplied in the incident request differs from the actual persisted ID. The final model checkpoint `af9837987ac0e9c2c3a06639e825b708b973ff9df764200bcb45720a28cae9f0`, tool index 2, derives:

`2ce2768e0c6c22238653e9d17f3c7b8f5606756b460c98e1b88de306ed74f57d`

The persisted blocked event records `ListFiles`, HTTP 409, argument hash `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a` (`{}`), attempt version/fence **426**, owner `a65ae4cb032541f19b47eaf7170de519`. The checkpoint contains `ListFiles {}` after successful ReconcileRepository and InspectDiff requests. The last start is `2026-10-30T23:45:12.4226577Z`.

The reconstructed fingerprint input, using the executor's default PascalCase serializer and the surrounding successful request bounds, is:

```json
{"RunId":"b09188be6e3743c797092d1e73ededd6","Repository":{"Owner":"ghptc","Repository":"devpulse","InstallationId":176070757,"DefaultBranch":"main"},"Operation":"ListFiles","Arguments":{},"TimeoutSeconds":120,"CpuLimit":1,"MemoryMb":1024,"OutputLimitBytes":100000,"NetworkEnabled":false}
```

The failed HTTP request body/fingerprint was not separately persisted; its exact tool arguments are preserved in the model checkpoint. SHA-256 of the reconstructed request: `2e81718c6b5d2b0ad1274b5504b8d209f656a7dfc8ea149a7cf26e61bead0f83`. This matches the last successful same-argument ListFiles receipt, operation `1b94a2b0fd601978da9e7909995dccd82621ffc0d326c0f0a69fe339f9418ad1`. The rejected operation has **no receipt**, so there is no saved fingerprint mismatch for that ID. Operation ID, owner and fence are intentionally excluded from this fingerprint.

The workspace contains **202 top-level JSON files: state.json and 201 durable operation receipts**. LocalExecutionService first checks fencing, saves the new owner/fence/time, checks for a matching existing receipt, then rejects a *new* operation when the JSON count is greater than 201. It has not yet accessed the repository or dispatched ListFiles. The saved state has fence 426, the exact attempted owner and `lastUsedAt=2026-10-30T23:45:12.4261404Z`, establishing that fencing passed and state was saved before rejection. No competing operation or receipt collision is needed to explain the rejection. The old middleware maps this InvalidOperationException to a generic 409 and the remote adapter treats unspecified 409 outcomes as unknown.

This is the **per-workspace operation receipt retention limit**, not the 20-workspace creation limit (18 run directories were present), source byte capacity, stale fencing, different owner at the same fence, repository identity mismatch, or a changed workspace revision. Existing receipt replay precedes this guard and remains available at the cap.

The canonical revision is `da8c06196bd139ecfaf7d3661e218fff157a63dfe5405bfc2883c14acb3b9389`; baseline `78ed296a2960f8a48d50231ae1fbd5dfd6fd3806da75079e63b6f35d910b3fec`; base/HEAD `0c6c5415864bc0f9ccb56959477b98d2d9f4fb97`. Selected branch, tested revision, committed revision and delivery commit are null. The run is Paused, resume stage Implementing, iteration 1, version 427, 242 tools, cost USD 3.413342, 1,385,889 recorded tokens. Mongo's authoritative lease fields are released (`Owner=null`, epoch expiry); the serialized run retains its historical owner/expiry. Those stale serialized fields do not indicate a live lease. The run policy remains 300 tools, 10 iterations, 90 minutes, USD 10. A prior user-approved extension is historical evidence, not part of this correction.

## Last 100 actual tool attempts

The [evidence manifest](evidence/devpulse-b09188-inspection.json) contains all 100 actual starts, the last 100 completed tool ledger entries, operation IDs, outcomes, argument/output hashes, fences, owners, revisions and every original receipt/state/source file hash. Checkpoint replay is not counted as a new physical invocation.

| Operation | Last 100 starts | Outcomes |
| --- | ---: | --- |
| ReadRepository | 12 | 12 succeeded |
| ListFiles | 8 | 7 succeeded; final request blocked with 409 |
| ReadFile | 51 | 51 succeeded |
| InspectDiff | 12 | 12 succeeded |
| ReconcileRepository | 12 | 12 succeeded |
| ReadProjectMemory | 5 | 5 succeeded |
| WriteFile / ApplyPatch | 0 / 0 | No implementation |

All **94 successful repository inspections** in that attempt window repeat an earlier identical operation/argument/output/revision tuple. The final rejected ListFiles repeats earlier arguments but returns no new output. Thus 95 of 100 attempts are repeated repository inspection requests. Using the last 100 *completed ledger entries* instead gives eight successful ListFiles, 95 redundant successful inspections and five memory reads; this window begins at `23:43:17.1736551Z` and excludes the final blocked attempt. All 83 workspace-bearing results in that completed window share the canonical revision; the other 17 are remote reconciliation/memory results without a revision.

Across the whole run, six WriteFile operations succeeded (five at `23:39:26Z`, AuthorizedPathPolicy at `23:40:54Z`); no ApplyPatch, RunCommand or RunTests ran. The ledger contains 120 successful ReadFile, one failed ReadFile, 29 ReadRepository, 28 ReconcileRepository, 27 InspectDiff, 19 ListFiles and 12 memory reads. The earlier ReadFile 400 was returned as corrective feedback and did not terminate the run. Two blocked executor events exist: an earlier ReadRepository rejection and the final ListFiles rejection. The final retention diagnosis must not be applied retroactively to the earlier rejection without its historical state.

## Why inspection continued

The observable loop is confirmed; a model's private decision process is not recoverable from summaries. Backend Engineer produced 61 persisted model checkpoints and 232 tool ledger entries, with repeated instructions to inspect before completing the API/Git inspector. It did transition to writing six files, then returned to inspection without implementation or executable verification.

Proven orchestration contributors:

1. `ToolAsync` treated every tool outside five local inspections as cache-invalidating. ReconcileRepository and ReadProjectMemory therefore cleared previously reusable local file/repository/diff results even though neither changes the local workspace. Only one InspectionReused event exists despite extensive identical evidence. This explains repeated real executor calls and receipt growth.
2. There was no bounded inspection-progress feedback or stop. The generic 80% capacity notice only began near 240/300 calls. Model requests with ToolCalls keep the same specialist active; the iteration counts workflow rework cycles, not individual model/tool turns. Iteration 1/10 therefore does not bound these inspections.
3. ContextBudget always keeps only the latest eight tool-evidence entries plus up to four failures, even below the model limit. InspectDiff's 9,377-byte output exceeds the 8,000-byte threshold and is replaced by a 2,000-character preview. There are 22 persisted diff-compaction events. Earlier source/result bodies can be omitted, but originals remain durable and retrievable. The five final memory reads retrieve ranges 0/4000 of two previous diff records and offset 2000 of another, each wrapping an 11,428-character serialized ToolResult; none establishes complete retrieval of that diff. Fresh receipt IDs also make identical diffs appear as different durable references.
4. Checkpoint recovery replays earlier model checkpoints and reconstructs their tool-evidence context without invoking or charging saved tools again. There are 170 ContextBudgetEvaluated and 167 handoff events versus 67 unique model task/message records, across six tool lease owners. This proves replay occurred, not that every repeated intention was checkpoint replay: the last repeated inspections also have fresh task and operation IDs. Recovery is an amplifier and must not reset a progress detector.

The agent repeatedly received identical repository/file/diff evidence, but complete provider request bodies were not persisted, so byte-identical full model inputs cannot be claimed. The currently configured Backend Engineer is instruction revision 6, model gpt-6.1-sol, with instruction SHA-256 `caa49dc8d6239af11314c2c83cc30b64f43baaf2301284b07e7ef1748c99a7de`. Its instructions explicitly require inspection followed by implementation and allow missing test infrastructure to remain a delivery blocker. They do not require restarting the same inspection cycle. Current catalog preview-consumption guidance and provider protocol also support retrieving bounded originals. Per-request instruction snapshots were not persisted, so the current definition alone cannot prove every historical request used identical instructions. No evidence proves that prompt wording alone caused the loop, that tool success was interpreted as failure, or that context pressure was the sole cause. Recent context upper bounds remain below 96,000 input allowance (last evaluation 75,305 plus separately reserved framing/instructions). The proven corrections target cache invalidation and the missing bounded feedback, without speculative prompt or context-window changes.

## M01 acceptance versus actual diff

The workspace adds six files alongside the unchanged README: an ASP.NET Web project, offline NuGet.Config, DTO contracts, RepositoryService and interfaces, in-memory registry, and AuthorizedPathPolicy. The source diff is real: six added files, 186 inserted lines, no deletions. A read-only Git diff and independent source-revision hash confirm the persisted diff and canonical revision; it is retained intact. Source was inspected as data; none of this generated code was executed on the workstation.

| M01 criterion | Actual source/evidence |
| --- | --- |
| Register an authorized repository | Partial service/path policy and registry; no HTTP endpoint or composition root |
| Read latest 20 Git commits via REST | Inspector interface and DTOs only; no controlled Git implementation, endpoint or enforced 20-commit retrieval |
| React/Ant Design commits page | No frontend files |
| Backend unit, API integration, frontend component tests | No test files/projects/manifests |
| Isolated tests and QA evidence | No RunTests or RunCommand evidence; advertised tests unavailable |
| Review actual changes | Backend inspection exists; no CodeReviewer/QA stage acceptance |
| Verified feature commit, draft PR, M02 context | No delivery commit, push, PR or completed next-context evidence |

The Web project has no entry point. A successful build is not claimed. No live external service success is inferred from deterministic regression tests.

## Corrections and safe recovery

Local observations now survive remote read/memory/artifact operations; mutations and branch selection still invalidate them. ReconcileRepository itself remains a real remote query, and SelectBranch still independently checks the current remote head. The progress detector rebuilds from persisted task/tool records scoped to specialist, task and iteration. It counts inspection-only turns whose request signatures were already seen, including cache hits; provides feedback after six repeated turns; and blocks before a fresh model request after twelve. These defaults are configurable through `InspectionProgress__FeedbackAfterRepeatedTurns` and `InspectionProgress__StopAfterRepeatedTurns`, with startup validation requiring positive feedback followed by a stop no later than 1,000 repeated turns. Existing runtime configuration was not changed. Novel ranges are not repeats and do not erase prior repeats. Changed observed output/source revision and implementation/verification checkpoints reset the count. It runs only during Implementing/GeneratingTests; it never manufactures edits, changes budgets or authorizes delivery. Resume replays existing checkpoints but cannot erase the accumulated block.

The retention rejection now uses bounded protocol code `operation_retention_limit`, HTTP 409 and a known non-executed outcome. No receipt cap or run/tool limit is raised, no receipt is deleted, and replay/fencing order is preserved.

Recovery procedure:

1. Keep this execution paused. Preserve Mongo records and all executor files; verify the manifest hashes before any maintenance. Do not delete receipts, reset Git, reassign operation IDs, change fences manually, or copy another run's receipts into this run.
2. Review the platform correction PR and deploy the application and separate daemon through owner-managed procedures when approved. Deployment is not included in this investigation.
3. The current workspace has exhausted its receipt ledger. The correction deliberately does **not** provide new capacity or a receipt migration. Repeated resume cannot recover this workspace, even though the rejected operation was read-only. Continuing this exact run requires a separately reviewed durable-receipt storage migration preserving every operation ID/fingerprint/response and run accounting; do not raise limits or prune receipts as an incident workaround.
4. Alternatively, after explicit human authorization, preserve this run as forensic evidence and prepare a separately scoped recovery task from a reviewed source archive/branch containing these six files. Reinspect its baseline, reconcile existing remote work, state the remaining M01 criteria, and explicitly account for the original spent budget; a new task must not silently reset this run's spending or masquerade as its continuation. No such task or branch is created here.
5. Before verification, the owner must approve real fixed profiles matching the actual test targets and an offline image with the required dependencies. Create missing executable tests through authorized tools, then obtain fresh isolated test, QA and actual diff-review evidence at the current revision. Human delivery approval remains required before DevPulse delivery; never merge automatically.

Regression verification and limitations are recorded in [verification.md](verification.md). The original execution remains incomplete and blocked independently of the platform correction's test results.
