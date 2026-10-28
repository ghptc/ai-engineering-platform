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
