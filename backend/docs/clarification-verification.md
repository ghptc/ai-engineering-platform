# Clarification fix verification — 2026-10-28

## Diagnosis confirmed against the local runtime

Read-only MongoDB inspection confirmed the reported five-requirement, zero-open-question CONTINUE response belonged to a project conversation and had no RunId. The worker's earlier CONTINUE responses requested repository tools; its final response was BLOCKED with two questions. The run remained Failed at the Analyzing checkpoint with two retries consumed. No production records were modified.

Project conversation replies were never execution transitions. ContextBuilder did reload recent project messages, but the worker reused task IDs based only on run/stage/iteration/role/turn, replaying its saved BLOCKED result instead of reconsidering the added authorization. BLOCKED was treated as a failure rather than a waiting checkpoint. See [ADR 005](architecture/005-execution-clarification.md).

## Verification performed

| Command | Result |
| --- | --- |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors |
| `RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:1b0fb53953d8f1281b750c9e76e668d717a00189932af92a4bd00135c5390cbf dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1` | 62 passed, 3 opt-in live tests skipped |
| `dotnet test backend/tests/AIEngineeringPlatform.Integration.Tests --no-restore --filter FullyQualifiedName~ClarificationTests` | Final targeted rerun: 4 passed, 1 live test skipped; includes additional budget rejection assertion |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 9 passed; jsdom CSS notices |
| `npm run build --prefix frontend` | Passed; existing Monaco chunk-size warning |
| `npm run e2e --prefix frontend` | 1 passed; deterministic engineering adapters and real isolated MongoDB |
| `docker build -t ai-engineering-platform:clarification-fix .` | Passed |
| `python3 backend/scripts/smoke-local-container.py --image ai-engineering-platform:clarification-fix` | Passed: isolated app container, separate MongoDB, authenticated Unix executor IPC, no Docker socket mount |
| `git diff --check` | Passed |

The first targeted run exposed a test assertion comparing record-contained arrays by reference. Comparing their serialized persisted values corrected the assertion; subsequent tests passed. No product behavior was weakened to accommodate the test.

Deterministic integration tests exercise: BA BLOCKED, conversation-only CONTINUE with five requirements, unchanged waiting execution, explicit run-bound owner response, concurrent/duplicate/conflicting responses, no new execution, final BA CONTINUE, five persisted requirements and a Requirements artifact, Planning transition, architect clarification, no re-execution of the completed analysis stage, application restart while waiting, fresh client messages on retry, invalid requirement rejection, persisted history/outbox events, authentication, legacy recovery with exhausted retries, and budget exhaustion on response. Domain tests preserve stage/retry/iteration/cost/deadline invariants. The UI test submits only to the displayed run/question and exposes no generic resume while input is pending.

## Opt-in real OpenAI result

With credentials loaded privately into the child environment from the ignored owner `.env`, executed:

```sh
RUN_LIVE_AGENT_TESTS=1 LIVE_MODEL_INPUT_PRICE=2 LIVE_MODEL_OUTPUT_PRICE=10 \
  dotnet test backend/tests/AIEngineeringPlatform.Integration.Tests --no-restore \
  --filter RealOpenAIClarificationResumesAnalysisWithoutRepositoryOperations \
  --logger 'console;verbosity=detailed'
```

Passed. Real BA Responses IDs:

- `resp_0de64b531fb747f7016ae21864d71087d2af2384fcb15dd379` — BLOCKED.
- `resp_0ef844f234e56005016ae2186af0a487d287a25ec9b57473c3` — CONTINUE after the persisted explicit owner response.

Actual usage: 2404 input / 758 output tokens; estimated cost **USD 0.012388** under the run's USD 0.25 ceiling. The test used real MongoDB, API authentication, the background worker and OpenAI BA calls. An explicit test-only architect checkpoint stopped further progress; repository tools were not invoked. This proves live clarification/resume, not a full live engineering cycle, real generated code/test execution, commits or GitHub PR delivery. The existing Docker suite separately executes actual workloads against disposable local fixtures.

## Existing-run recovery and deployment

No running owner container was replaced and no production run was automatically migrated or answered. After review/merge and rebuilding/restarting the application, an eligible legacy clarification failure offers **Recover clarification**, followed by a run-specific answer. This requires a saved BLOCKED task matching the checkpoint and preserves retry counters and all limits.

The observed production run has already exceeded its original **60-minute wall-clock deadline** (read-only inspection showed over eight hours elapsed). Its budget was not exhausted, but response/resume must still reject the expired duration. Do not reset timestamps, increase budgets or clear retry counts in MongoDB. Cancel that expired run and explicitly start a new bounded execution after deployment; saved conversation context remains available. Runs still within their limits can recover and resume the original checkpoint without starting another execution.

Temporary logs remained under `/tmp`; credentials, environment files, binaries, build outputs and test artifacts are excluded from Git. The feature branch is submitted by PR to protected master, with no merge or protection bypass.
