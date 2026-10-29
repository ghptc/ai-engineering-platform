> Milestone 8 update: this contract now also has an included local implementation in `AIEngineeringPlatform.LocalExecutor`, reached through an authenticated Unix socket. See [local runtime](local-runtime.md) and [ADR 004](architecture/004-local-docker-executor.md). HTTPS remains optional; the local transport does not expose a network listener. Local ApplyPatch accepts a JSON-encoded `{path,search,replacement}` patch with exactly one matching source region. Workloads have no network and use administrator-approved offline images/profiles.

# Remote execution contract

The platform implements an HTTPS client for a separately provisioned trusted executor. There is intentionally no unsafe local fallback. The local daemon is included; a remote service is optional. Simulated test adapters are not a production executor.

`EXECUTION_SANDBOX_PROVIDER=Remote`, `EXECUTION_SANDBOX_ENDPOINT=https://executor.example/`, and `EXECUTION_SANDBOX_TOKEN` configure the control-plane connection. Endpoints with embedded credentials, redirects to untrusted endpoints, or plaintext HTTP must not be used.

## Request

`POST /v1/operations`, with `Authorization: Bearer <control-token>` and `Idempotency-Key: <operationId>`.

```json
{
  "operationId": "stable-operation-hash",
  "runId": "platform-run-id",
  "repository": {"owner":"organization","repository":"product","installationId":123,"defaultBranch":"main"},
  "operation": "RunTests",
  "arguments": {"profile":"all"},
  "timeoutSeconds": 60,
  "cpuLimit": 1,
  "memoryMb": 512,
  "outputLimitBytes": 100000,
  "networkEnabled": false,
  "fence": 42,
  "leaseOwner": "unique-worker-acquisition"
}
```

The executor control plane must:

1. Authenticate the platform and authorize repository identity, installation, operation and profile. Clone/check out the default-branch snapshot into an isolated workspace identified by runId and create `engineering/<runId>`. Keep workspaces for interrupted stages until explicit cleanup or an operator retention deadline.
2. Deduplicate operation IDs durably. Return the original result on retries. Serialize access to each workspace, reject stale fencing tokens, and stop stale writers before accepting a newer fence. Never mutate the default branch.
3. Resolve and validate every path after symlink resolution. Reject absolute paths, traversal, `.git`, secrets, device files and writes outside the workspace. Disable repository hooks and external Git protocol helpers. Treat patches as untrusted input, including rename/copy targets and symlinks.
4. Run builds/tests in disposable hardened containers or microVMs with a read-only base filesystem, unprivileged UID, dropped capabilities, no privilege escalation, CPU/memory/pid limits, output bounds, enforced deadlines, no Docker socket and no host mounts. Mount only the run workspace. Deny network by default; pre-provision dependency caches or explicitly allow only trusted package mirrors.
5. Never expose the platform API key, MongoDB connection, GitHub private key, sandbox control token or host credentials inside workloads. Use a trusted transfer plane for short-lived repository-scoped Git access. Never place tokens in command arguments or logs.
6. Select test commands from administrator-defined profiles (`backend`, `frontend`, `e2e`, `all`). RunCommand accepts only administrator-defined build, lint, format and typecheck profiles. Models cannot submit unrestricted shell commands. A profile that executes zero tests must fail. Report actual exit codes and immutable report references.
7. Compute a workspace revision over the complete source snapshot, including tracked/untracked source and generated tests, independent of Git commit metadata. Return the same revision for InspectDiff and RunTests when source is unchanged. For CreateCommit require `arguments.expectedRevision` to match that tested snapshot. Reject dirty/unreviewed source changes during delivery.
8. Enforce `engineering/<runId>` for commits and pushes; use controlled Git CLI argument arrays, bounded output/timeouts and a minimal scrubbed process environment. Match the configured repository identity before transfer. Never run repository-supplied hooks. Operations include ReadRepository, ListFiles, SearchCode, ReadFile, WriteFile, ApplyPatch, InspectDiff, RunTests, CreateCommit and PushBranch.
9. Persist immutable evidence, redact secrets from output, clean disposable test workloads after each operation, and expire abandoned workspaces according to a documented retention policy. Cleanup must not destroy resumable work silently. The executor must enforce the deadline even if the HTTP client disconnects.

## Response

```json
{
  "success": true,
  "output": "Actual bounded stdout, diff or operation result",
  "evidenceId": "immutable-executor-evidence-reference",
  "exitCode": 0,
  "workspaceId": "platform-run-id",
  "diffHash": "source-snapshot-revision"
}
```

Nonzero test exit codes fail the quality gate. The platform rejects absent evidence IDs, mismatched workspace IDs and oversized responses. Test progression also requires a revision; QA must cite test evidence. Missing configuration disables execution instead of granting host access.

The HTTP adapter cannot attest that an arbitrary server enforces isolation. Provision and audit the executor control plane before enabling production execution. The local daemon brokers GitHub App installation credentials only for trusted Git transfers. Build/test containers never receive them.

## Repository reconciliation and profile discovery

`ReadRepository {}` must return `HeadCommit`, `BaseCommit`, `SelectedBranch` (nullable), `HasSourceChanges` and `TestProfiles`, alongside the bounded file inventory. Profiles are filtered against real files and administrator capabilities. The `node` profile is exactly `["node", "--test"]`; .NET needs project/solution files; npm needs its package manifest and `npm ci` also needs a lockfile. Composite administrator profiles declare `RequiredFiles`; existing composite configurations must add these prerequisites. Missing or ambiguous profiles block execution, with no shell fallback.

`ReconcileRepository {}` is a GitHub control-plane read: it inventories bounded same-repository engineering branches, commits and PRs against the configured default branch. `SelectBranch {branch,commit}` fetches an exact existing engineering branch into the run's dedicated branch. It requires current same-repository remote facts, a clean workspace and matching fetched SHA; it never checks out or pushes the default branch. Tests are invalidated after selection. The original default snapshot remains the diff base. Legacy dirty workspaces without a baseline fail closed and retain their source for human reconciliation.

No-op implementations continue through current-revision tests, acceptance QA and actual diff review. They can finish as `AlreadySatisfied`, persisting the existing commit and exact matching PR rather than manufacturing edits or invoking delivery tools. New source changes still require human delivery approval. Further edits to a reconciled existing feature branch require explicit existing-branch delivery handling; the platform blocks duplicate PR creation. Other explicit outcomes are `Implemented`, `AwaitingHumanInput`, `Blocked` and `Failed`. Human-input and capability blockers pause resumable runs; they are not implementation failures. Previous test reports are never imported across task scopes: this implementation runs fresh required tests for each task/iteration.
