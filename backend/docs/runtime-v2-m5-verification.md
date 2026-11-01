# M5 live certification: paused; not certified

## Authorized scope and observed outcome

The owner authorized only `ghptc/aep-sandbox-m8-20261028-3a0ed9`, GitHub App installation `176070757`, a new isolated feature branch, a draft PR and a maximum total OpenAI spend of USD 10. No automatic merge or automatic recovery is authorized. Codex authored only the platform harness/fix/tests, not the target application.

The production GitHub App token was requested for this repository only. GitHub confirmed contents/write, pull_requests/write, metadata/read and exactly one accessible repository. The baseline was `main@eb4462114a9c7c5f95b4db99ee50013d5ea3f94a`, tree `6abb50de578dc0dd3d1040c3c9d12da526ee4d4c`. Two existing branches and three files were inventoried before starting. This disposable repository's default branch is not protected in GitHub; no repository settings were changed. Platform delivery still targets protected master.

A fresh Compose project `aep-m5-certification` was provisioned at loopback port 18099 with independent MongoDB, signing keys, control token, executor state and a read-only App credential volume. A random private owner password replaces the publicly documented earlier review credential. The existing owner/review stacks were untouched. No secret is committed.

One V2 run was started: `24464e57c4384cb38b5771eabe24fe1c`, project `4d2d73c89b7842b18c70c3d4effcbde6`. Its task asks the seven core AEP specialists to plan, implement, test, QA and review a small temperature converter with a real .NET REST API and React/TypeScript/Ant Design UI. It contains product requirements and acceptance criteria, no application source templates. Policy: 100 tools, three iterations, 60 minutes, two retries, USD 9.50 conservative accounting ceiling, existing approval/review boundaries. Each configured role has 64,000 input/8,000 output token limits. Conservative USD 5/input-million and USD 20/output-million accounting exceeds the verified standard model prices and charges cached input in full. `gpt-6.1-sol` model metadata access and an actual successful Responses invocation are verified.

The Business Analyst's first model response requested `ReadRepository {}`. Before any executor operation completed, transport failed and the worker correctly paused the run at `Analyzing`. Recorded conservative cost is **USD 0.01924**, successful tools **0**, writes **0**, no build/test/QA/review/delivery evidence. This is a model usage accounting amount, not an independently reconciled provider invoice. The USD 10 authorization remains cumulative; no second run or paid probe was started.

Operation: `b42cee1879c6e47e415a014117687f1d8902ecb79b9fd6ec7d72e5d2c382e3cb`. Arguments are `{}`, SHA-256 `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a`. Invocation event `edc4c77d8aad4f2b8945d942d6b40960`, sequence 39, UTC `2026-10-31T21:39:58.2415814Z`; blocked event sequence 40, attempt version 4, outcomeUnknown true. These persisted records were not rewritten. No executor receipt, fingerprint, workspace revision, fence/lease record or workspace existed for this run because the request never reached the executor.

## Proven deployment defect and correction

The executor was UID/GID 0:0 with effective capabilities `0xB` (CHOWN/FOWNER/DAC_OVERRIDE). Its entrypoint chowned the IPC directory to 0:1654 and requested mode 2750. Without CAP_FSETID, root outside group 1654 could not retain the setgid bit: observed directory 0750 and socket 0660 owned 0:0. The application worker is UID/GID 1654:1654. The token was correctly group-readable; socket connection was denied before HTTP. Executor root healthchecks alone had reported healthy and did not prove application connectivity. Empty executor run storage corroborates the pre-request failure. This is not workspace capacity, stale fencing or an inspection loop.

Compose now gives the trusted executor primary identity **0:1654**. No capabilities are added. Observed directory is 2750, socket 0660 owned 0:1654. An authenticated health request from the actual non-root worker succeeds with contract version 2, Docker/cgroup-v2/seccomp isolation and network none. The executor remains the sole Docker socket trust boundary. API/worker remain non-root and have no Docker socket. No production receipt, operation identity, budget, checkpoint, agent guard or workspace was edited. Only the isolated certification executor was recreated; the run remains paused at the same cost/tool count.

## Harness behavior and security review

`backend/tools/AIEngineeringPlatform.Certification` is an owner-operated console tool using the real production owner HTTP API and production GitHub App integration. It has no target-source writer, executor mutation endpoint, automatic resume/cancel/retry or approval action. Fixed loopback API and exact repository/installation constants prevent arbitrary target expansion. Metadata and evidence responses are bounded; redirects are disabled. Metadata reports exclude tokens/credentials. Owner settings are parsed as data, never evaluated by a shell. An exclusive `start-intent.json` fences a second start, including interrupted/ambiguous HTTP outcomes. Existing executions in the isolated database reject setup. Action ambiguity, duplicate options and missing values are rejected before side effects. `--observe` is read-only and needs no API-provider settings.

Private deployment settings/password/key and raw captures remain under owner-only `/tmp/aep-m5-certification` and named volumes. Do not attach them to a PR. The checked-in sanitized manifest preserves evidence identities and hashes. No private chain-of-thought is displayed; only operational summaries/events and actual tool results are collected.

## Migration, rollback and safe recovery

1. Review this fix and its regression evidence. Deploy the Compose identity change using the existing volume set; do not delete volumes, reinitialize tokens or clean workspaces. Recreate the executor only after active operations are quiescent. The paused certification has no in-flight executor operation.
2. Verify socket mode/group and authenticated readiness **from the non-root application worker**, not just from inside the executor. Preserve failed startup/test evidence.
3. Reconcile the exact paused operation above: no receipt or run workspace exists, the request was denied before HTTP, repository/default/existing feature refs remain at the recorded baseline. Do not edit the historical blocked record to claim success. Retain the existing immutable model result/checkpoint and budgets.
4. Obtain explicit owner authorization to resume **this run only**. An owner resume through the supported UI/API can replay its persisted read-only `ReadRepository` request. Do not create another run, increase limits, directly write target source or merge. Recheck remaining cumulative budget and repository permissions first. Observe genuine source/test/QA/review evidence; stop again on an unresolved operation.
5. If successful later, require nonzero backend/frontend test receipts tied to the final revision, QA checks, actual diff review and the established owner delivery approval before a new draft PR. M5 is not accepted until this happens.

Rollback: stop/quiesce the executor, restore the previous Compose configuration/image if needed, retain every named volume and receipt. The old 0:0 setting reproduces the access defect and is not a working recovery; keep affected runs paused until a reviewed alternative IPC group configuration is installed. The harness is additive and can be omitted without changing persisted execution schemas.

## Verification

Final results are recorded in the adjacent evidence manifest. Target application implementation, application tests, QA, code review and draft PR are **not completed**. Platform regression fixtures and successful infrastructure health checks do not satisfy autonomous certification. No frontend product code changed; earlier M4 frontend/browser evidence remains separate and is not claimed as rerun for this correction.
