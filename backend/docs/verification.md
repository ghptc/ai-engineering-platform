# Verification report — 2026-10-28

The implementation was resumed from preserved uncommitted files after the host restart. No reset, source deletion, or repository recreation was used. The recovery audit found only the initial commit. Milestone history is available through `git log`.

## Executed commands and final results

Commands were executed from the repository root unless stated otherwise. Final results are summarized below. Raw command outputs remain local in the ignored `verification-evidence/` directory; generated logs and test artifacts are intentionally excluded from source control.

| Command | Final result |
| --- | --- |
| `git status --short`, `git log -5 --oneline` | Inspected initial commit and preserved uncommitted source |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed; all projects up to date |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed; zero warnings, zero errors |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore --logger 'console;verbosity=minimal'` | Passed: 45 tests; 14 Domain, 6 Application, 2 Architecture, 23 Integration; none skipped |
| `npm ci --prefix frontend` | Passed from the lockfile; 0 vulnerabilities reported |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | Passed: 7 Vitest/React Testing Library tests |
| `npm run build --prefix frontend` | Passed; lazy Monaco bundle produces a size warning |
| `npm run e2e --prefix frontend` | Passed: full Playwright browser workflow; final clean-install run 1 passed in 14.7 seconds |
| `npm audit --prefix frontend --json` | Zero known dependency vulnerabilities |
| `dotnet list backend/AIEngineeringPlatform.sln package --vulnerable --include-transitive` | No known vulnerable packages reported for any project |
| `docker build -t ai-engineering-platform:local .` | Passed; final image `sha256:c6ada59f5e964953e90918648a4c613b6395877f0b14e669b2c799dc8e203c8f` |
| `python3 /tmp/aep/container-smoke.py` | Passed: 13 deployed-container checks, listed below |
| `docker exec aep-app-smoke id -u` | UID 1654 (non-root) |
| `docker exec aep-app-smoke sh -c 'if command -v node; then exit 1; else echo "Node runtime absent"; fi'` | Node runtime absent |
| `docker inspect aep-app-smoke-final --format '{{.Config.User}} {{.State.Health.Status}} {{.Image}}'` | `app healthy` with the final image identity above |
| `git diff --check` | Passed |

The initial smoke container used the same runtime base/user as the final image. The final rebuilt image was separately started as `aep-app-smoke-final` and all 13 HTTP checks repeated successfully. Temporary verification containers were stopped after verification; their MongoDB data and application source were retained.

## What the tests establish

MongoDB tests use a real separate MongoDB 8 container, unique test databases, the official driver, indexes, serialization, optimistic versions, duplicate prevention, lease fencing, atomic transition outbox recovery, conversation persistence, approval decisions and PR webhook deduplication. Both review-before-commit and review-after-PR paths run through the complete workflow with deterministic external adapters.

Provider contract tests exercise the actual official OpenAI Responses SDK against a fake HTTP transport, including strict output schemas and usage parsing. GitHub HTTP contract tests exercise RSA App JWT generation, repository-scoped installation tokens, PR creation and review deduplication. Sandbox contract tests inspect bounded requests and fail-closed configuration. These tests do not perform live external operations.

The Playwright browser journey signs in, creates a project with initial context, connects a simulated repository, persists a client conversation, starts a run, observes stages/messages, inspects artifacts and review records, approves delivery, reaches completion and confirms history after refresh. Its AI, sandbox and GitHub adapters are explicitly test-only fixtures. Production source contains no selectable fake integration mode.

Deployed-container checks verify liveness, MongoDB readiness, SPA root and deep links, anonymous API rejection, unknown API 404 behavior, owner authentication with Secure/HttpOnly cookies, authenticated project creation/read against MongoDB, disabled unconfigured sandbox, aggregate dashboard statistics, authenticated OpenAPI and SignalR negotiation. The smoke harness used an ephemeral local test credential, not a production owner secret.

## Resolved failures and remaining verification boundary

Earlier failures exposed outdated/vulnerable package versions, a Monaco worker export change, test-host content-root configuration, artifact/tool ID collisions, brittle browser locators and an event replay request storm. They were fixed before the passing results above. Vitest emits jsdom getComputedStyle pseudo-element notices; the production build emits a lazy-editor chunk-size warning. Neither is a failing check.

Live OpenAI model execution, GitHub App installation access, real branch/commit/push/PR/review operations and execution isolation on a production remote sandbox remain unverified because no live credentials or executor deployment were supplied. The production adapters and explicit configuration exist, but successful fake calls are not evidence of live service behavior. The external executor must implement and enforce [the sandbox contract](sandbox-contract.md). Without it the platform rejects execution instead of running untrusted code locally.

The complete production Definition of Done remains contingent on those external prerequisites and opt-in verification. The platform does not automatically merge or launch unlimited additional runs. The next development context is persisted for the next explicit cycle. See README for actual capabilities, deployment commands and operational limits.

## Adaptive context management — 2026-10-28

Verified the feature-branch source/test delta with SHA-256 `df4dd1e4a1b922c54b89e5f9285e6549074bb9f5aa83666b7d1b5d6082257957` (`git diff --cached --binary -- backend/src backend/tests frontend/src | sha256sum` before the milestone commit). No application source changed after these final checks; subsequent edits only record this evidence.

| Check | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors on final build |
| `dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1` | 63 passed; 5 opt-in tests skipped: 14 Application, 14 Domain, 2 Architecture, 25 Integration, 8 LocalExecutor passing |
| `npm ci --prefix frontend` | Passed, zero reported vulnerabilities |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 8 passed |
| `npm run build --prefix frontend` | Passed; existing large-chunk warning remains |
| `npm run e2e --prefix frontend` | 1 passed on dedicated non-reused test ports; latest run 9.7 seconds |
| `docker build -t ai-engineering-platform:local .` | Passed; image config `sha256:2bed89df8a6191eb314e0cf7592c976a1e1c359ec8da9e142d764d42943b45fa` |
| `git diff --cached --check` | Passed |

New regression tests verify Unicode token bounds, output reservations, explicit protected overflow, long tool loops, original requirement/human-instruction preservation across four stages with oversized legacy snapshots, bounded project-scoped memory retrieval, provider rejection before transport, and test evidence/revision preservation. Both complete workflow approval paths now start with 200000-character legacy snapshots and completed-history artifacts, pass implementation/QA/review, await human approval and complete with deterministic external adapters backed by isolated MongoDB.

Read-only inspection identified the actual matching local failed run and artifact sizes; [ADR 005](architecture/005-adaptive-context.md) records that inventory and the exact old guards. No production run, project, message, approval or artifact was changed or retried. The running owner application and its MongoDB remained intact. The isolated MongoDB test container exited during initial verification; restarting only that test container restored the passing runs. An intermediate regression exposed the deterministic fixture's dependence on full repeated TestReport content; summaries now preserve evidence/revision and the fixture resolves the explicit required-test reference.

Sizing deliberately uses a conservative byte-level BPE token upper bound, not exact model tokenization. Live OpenAI/GitHub App engineering execution and opt-in real Docker executor tests were not run for this change (2 live-agent and 3 executor tests skipped). Docker image build success and the feature-branch Git/PR delivery are separate from those external integration boundaries. No deployment or automatic retry of the preserved failed run is claimed.

Diff review checked protected-content retention, original-reference/project isolation, model preflight/output reservation, persist-before-publish telemetry, unchanged approval/lease/fence/idempotency gates, source layout and absence of secrets/generated build artifacts in the staged change.
