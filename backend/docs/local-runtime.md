# Local workstation runtime

Milestone 8 adds `AIEngineeringPlatform.LocalExecutor`, a .NET 10 host daemon. It is not loaded by the platform API and is not a second platform application container. It alone can invoke the local Docker CLI. Workloads use ephemeral isolated containers; MongoDB is a separate persistent service.

## Prerequisites and trust boundaries

On Arch Linux install .NET 10 (`dotnet-sdk` / `aspnet-runtime`), Docker, Git, Python and util-linux (`prlimit`). Start Docker and verify cgroup v2, seccomp, CPU/memory/PID controllers. `docker info` must succeed for the daemon identity. No Docker socket or Docker-group access is given to the application container. For a system deployment use the dedicated service identity in `backend/executor/aep-executor.service`; Docker-group membership gives that trusted daemon broad host control and must not be granted to agent code.

Create a **disposable** GitHub repository named `aep-sandbox-*` with an initial commit for verification, install the dedicated GitHub App on it, and add only that repository to the daemon allowlist. The default platform repository is never a verification target. App permissions are Contents read/write and Pull requests read/write. The host daemon and platform need authorized App credentials for their respective trusted operations; build/test containers never receive them. `gh auth` credentials are not silently repurposed as application credentials.

## Development setup

1. Copy `.env.example` to `.env`; set OpenAI API key/model, GitHub App ID/private-key path and, optionally, an existing PBKDF2 owner credential hash. Secrets stay in ignored owner-only files. Create the hash using `python3 backend/scripts/owner-credential.py` or let the configuration script prompt locally.
2. Build the approved runtime, daemon and single platform image:

```sh
backend/scripts/local-runtime.sh build
```

3. Generate explicit local configuration:

```sh
backend/scripts/local-runtime.sh configure \
  --repository YOUR-OWNER/aep-sandbox-disposable \
  --installation-id YOUR-INSTALLATION-ID --default-branch main
```

This writes ignored `backend/.local-runtime/` files, pins the image ID, generates a random control token, and creates an owner-only app environment file. Existing executor configuration is never overwritten. The GitHub private key is copied into restricted local runtime storage for explicit read-only mounting into the trusted application. Review `executor.json` before starting, particularly repository identity, default branch and fixed profile command paths. The example expects a `backend/` .NET project/solution and `frontend/` npm project; adapt it to the authorized repository, or use Node's built-in test runner for a small dependency-free disposable fixture.

4. Prepare offline dependencies if the repository needs them. Builds/tests have **no network**. A missing dependency is a prerequisite failure, not permission to enable arbitrary egress. With explicitly owner-approved local dependency caches:

```sh
python3 backend/scripts/seed-executor-cache.py \
  --nuget-cache /explicit/path/to/nuget/packages \
  --npm-cache /explicit/path/to/npm/_cacache \
  --runtime-image aep-executor-runtime:local --tag aep-executor-cached:local
docker image inspect aep-executor-cached:local --format '{{.Id}}'
```

Set that immutable `sha256:` ID in the allowlisted profiles. The utility copies selected regular-file dependency data into an image; it rejects symlinks/special files, excludes known credential files and caps preparation size. Do not approve cache data containing secrets. It does not infer home paths, mount home directories, execute generated code on the host or enable build network access. npm uses a copy in bounded temporary storage and `npm ci --offline --ignore-scripts`; .NET restores only from the available offline packages. The example disables online NuGet vulnerability lookup inside the disconnected workload (`NuGetAudit=false`); audit dependency caches separately during owner-controlled image preparation. Large caches may require a smaller curated image or adjusted bounded resources. Browser testing requires a separately approved image containing its browser binaries; the default SDK/Node image does not claim to include Playwright browsers.

5. Start the daemon in a terminal:

```sh
backend/scripts/local-runtime.sh daemon
```

It binds only `backend/.local-runtime/socket/executor.sock`, verifies Docker isolation, removes only its abandoned workloads, and uses private state plus durable receipts. There is no HTTP port to expose. The socket is group-readable/writable and the directory group-traversable; the application is granted that group, not the Docker group.

6. In another terminal start MongoDB and the platform:

```sh
backend/scripts/local-runtime.sh up
```

Open **http://localhost:8080**. The local script uses Development cookie settings only on the loopback-bound application port; do not expose it on a public interface. For a system installation use HTTPS and Production cookies. MongoDB has no published port in the local app network. The executor socket directory is mounted read-only into the app; neither its canonical state nor Docker socket is mounted.

Sign in, configure the seven agents' available models, current token prices and output limits in Settings, and verify the local sandbox reports Ready. Create a project, connect the same allowlisted repository/installation/default branch, submit requirements/context, set a small budget/iteration limit and start the run. Real model output, tool invocations, test evidence and cost limits appear in the persisted timeline. A human approves delivery; the platform pushes only the feature branch, opens a draft PR and submits an actual review/comment with local review content and test evidence. It never merges.

To stop containers without deleting state:

```sh
backend/scripts/local-runtime.sh down
```

Stop the foreground daemon separately. To replace/rebuild the application container, stop it and remove only that application container explicitly before `up`; MongoDB's named volume and executor state are retained. Configuration/run changes are explicit; existing run repository snapshots do not change silently.

## System service

Publish the daemon to `/opt/aep-executor`, create a dedicated `aep-executor` user/group, copy the reviewed JSON and random token to `/etc/aep-executor`, and install `backend/executor/aep-executor.service`. Use `/var/lib/aep-executor` for state and `/run/aep-executor/executor.sock` for IPC. Private keys must be readable by the daemon from `/etc`, not an owner home directory blocked by ProtectHome. The token must be at least 32 random characters, not readable by others, and not group-writable. Give only the application container the IPC group and control token. No TCP binding or host Docker socket mount is needed. This privileged installation is documented rather than silently modifying the owner's users or systemd configuration.

The default rootful socket is `unix:///var/run/docker.sock`. An explicitly configured rootless local socket is also supported, provided the engine passes the same delegated cgroup v2/resource/seccomp checks. All image IDs and commands are owner-managed and require daemon restart to change. Platform request ceilings are configurable through SandboxExecution__TimeoutSeconds (maximum 120), SandboxExecution__MemoryMb (maximum 4096), SandboxExecution__CpuLimit (maximum 4) and SandboxExecution__OutputLimitBytes (maximum 100000); the daemon profile can lower them further.

## Real tests and optional live cycle

```sh
backend/scripts/local-runtime.sh test
```

Real Docker tests create a uniquely named disposable **local bare Git repository**, run isolated .NET/Node assertions, edit/patch actual files, commit and push only its feature branch, and assert the default-branch SHA is unchanged. They test non-root UID, no-new-privileges, seccomp, cgroup resource values, read-only root, network denial, absent Docker socket/home/credentials, filesystem limits, zero-test rejection, deadlines, output bounds, cleanup, authentication, durable replay and daemon restart. Unit tests run without Docker; real tests are explicitly skipped unless `RUN_LOCAL_EXECUTOR_TESTS=1` and `EXECUTOR_TEST_IMAGE` are provided.

The optional live test uses real OpenAI, GitHub App APIs, MongoDB and an already running local daemon. Provision/seed the disposable repository and its offline profiles first, then explicitly set:

```text
RUN_LIVE_AGENT_TESTS=1
OPENAI_API_KEY=<secret>
OPENAI_DEFAULT_MODEL=<available model>
GITHUB_APP_ID=<app id>
GITHUB_APP_PRIVATE_KEY_PATH=<host path>
LIVE_GITHUB_OWNER=<owner>
LIVE_GITHUB_REPOSITORY=aep-sandbox-<disposable name>
LIVE_GITHUB_INSTALLATION_ID=<installation>
LIVE_GITHUB_DEFAULT_BRANCH=main
LIVE_EXECUTOR_SOCKET_PATH=<host socket path>
LIVE_EXECUTOR_TOKEN_FILE=<host token file>
LIVE_MODEL_INPUT_PRICE=<current USD per million input tokens>
LIVE_MODEL_OUTPUT_PRICE=<current USD per million output tokens>
LIVE_MAX_BUDGET_USD=<positive budget no greater than 5>
TEST_MONGODB_CONNECTION_STRING=<isolated test MongoDB>
```

Run `dotnet test backend/tests/AIEngineeringPlatform.Integration.Tests --filter RealAgentsDeliverPullRequestOnlyInAuthorizedDisposableRepository`. Opting in authorizes the test to approve delivery only in that explicitly disposable repository. It leaves the resulting PR for human inspection and does not merge or delete it. Missing credentials fail an opted-in test; without opt-in it is reported skipped. No simulated model or GitHub adapter is used in this test. Do not infer a live success from deterministic browser tests.

## Focused local tasks

Use **Start focused task** for a bounded implementation change. Save lasting human constraints in **Project instructions**; attach any additional selected human-message IDs through `HumanInstructionIds` in the task API. Earlier conversations stay visible and retrievable, but are not default execution transcripts. Request BA/Architect expertise only when needed, and select the developer specializations appropriate to the repository. Queue further authorized tasks to continue automatically after verified, approved delivery; all queued tasks share the original execution budget and each delivery still needs approval. The existing **Start execution** action retains the project-planning route for complex/legacy work.

For the dependency-free JavaScript fixture, configure owner-managed test profiles with an immutable image ID and fixed arguments such as `"Command": ["node", "--test"]` for `all` and `backend`. Do not point that fixture at the example profile that expects backend/.NET and frontend/npm projects. The executor recognizes Node's default and TAP reporters. Apply profile or daemon-binary changes by an explicit daemon restart; do not substitute simulated adapters or alter the running owner's configuration during verification.

The existing opt-in live workflow supports `LIVE_FOCUSED_TASK=1`. It then uses only Developer, Test Engineer, QA and Reviewer, requires the two-file answer() 1→2 change, persists real source/test/review evidence and leaves a draft PR. All existing live-test credential, budget, disposable-repository, approval and isolation restrictions still apply. The live test approves only the explicitly authorized disposable verification delivery, not production deliveries or merges.
