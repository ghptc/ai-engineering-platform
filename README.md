# AI Engineering Platform

A single-owner engineering orchestration platform built with .NET 10, MongoDB, React, TypeScript and Ant Design. A client supplies product context and focused tasks. Seven specialized agents are available; straightforward tasks use a Developer, Test Engineer, QA and Reviewer, with BA/Architect and frontend expertise selected when needed. A human approves delivery. The platform records execution history, conversations, usage, artifacts and decisions.

**Local execution requires configured OpenAI access, a GitHub App and the included host Docker executor daemon; no cloud executor is required.** The application never executes repository code in its own container. The included deterministic AI, sandbox and source-control adapters live only in test assemblies and do not verify real external services.

## Architecture

Clean Architecture and logical bounded contexts form a modular monolith. Domain owns invariants and the execution state machine. Application owns use cases, context building and interfaces. Infrastructure implements MongoDB repositories, the official OpenAI Responses SDK, GitHub App authentication/REST and authenticated Unix/HTTPS sandbox transports. A separate host daemon implements secure local Docker execution. Agents specialize structured outputs, instructions and tool permissions. Orchestration coordinates stages; Worker hosts the durable scheduler; API composes everything and serves the compiled SPA.

```text
backend/
  AIEngineeringPlatform.sln
  src/
    AIEngineeringPlatform.Domain/
    AIEngineeringPlatform.Application/
    AIEngineeringPlatform.Infrastructure/
    AIEngineeringPlatform.Agents/
    AIEngineeringPlatform.Orchestration/
    AIEngineeringPlatform.Worker/
    AIEngineeringPlatform.Api/
    AIEngineeringPlatform.LocalExecutor/
  tests/
    AIEngineeringPlatform.Domain.Tests/
    AIEngineeringPlatform.Application.Tests/
    AIEngineeringPlatform.Integration.Tests/
    AIEngineeringPlatform.Architecture.Tests/
    AIEngineeringPlatform.E2EHost/
    AIEngineeringPlatform.LocalExecutor.Tests/
  docs/architecture/
  scripts/owner-credential.py
  scripts/local-runtime.sh
  executor/
frontend/
  src/{app,components,features,hooks,services,styles,types}/
  tests/{platform.test.tsx,e2e}/
Dockerfile
.env.example
AGENTS.md
```

Dependencies point inward. MongoDB stores projects/runs separately and indexed, versioned operational envelopes for messages, requirements, tasks, events, reviews, approvals and artifacts. Run changes use optimistic concurrency, expiring worker leases and ownership fences. State changes and outbox records commit atomically in the same run document; persisted sequence-numbered events support replay. No transactions span model or executor calls.

See [architecture decisions](backend/docs/architecture/), [API contracts](backend/docs/api.md), [sandbox contract](backend/docs/sandbox-contract.md), and [current verification evidence](backend/docs/milestone-8-verification.md).

## Features and execution policy

The UI provides dashboard statistics, project editing and repository connection, requirements, client conversations, saved agent collaboration, run timelines and controls, Monaco artifact/diff inspection, code reviews, human approvals, per-agent configuration and dark mode. Operational data comes from persisted backend responses.

The legacy project-planning workflow is `Pending → Analyzing → Planning → PreparingContext → Implementing → GeneratingTests → RunningTests → Validating → Reviewing → AwaitingHumanApproval → PreparingDelivery → Completed`. Focused tasks skip unnecessary analysis/planning stages, keep project knowledge separate from current task and execution evidence, and retrieve older context on demand. An authorized queue progresses to the next task automatically after verified, approved delivery, sharing the original cumulative budgets. Each delivery still requires human approval. Failed tests, QA or review return to implementation within the configured iteration limit. Failed stages retain details and checkpointed outputs; Retry resumes that stage within the retry limit. Pause/resume/cancel are explicit state transitions. Crashed workers lose their leases and another worker can resume.

Actual test evidence must include a successful exit code and the tested workspace revision. QA maps selected requirements to that evidence. Delivery rejects a changed source snapshot. A successful run creates commit/PR artifacts, records verified requirements and builds next context from persisted evidence. A generated source summary alone cannot complete a run.

`ReviewBeforeCommit=true` reviews local changes, requests human delivery approval, then prepares a commit and draft PR and records the local review as a GitHub comment review. With `false`, QA is followed by human approval to prepare the commit/draft PR, then the reviewer reads the existing PR and publishes its decision. GitHub errors, including self-review restrictions, remain failures. Delivery approval never counts as independent GitHub approval. Automatic merge is disabled; the human merges through GitHub. The next context is persisted; a new run remains explicit to preserve human control and per-run budgets.

## Local workstation runtime (Milestone 8)

The included .NET host daemon executes fixed, owner-approved profiles in ephemeral Docker containers. The application connects over an authenticated Unix socket; it never receives Docker access. Workloads have no host mounts, network, credentials, or owner-home access, and run non-root with CPU/memory/PID/time/output/tmpfs limits. Canonical source changes use validated repository tools; tested snapshots gate commits and pushes to dedicated feature branches.

Start with [the local runtime guide](backend/docs/local-runtime.md), which covers `.env`, the explicit repository/App allowlist, offline dependency images, startup scripts, systemd, real Docker tests and the optional spending-limited live OpenAI/GitHub cycle. Typical commands after configuration are:

```sh
backend/scripts/local-runtime.sh build
backend/scripts/local-runtime.sh configure --repository YOUR-OWNER/aep-sandbox-disposable --installation-id YOUR-INSTALLATION-ID
backend/scripts/local-runtime.sh daemon
# In another terminal:
backend/scripts/local-runtime.sh up
```

Open `http://localhost:8080`, configure per-agent models/prices in Settings, connect the same authorized repository, and start a bounded execution. Iteration, budget, tool-call, retry and duration limits are visible in the execution view. The platform's own protected default branch is never a live verification target.

## Local development

Prerequisites: .NET 10 SDK, Node.js 24 or later with npm, MongoDB 8 or compatible, and optional Docker for image/persistence tests. MongoDB runs separately:

```sh
docker run -d --name aep-mongo -p 127.0.0.1:27017:27017 \
  -v aep-mongo-data:/data/db mongo:8.0
```

The example local database is loopback-only and unauthenticated. Use TLS, authenticated database credentials and least-privilege access for production MongoDB. Do not publish it to an untrusted network.

Copy `.env.example` to `.env` and populate it. Generate a PBKDF2-SHA256 owner credential hash interactively:

```sh
python3 backend/scripts/owner-credential.py
```

Set its output as `APPLICATION_OWNER_CREDENTIAL_SECRET`. The hash format is `iterations:base64(salt):base64(hash)` with at least 600000 iterations. The password is never hardcoded or stored in MongoDB. Owner mode is the implemented authentication mode; unsupported modes fail configuration validation.

Export configuration into your shell (the app does not automatically load .env):

```sh
set -a
. ./.env
set +a
export ASPNETCORE_ENVIRONMENT=Development
export MONGODB_CONNECTION_STRING=mongodb://localhost:27017
export ASPNETCORE_URLS=http://localhost:8080
dotnet restore backend/AIEngineeringPlatform.sln
dotnet run --project backend/src/AIEngineeringPlatform.Api
```

In another terminal:

```sh
npm ci --prefix frontend
npm run dev --prefix frontend
```

Open the Vite URL and sign in with the original owner credential. Vite proxies `/api` and `/hubs` to port 8080. Same-origin cookies and `X-Requested-With` protect REST mutations. Production uses Secure cookies and requires HTTPS; Development permits loopback HTTP. The API refuses startup without MongoDB configuration and a valid owner hash. Missing optional integrations appear in Settings and fail closed when invoked.

## Environment and integrations

| Variable | Purpose |
| --- | --- |
| `MONGODB_CONNECTION_STRING`, `MONGODB_DATABASE` | Separate persistent database |
| `APPLICATION_AUTH_MODE=Owner` | Implemented single-owner authorization model |
| `APPLICATION_OWNER_CREDENTIAL_SECRET` | PBKDF2 credential hash from the script |
| `OPENAI_API_KEY`, `OPENAI_DEFAULT_MODEL` | Server-only provider credential and initial agent model |
| `GITHUB_APP_ID`, `GITHUB_APP_PRIVATE_KEY_PATH` | App identifier and mounted PEM path |
| `GITHUB_WEBHOOK_SECRET` | HMAC-SHA256 webhook verification secret |
| `EXECUTION_SANDBOX_PROVIDER` | `LocalUnix` for the included daemon, or optional `Remote` HTTPS transport |
| `EXECUTION_SANDBOX_SOCKET_PATH` | Local Unix socket path visible inside the application container |
| `EXECUTION_SANDBOX_ENDPOINT`, `EXECUTION_SANDBOX_TOKEN` | HTTPS control-plane endpoint and secret |
| `EXECUTION_MAX_ITERATIONS`, `EXECUTION_MAX_TOOL_CALLS` | Default project execution limits |
| `EXECUTION_MAX_DURATION_MINUTES`, `EXECUTION_MAX_BUDGET_USD` | Default duration and budget |
| `LanguageModel__TimeoutSeconds`, `LanguageModel__MaxRetries` | Provider timeout and bounded transient retries |
| `ASPNETCORE_HTTP_PORTS` | Application listening port, default image port 8080 |

Configure each agent in Settings with a model available to your OpenAI project, positive current input/output prices per million tokens, output-token limit, instructions and enabled state. Initial model selection uses `OPENAI_DEFAULT_MODEL`; editing persisted definitions is explicit. Instruction revisions are retained. The provider uses the official OpenAI .NET SDK Responses API with strict JSON schemas and bounded structured tool requests. Operational state lives in MongoDB, not provider conversation history. The model does not get host tools. Model names are not hardcoded into business logic.

Usage is an estimate based on configured prices. Configure an account-level spending limit as well: interruption or timeout can occur after the provider bills a request but before its result is checkpointed. Context, output and tool limits bound normal execution; they are not a billing guarantee.

Create a GitHub App with repository Contents read/write, Pull requests read/write and Metadata read access; install it only on authorized repositories. Mount its private PEM read-only and configure the installation ID when connecting a repository in the UI. Tokens are short-lived and scoped to that repository. Configure the webhook endpoint `/api/v1/webhooks/github` and the same HMAC secret in GitHub. Deliveries are authenticated and deduplicated. Webhook receipts are retained and processed idempotently. Pull-request events update tracked state only for the matching repository and App installation; they do not authorize mutations. No automatic merge is performed.

Use the included local daemon described in [local runtime](backend/docs/local-runtime.md), or provision a remote executor implementing [the contract](backend/docs/sandbox-contract.md). It must enforce workload isolation, filesystem/symlink safety, network policy, CPU/memory/time/output limits, operation idempotency and fencing, and perform controlled Git CLI transfers outside untrusted builds. Private-repository access must be brokered in its trusted transfer plane. The platform sends bounded requests and validates responses; it cannot prove an arbitrary remote server enforces isolation. Local executor provisioning is included; an HTTPS remote executor remains an optional alternative. Never mount a Docker socket or application credentials into build workloads.

## Single application container

```sh
docker build -t ai-engineering-platform:local .
docker run -d --name ai-engineering-platform --env-file .env \
  -p 8080:8080 \
  -v /absolute/path/github-app.pem:/run/secrets/github-app.pem:ro \
  ai-engineering-platform:local
```

Set `GITHUB_APP_PRIVATE_KEY_PATH=/run/secrets/github-app.pem` and ensure the non-root application user can read the mounted file. Set MongoDB and sandbox addresses reachable from the container; `localhost` inside the container refers to the platform itself. Omit the PEM mount until GitHub is configured. Deploy behind an HTTPS reverse proxy on the same origin; port 8080 is the internal HTTP listener.

The multi-stage build compiles React with Node, restores/publishes .NET, and copies both into one ASP.NET Core runtime image. Node is absent at runtime; the final process runs as `app`. `/health/live` is the container health check and `/health/ready` checks MongoDB. One application container does not include MongoDB or sandbox infrastructure. Session data-protection keys are ephemeral unless operators configure persistent key storage; container replacement signs out existing sessions.

## Tests

```sh
docker run -d --name aep-mongo-test -p 127.0.0.1:27028:27017 mongo:8.0
dotnet restore backend/AIEngineeringPlatform.sln
dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1
dotnet test backend/AIEngineeringPlatform.sln --no-restore
npm ci --prefix frontend
npm run typecheck --prefix frontend
npm test --prefix frontend
npm run build --prefix frontend
cd frontend
npx playwright install chromium
npm run e2e
```

Use `TEST_MONGODB_CONNECTION_STRING` to point integration tests at a separate disposable server. Tests use uniquely named databases; do not point them at a production deployment. The E2E host uses dedicated Kestrel/Vite ports 15088/15173 by default, without reusing owner development servers, real MongoDB, a test-only owner credential and deterministic external adapters. Chromium covers login, project creation/context, repository connection, saved client messages, stage progression, agent collaboration, artifacts, review, approval and refresh persistence. Simulated tests/PRs are clearly labeled fixtures, not claimed live operations.

## Current limits

Live OpenAI and GitHub installation access still require owner credentials. The local Docker backend has real integration tests; these are separate from a live-agent success. See the verification report for what was actually run. Repository sandbox workloads and controlled Git CLI transfers are performed by the included host daemon over a Unix socket, or optionally by an external executor. Pull-request webhooks update tracked state; unsupported event types are retained and marked ignored. Large histories are paged; the UI currently loads bounded pages. Scheduling runs one stage at a time per worker. Each run freezes its project/repository configuration; connect the repository before starting, and start a new run for repository changes. Context building is bounded, and exceptionally large tasks must be split. Artifacts are text/JSON/diff records, not large binary blob storage. No automatic merge or unlimited next-cycle execution is enabled.

Milestone 8 architecture and verification: [ADR 004](backend/docs/architecture/004-local-docker-executor.md), [local runtime verification](backend/docs/milestone-8-verification.md).
