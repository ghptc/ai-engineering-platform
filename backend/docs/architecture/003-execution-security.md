# ADR 003: Separate execution trust boundary

Status: accepted.

Repository contents, generated patches and build scripts are untrusted. The platform container does not execute repository processes, embed a host shell tool, or mount a Docker socket. The configured HTTPS remote executor is the sole route for repository inspection, edits, test profiles and controlled Git CLI operations. Missing executor configuration fails closed.

The remote executor is external infrastructure, not shipped inside the platform image. Its control plane must implement the contract in ../sandbox-contract.md using isolated short-lived workloads with CPU, memory, filesystem, output, network and time limits. The platform adapter sends mandatory limits, idempotency IDs, lease ownership and fencing tokens and validates returned workspace/evidence identity. The infrastructure operator must enforce these limits; an HTTP adapter alone cannot prove remote isolation.

GitHub REST calls use per-installation, repository-scoped short-lived App tokens. Tokens stay in memory and authorization headers. Untrusted builds must never receive application secrets, App private keys, MongoDB credentials or the sandbox control token. Trusted Git transfer infrastructure may broker repository-scoped temporary access without exposing credentials to builds. Default branch mutations and automatic merges are disabled.

The initial authorization model is a single owner. Owner credentials are PBKDF2-SHA256 hashes supplied through deployment secrets. HttpOnly SameSite=Strict cookies, production Secure cookies, same-origin mutation headers, rate limiting and security headers protect the browser/API boundary. Deploy behind HTTPS; there is no permissive cross-origin configuration.
