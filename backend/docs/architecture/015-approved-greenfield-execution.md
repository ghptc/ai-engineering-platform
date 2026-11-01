# ADR 015: Approved greenfield lock bootstrap and combined verification

Status: implemented and verified before live certification. Builds on M4 source `46399f5`.

## Proven gaps

The managed frontend profile required a persisted package lock but agents had neither a safe lock generator nor package-network authority. Writing invented integrity data or running a host package resolver would violate the execution boundary. Also, the managed configuration had separate backend/frontend profiles but no aggregate profile; the orchestrator correctly blocked when neither one profile nor an approved aggregate could certify both stacks.

A real Docker regression exposed a third defect: `COPY --chmod=0444 ... /opt/aep/profiles.py` created `/opt/aep` as mode 0444 when the directory was absent. The fixed non-root workload could not traverse it. The exact failure was Python errno 13 before any tests executed. The corrected image creates `/opt/aep` with mode 0755 before copying the read-only script. Root filesystem, user, capabilities, mounts, network and resource limits remain unchanged.

## Controlled source preparation

`PrepareFrontendLock {}` is an additive executor operation available to Frontend/Test roles. It accepts no path, package, command, image or network arguments. ReadRepository advertises exact catalog versions, allowed manifest fields and a catalog SHA-256, while retaining the warning that actual cache/SDK availability must be verified.

The daemon validates the bounded frontend manifest against the complete administrator-owned pinned dependency catalog, rejects duplicate/unsupported fields, unknown packages, ranges, URLs, overrides/workspaces and linked paths, and only creates a missing `frontend/package-lock.json`. It reuses actual trusted package/integrity entries embedded from the managed image build manifest; only root package metadata/dependency placement changes. Compact JSON fits the existing 100000-byte source-write bound. Existing lockfiles/manifests are never overwritten. No repository script or resolver runs on the host.

The operation uses the normal repository allowlist, lease owner/fence, operation fingerprint, receipt ledger, source revision and tool accounting. Its receipt can replay idempotently; a new attempt cannot overwrite existing source. Only a matching known non-executed HTTP 400 becomes bounded corrective feedback; authorization, unknown outcomes, conflicts and infrastructure failures still block. Repeated bootstrap without a changed source revision counts toward the existing inspection progress guard, with thresholds unchanged at feedback 6 and stop 12. Actual new source resets progress through the existing revision rule.

## Actual combined tests

The owner-defined managed `all` profile runs backend then frontend sequentially inside the same controlled workload. It is advertised only when both backend test-project files and frontend manifest/lock prerequisites exist. There is no implied parallelism or additional model/tool allowance.

The trusted Python helper streams subprocess output directly to preserve outer output/time enforcement. It requests actual .NET TRX and Vitest JSON reports in bounded temporary storage and uses executed counters (excluding skipped tests). Missing reports, zero executed tests, malformed counts, failed commands and incomplete stage coverage cannot certify success. Its final structured summary follows untrusted command output; the daemon requires matching profile coverage and actual exit status. Aggregate counts sum the executed backend and frontend tests instead of taking a maximum. Legacy textual counting now uses the final runner summary and excludes skipped .NET tests rather than accepting an earlier stdout claim.

## Evidence and boundaries

The full backend suite passed 386 tests with explicit real Docker/Git/Unix executor opt-ins; three live/remote opt-ins were skipped. The new Docker regression executed two real .NET HTTP tests plus three real React component tests, excluded one skipped .NET test, passed frontend typecheck and production build, verified exact source-revision preservation, verified receipt replay and left the disposable local bare repository's default HEAD unchanged. Catalog tests prove original transitive package/integrity data is retained; both dependencies/devDependencies placement works with real offline npm ci. These fixtures are test-only platform code and are never copied into the M5 target repository.

The first permission-failing image and its failed test evidence are retained. Corrected runtime: `sha256:d597eaf18bed0c507ef5f1fc1bb0d75985a2b82838e8057288eb42ea13bd0bec`. No live provider spending, GitHub target mutation or existing execution resume was performed.

## Migration and rollback

Rebuild runtime-image and executor together through Compose, retaining old immutable image pins. The helper version label is 2; an old helper lacks the structured completion summary and cannot be silently certified by the new managed profile. API/worker registrations and instruction revisions are additive. Owner-authored previous instructions are appended to history before migration. No workspace, receipt, checkpoint, operation ID or budget is reset or pruned.

Rollback pauses affected V2 work and retains all source, generated locks, receipts and image pins. Restore a compatible API/worker/executor/image set before an explicit owner resume. Do not overwrite an existing lock, relax offline execution, retry an uncertain write, bypass the guard or increase limits as recovery. Live certification still requires verification of the explicitly authorized GitHub App installation/repository and its separate USD 10 aggregate spending authorization.
