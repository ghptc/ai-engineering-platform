# HTTP and event contracts

Base path: `/api/v1`. `/openapi/v1.json` contains the generated API document and requires owner authentication. ASP.NET Core serves the React SPA at `/`; unknown API/hub paths return HTTP 404 rather than the SPA.

Authenticate with `POST /api/v1/auth/login` and JSON `{"password":"<owner credential>"}`. Requests use an HttpOnly cookie. All REST mutations except the signed GitHub webhook require `X-Requested-With: AIEngineeringPlatform`. Use `/auth/me` to inspect the session and `/auth/logout` to terminate it. Production cookies require HTTPS. No API key or GitHub token is returned to the browser.

| Resource | Operations |
| --- | --- |
| Projects | `POST /projects`, `GET /projects`, `GET/PUT/DELETE /projects/{id}` |
| Context and memory | `POST /projects/{id}/contexts`, `GET /projects/{id}/memory` |
| Requirements | `GET/POST /projects/{id}/requirements`, `PATCH /requirements/{id}` |
| Runs | `POST/GET /projects/{id}/runs`, `GET /runs/{id}` |
| Run control | `POST /runs/{id}/pause`, `/resume`, `/retry`, `/cancel` |
| Evidence | `GET /runs/{id}/events`, `GET /runs/{id}/artifacts` |
| Conversations | `GET/POST /projects/{id}/conversations`, `GET/POST /conversations/{id}/messages`, `POST /conversations/{id}/reply` |
| Agents | `GET /agents`, `GET /agents/{id}`, `PATCH /agents/{id}/configuration` |
| Reviews | `GET /projects/{id}/reviews`, `GET /reviews/{id}`, `GET /projects/{id}/pull-requests` |
| Approvals | `GET /approvals`, `POST /approvals/{id}/decision` |
| GitHub | `POST /integrations/github/repositories`, `POST /webhooks/github` |
| Operations | `GET /usage`, `GET /activity`, `GET /settings`, unauthenticated `/health/live` and `/health/ready` |

Run creation requires `Idempotency-Key` (1–128 characters), returns 202 and a Location for the persisted run. Only one recoverable run may exist per project; failed runs must be retried or cancelled before starting another. A repeated key returns the original run. Pause, cancel and retry obey the domain state machine. Project PUT requires the current `version`; conflicts return 409. DELETE archives the project and preserves history. Requirement PATCH creates an immutable revision referencing its predecessor.

Paged list responses use `{items,offset,limit}` and accept `offset` and `limit` (1–500, default 50). Run lists also accept a `status` filter. `/runs/{id}/events?after=<sequence>` replays persisted events. `/projects/all/runs` and `/projects/all/reviews` provide owner-wide views. Project memory is an array. Validation and operational errors use `application/problem+json` with a traceId. HTTP 401/403 protect owner operations; 409 represents state/concurrency conflicts; unconfigured integrations return 503.

`POST /approvals/{id}/decision` accepts `{approve:boolean,reason:string}`. Only the human-authenticated endpoint can decide. Agents have no approval tool. Decisions are persisted before worker reconciliation. Delivery approval does not authorize a merge.

SignalR hub: `/hubs/engineering`, owner authentication required. Invoke `SubscribeProject(projectId, afterSequence)`; the hub validates access, subscribes, then replays persisted events. Listen to `EngineeringEvent` envelopes `{id,kind,projectId,runId,name,content,sequence,createdAt,metadata}`. Named events are also emitted. Clients must deduplicate by ID/sequence and refetch REST after reconnecting; SignalR is not the source of truth.

Named execution events include RunStarted, StageChanged, AgentStarted, AgentMessageCreated, ToolInvocationStarted, ToolInvocationCompleted, ArtifactCreated, TestExecutionCompleted, ReviewCompleted, ApprovalRequested, RunFailed and RunCompleted. Transition events are persisted through an atomic outbox; at-least-once live delivery is expected.
