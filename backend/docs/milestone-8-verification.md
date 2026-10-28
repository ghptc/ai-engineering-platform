# Milestone 8 verification — 2026-10-28

## Delivered and verified

The local host daemon uses authenticated Unix-socket IPC and creates ephemeral Docker workloads. The platform remains one application image/container with separate MongoDB. The application and workloads receive no Docker socket. Owner-allowlisted repositories, fixed offline command profiles and immutable runtime image IDs define the execution boundary. Real tests exercised Node and C# assertions, repository edits/patches, actual diffs, revision-gated commits and feature-branch pushes to a disposable local bare repository. The default-branch SHA remained unchanged.

The seven configured agents use the existing structured Responses contract and bounded tool loop. Corrections receive tool failure feedback; QA receives real revision-linked test evidence and selected requirement IDs. Delivery retains the human approval gate. Published review bodies now contain findings and evidence. Settings distinguish configured credentials from actual sandbox readiness, and execution views expose cost/budget, iteration, retry, duration and tool-call limits.

## Commands and results

Commands were executed from the repository root. Private credentials were loaded into child-process environments from the ignored `.env`; no secret values were logged or committed. Temporary logs stayed under `/tmp`.

| Command | Result |
| --- | --- |
| `dotnet restore backend/AIEngineeringPlatform.sln` | Passed |
| `dotnet build backend/AIEngineeringPlatform.sln --no-restore -m:1` | Passed, zero warnings/errors |
| `RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:1b0fb53953d8f1281b750c9e76e668d717a00189932af92a4bd00135c5390cbf dotnet test backend/AIEngineeringPlatform.sln --no-restore -m:1` | 57 passed; 2 opt-in live tests skipped in this regression invocation |
| `npm ci --prefix frontend` | Passed |
| `npm run typecheck --prefix frontend` | Passed |
| `npm test --prefix frontend` | 8 passed; jsdom CSS notices |
| `npm run build --prefix frontend` | Passed; code-editor chunk size warning |
| `npm run e2e --prefix frontend` | 1 passed; real MongoDB, deterministic AI/GitHub/sandbox adapters; **not a live agent cycle** |
| `docker build -t aep-executor-runtime:local backend/executor/runtime` | Passed |
| `docker build -t ai-engineering-platform:milestone8 .` | Passed, including final rebuild |
| `python3 backend/scripts/smoke-local-container.py --image ai-engineering-platform:milestone8` | Passed against final image: real non-root app container, authenticated Unix IPC, host daemon Docker readiness, separate disposable MongoDB; no Docker socket mount |
| `RUN_LIVE_AGENT_TESTS=1 LIVE_MODEL_INPUT_PRICE=2 LIVE_MODEL_OUTPUT_PRICE=10 dotnet test backend/tests/AIEngineeringPlatform.Integration.Tests --filter RealOpenAIProducesStructuredOutputAndUsage --logger 'console;verbosity=detailed'` | Passed with real configured model/key; structured SDK response and usage |
| `npm audit --prefix frontend --omit=dev --json` | Zero reported vulnerabilities |
| `dotnet list backend/AIEngineeringPlatform.sln package --vulnerable --include-transitive` | No known vulnerable packages reported |
| `git diff --check` | Passed |

The real OpenAI probe returned response ID `resp_0090aa235f0c74b9016ae1a5a2a69c87d2a45ec43f67834ae1`, 688 input tokens and 61 output tokens. Estimated cost was **USD 0.001986**, using explicitly supplied prices of USD 2/million input and USD 10/million output. The probe enforced a USD 0.10 budget ceiling and requested no repository tools. It proves real SDK/structured-output compatibility, not a complete engineering cycle. Prices were checked against the [official configured-model documentation](https://developers.openai.com/api/docs/models/gpt-6.1-sol).

Additional checks passed: startup/configuration script syntax, disposable configuration generation and refusal to overwrite, private file modes, empty-cache image preparation, Docker isolation assertions (UID, seccomp, no-new-privileges, CPU/memory/PID limits, read-only root, bounded tmpfs, absent credentials/home/socket, network denial), timeout/output limits, zero-test rejection, stale fencing, durable replay and daemon restart. Cache preparation used empty disposable caches; dependency-rich target repositories still need owner-approved offline caches.

The final regression initially exposed a test race between the completed state and asynchronous outbox delivery. The test now waits with a deadline for the persisted replay event; the complete rerun passed. Browser tests also use dedicated ports so they do not reuse the owner's running development server.

## Remaining live verification blocker

A private disposable repository was created and seeded solely for live validation:

`https://github.com/ghptc/aep-sandbox-m8-20261028-3a0ed9`

OpenAI model listing succeeded (HTTP 200); GitHub App authentication and installation token creation succeeded (HTTP 200/201). However, requesting a token scoped to this repository from installation **176070757** returned **HTTP 422**:

> There is at least one repository that does not exist or is not accessible to the parent installation.

An earlier attempt to add the repository through the owner's authenticated GitHub CLI returned HTTP 403: “You do not have permission to modify this app on ghptc.” The owner attempted the installation update, but a fresh repository-scoped token check still returned 422, then explicitly requested proceeding with the blocker documented. The runtime did not substitute owner OAuth credentials for GitHub App credentials.

The installation administrator must select/save the repository in `https://github.com/settings/installations/176070757` for the App matching `.env`, or supply the correct authorized installation/App credentials. Then run the optional real cycle described in [local-runtime.md](local-runtime.md). No full real agent cycle, GitHub App clone/push, generated PR or published review is claimed. No platform default branch was used as the execution target. The disposable repository is retained for follow-up; nothing was merged.

The owner workstation service has not been silently installed, and owner production configuration has not been overwritten. Follow the local startup guide to generate/review the explicit allowlist and start the daemon and platform. Systemd installation, populated offline dependency caches, Playwright-in-sandbox images, a complete paid multi-agent cycle and GitHub delivery remain prerequisites or follow-up verification as applicable. These limits mean the full live Definition of Done remains blocked, despite passing independently verifiable implementation checks.
