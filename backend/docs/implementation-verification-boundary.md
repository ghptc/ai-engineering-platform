# Implementation and verification lifecycle boundary

## Incident evidence

Read-only inspection resolved DevPulse execution `7914c5f932ec` to `7914c5f932ec43e5a500427a0236bb58`. The persisted run is paused at version 23, resume stage Implementing, with seven tool calls, 75,069 tokens and $0.200346 recorded usage. BA and Architect both completed and classified the absent approved test infrastructure as Delivery blockers. No Backend Engineer invocation occurred. EngineeringWorkflow incorrectly selected both Implementation and Delivery blockers before implementation. This deterministic policy, rather than an agent decision or invalid handoff, caused the pause.

The exact run's executor workspace is README-only. Other DevPulse runs contain partial API bootstraps; they are distinct workspaces and were not substituted as this run's source.

## Correction

Only explicit Implementation blockers prevent implementation. Delivery blockers remain in their immutable planning checkpoints. Missing applicable test profiles produce a VerificationPrerequisite event, without granting validation. Developers use existing approved tools and build profiles, and Test Engineer creates real tests. RunningTests still requires an applicable approved profile, otherwise it pauses with instructions for the owner to approve fixed targets. Tests must return successful executor evidence for the current revision before QA, review or delivery. Zero tests cannot certify a revision. No schema, allowlist, sandbox, budgeting, fencing, operation identity or human approval policy changed.

Agent lifecycle instructions describe this distinction; the existing worker migration persists the previous instruction revision before updating definitions. No production instruction or run record was edited during investigation.

## Isolated scenario verification

A separate authenticated Unix daemon used the current pinned DevPulse image and a copy of this exact run's state. The owner daemon/application remained running. The absent API target correctly produced HTTP 422. Controlled WriteFile operations then created the three project manifests in the copy; the fixed API build restored them, compiled Core/Infrastructure and failed CS5001 for the absent API entry point. This establishes usable incremental build feedback, not a passing application or test run. Original files and existing receipts matched the pre-verification snapshot afterward.

Local evidence: `/tmp/devpulse-359-isolated-sxu_6bsk` (original target rejection), `/tmp/devpulse-359-isolated-m4leja9y` (isolated bootstrap/build). No live OpenAI or GitHub agent cycle was attempted.

## Recovery after human review and deployment

Deploy the reviewed application image and restart the platform application/worker. No executor code changed, so this patch alone needs no daemon restart. Separately restart the daemon if the earlier owner-managed profile configuration has not yet been loaded; preserve its state directory and socket/authentication configuration. Confirm contract version 2 readiness.

Explicitly resume the existing run through the normal authenticated resume action. Do not reset checkpoints, mint replacement operation IDs, extend budgets automatically or remove blockers. BA/Architect checkpoints are reusable. Backend Engineer should bootstrap source through approved tools and use RunCommand with profile build after creating its target. Genuine missing SDK/package/operation prerequisites still block the affected implementation step.

Test Engineer creates executable tests. Before RunningTests can succeed, the owner must review actual test paths, offline dependencies and fixed backend/frontend/aggregate profiles; agents cannot approve or change these profiles. Reload owner-approved executor configuration and explicitly resume RunningTests. Missing/failing/zero-test runs provide no QA or delivery acceptance. Human delivery approval remains mandatory. This investigation did not resume the paused run.
