# Owner-operated M5 certification harness

This fixed-scope tool supervises the explicitly authorized disposable repository and uses the real production API, runtime, App permissions and executor. It never authors target application source. It is not a replacement for the normal web UI.

See [the outcome, security review and recovery procedure](../../docs/runtime-v2-m5-verification.md). **The existing attempt is paused. Do not invoke `--start` again.**

Read-only observation of that attempt:

```sh
dotnet run --project backend/tools/AIEngineeringPlatform.Certification -- \
  --observe --output /tmp/aep-m5-certification \
  --password-file /tmp/aep-m5-certification/owner-password
```

A metadata-only check, which makes no paid Responses request or repository mutation:

```sh
dotnet run --project backend/tools/AIEngineeringPlatform.Certification -- \
  --preflight --settings /path/to/private/owner.env --output /tmp/private-preflight
```

The owner settings require `GITHUB_APP_ID`, `GITHUB_APP_PRIVATE_KEY_PATH`, `OPENAI_API_KEY` and `OPENAI_DEFAULT_MODEL`. Values are parsed literally, not sourced by a shell. Store credentials outside Git. Output directories are owner-only on Linux. Never attach raw settings, passwords, tokens or session cookies to reports.

`--start` is an explicit, one-shot, already-used action for a fresh isolated production Compose database at `127.0.0.1:18099`. It repeats permission/repository/model verification, refuses existing executions, preserves default role instructions, sets conservative pricing and bounded task limits, registers the authorized repository and submits one focused task through the normal API. `start-intent.json` is an exclusive durable fence. After an ambiguous failure, reconcile through persisted API/DB/executor evidence; do not delete the intent file or retry mutations blindly. There is no automatic resume, cancellation, approval, merge or source-writing action in this harness.

The current run must be resumed, if explicitly approved, through the normal owner UI/API with its existing identity, policy, receipts and checkpoint. A fresh metadata check is safe; another `--start` is not a recovery procedure. Keep cumulative live spending below the original USD 10 authorization, including failed requests. Do not use earlier public review credentials with real provider keys.
