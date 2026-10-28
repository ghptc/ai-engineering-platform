# ADR 001: Modular monolith and dependency boundaries

Status: accepted.

The platform deploys as one ASP.NET Core application process hosting REST, SignalR and BackgroundService scheduling. ASP.NET Core serves the Vite production assets. MongoDB and secure execution infrastructure are separate prerequisites.

Domain owns project invariants, execution policies and the explicit state machine. Application owns use cases, context building and integration contracts. Agents specialize a common structured execution strategy. Orchestration coordinates evidence-gated workflows. Infrastructure implements MongoDB, OpenAI Responses, GitHub App and remote sandbox adapters. Worker hosts scheduling; API composes dependencies and enforces owner access.

The domain has no framework/provider dependencies. Application depends only on Domain; no inner layer accesses SDK objects. Agent instructions and model prices are persisted, versioned and configured independently of business logic. Bounded contexts are logical modules, not independently deployed services.

Operational records use a shared indexed MongoDB envelope with an explicit kind and typed JSON payloads; projects and runs have dedicated persistence documents and optimistic concurrency. This keeps event/message/artifact formats evolvable. It trades MongoDB-native querying of payload fields for explicit indexed operational metadata. Secrets are injected by environment or mounted files and never persisted in those envelopes.
