# Approved greenfield execution verification

Final solution build: passed, zero warnings/errors. Production API/worker and separate executor Docker images built successfully under unique greenfield review tags; immutable identities are recorded in the evidence manifest. Full backend regression with actual Docker opt-ins: **386 passed, 3 skipped** (Application 81, Architecture 2, Domain 19, Integration 222, LocalExecutor 62; two live OpenAI/GitHub and one separately opted-in disposable-GitHub test skipped).

Command: `TEST_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27058 RUN_LOCAL_EXECUTOR_TESTS=1 EXECUTOR_TEST_IMAGE=sha256:d597eaf18bed0c507ef5f1fc1bb0d75985a2b82838e8057288eb42ea13bd0bec dotnet test backend/AIEngineeringPlatform.sln --no-build --no-restore -m:1`.

The new real Docker fixture passed actual .NET HTTP integration tests (two executed, one skipped), React component tests (three executed), typecheck and production build with networking disabled and the existing isolation/resource controls. The combined receipt reports exactly five executed tests. Source hashes, original default HEAD and the lock receipt remained intact. Earlier focused catalog/validation tests passed; narrow bootstrap feedback and progress regressions passed. The initial runtime image failed before tests because `/opt/aep` was mode 0444; its image pin and failed TRX are retained and excluded from success evidence.

No frontend product code changed, so M4's 55 component tests/five Playwright cases remain separate evidence rather than being claimed as rerun. These are platform regression fixtures, not M5 autonomous delivery. No customer application source was authored, no existing execution/workspace was resumed or rewritten and live OpenAI spend remains USD 0.

See [ADR 015](architecture/015-approved-greenfield-execution.md) for security review, exact causes, migration and safe rollback. Source/test/TRX hashes are recorded in `evidence/runtime-v2-greenfield/verification.json`.
