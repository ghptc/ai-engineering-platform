# ADR 002: Durable stage checkpoints, leases, and evidence

Status: accepted.

MongoDB atomically claims runnable executions with expiring leases and increments the version as a fencing token. Every worker acquisition has a unique owner. Writes require both expected version and live lease ownership. The worker renews leases every ten seconds and stops its work if ownership or control state changes. Stale leases can be reclaimed after two minutes.

Run transitions and their outbox entries commit in one MongoDB document. The worker drains these immutable entries into persisted sequenced events before live publication. Delivery is at least once; clients use sequence numbers and refetch authoritative REST data. Project conversations and artifacts do not expire.

Agent responses and tool results are durable checkpoints. Stable operation IDs include run, stage, iteration and turn. Remote executors must honor idempotency and monotonically increasing fences. No database transaction remains open during external calls. Task responses form the usage ledger, avoiding duplicate charges for cached responses. A provider may bill an interrupted request before its response is checkpointed; platform estimates cannot replace a provider account spending cap.

Tests must return a successful exit code, evidence identifier and workspace revision. QA must reference required execution evidence and cover selected requirements. Before delivery the workspace revision must match the tested revision. Correction loops consume development iterations; manual retries resume the failed stage, and transient provider errors have bounded backoff. A human approves delivery; approval and run-state reconciliation survive interruption between writes. GitHub approval errors remain failures and never count as independent review.

A completed run records verified requirement progress and derives the next context from persisted memory. It does not automatically merge or start an unlimited new run.
