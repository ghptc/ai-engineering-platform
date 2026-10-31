# Runtime V2 deployment, migration and rollback

M1 introduces an opt-in Compose deployment; it does not import, delete or rewrite existing production MongoDB/executor state. Back up the old Mongo database, executor state and cookie key ring before an owner-scheduled cutover. Do not run two executors against the same state directory.

## First setup

Create `.env` from `.env.example`, set a PBKDF2 owner hash with `backend/scripts/owner-credential.py`, and configure provider model/prices and server-side credentials. Then use:

```sh
docker compose up -d --build
```

Open `http://localhost:8080`. The API publishes only to 127.0.0.1. MongoDB, executor and worker have no host TCP publication. Set `AEP_HTTP_PORT` if the owner already has a development server on 8080. Initial credential/allowlist setup is administrative; routine starts do not require a second terminal or raw executor JSON.

`EXECUTOR_REPOSITORIES` is a semicolon-separated owner allowlist: `owner/repository@installation-id:default-branch`. Empty means no repository operations are authorized. The GitHub App private key is supplied to the `github-credentials` named volume as `github-app.pem`, readable only by the executor and application UID 1654. Never place credentials in source, package caches, agent contexts or workload images. The existing HTTPS remote executor mode and host-managed JSON mode remain available for separately managed installations.

The executor builds/resolves the `aep-managed-runtime:local` image at startup; every executable profile contains its immutable ID and a retained owner pin keeps that digest available across moving-tag rebuilds. A persistent per-volume instance ID isolates workload cleanup between Compose deployments. A missing image fails readiness with a rebuild diagnostic. Profiles require real project/test manifests and offline package availability. The built image provisions fixed backend test and React test dependencies from the administrator-owned manifests in `backend/executor/managed/dependencies/`. Arbitrary dependencies are not implied: missing packages fail closed and agents must use supported versions or request an owner-reviewed profile update. UI-managed allowlist configuration remains later runtime work; do not claim full autonomous certification from M1.

## Sessions and security

Sessions persist in the separate `owner_sessions_v2` Mongo collection and the `owner-keys` key-ring volume. Never run `docker compose down -v` during upgrades. Browser cookies are HttpOnly, SameSite Strict and persistent for at most eight hours from authentication. Logout/credential rotation revoke access on the server. Legacy cookie-only tickets require one reauthentication at cutover; API clients still use the same login and cookie-jar contract. There is no legacy bearer scheme to migrate.

`AUTH_LOCAL_HTTP=true` is only for this loopback Compose publication. For HTTPS, set it false and configure an owner-managed TLS listener/proxy with explicitly trusted forwarding settings. Do not enable broad forwarded-header trust or publish the HTTP owner interface publicly. Persist/protect the key volume, backups and Mongo storage with owner-only access/encrypted disks. Anyone with Docker daemon or key-volume administration is inside the trusted host boundary.

Only the executor mounts `/var/run/docker.sock`. A compromised executor has host-equivalent Docker authority: deploy it on a dedicated trusted Linux Docker host, restrict access, keep the application/worker unprivileged, and never expose the socket through TCP, the web API or agent tools. The executor has no application database volume/network. Build/test workloads remain separate containers with no socket, credentials, host mounts or network. Do not relax failed isolation/cleanup checks to regain readiness.

## Recovery and rollback

Inspect `docker compose ps`, service health and bounded service logs. API liveness remains separate from worker progress; existing UI history/reconnect/control endpoints remain available after worker restart. Readiness verifies Mongo; executor health independently verifies Docker/cgroup-v2/seccomp. Restart only the affected service. Mutating operation IDs and receipts remain reconciled by the existing executor; do not reissue an uncertain mutation with a new ID.

Retain the old stack during review. To roll back, stop the new Compose project without deleting volumes and restart the prior application/daemon with its original database/state/configuration. The additive session collection is safe to retain. New server-side cookies are intentionally incompatible with old cookie-only code and require reauthentication after rollback. Do not overwrite old keys or workspaces. No production schema rewrite, run resume or delivery approval occurs automatically.
