// Each batch uses a fresh disposable API host, preserving the real five-login/minute policy.
// Do not raise production auth limits merely because browser cases use separate cookie jars.
import { spawnSync } from "node:child_process";
import { readFileSync, mkdirSync } from "node:fs";
import { resolve } from "node:path";
import { fileURLToPath } from "node:url";
const cli = fileURLToPath(
  new URL("../node_modules/@playwright/test/cli.js", import.meta.url),
);
const args = process.argv.slice(2);
const invoke = (arguments_, options = {}) =>
  spawnSync(process.execPath, [cli, "test", ...arguments_], {
    env: process.env,
    ...options,
  });
if (args.some((x) => ["--list", "--help", "--version", "-h"].includes(x))) {
  process.exit(invoke(args, { stdio: "inherit" }).status ?? 1);
}
const discovery = invoke([...args, "--list", "--reporter=json"], {
  encoding: "utf8",
  maxBuffer: 10 * 1024 * 1024,
});
if (discovery.status !== 0) {
  process.stderr.write(
    discovery.stderr || discovery.stdout || "Browser discovery failed.",
  );
  process.exit(discovery.status ?? 1);
}
let report;
try {
  report = JSON.parse(discovery.stdout);
} catch {
  throw new Error("Browser discovery returned no valid report.");
}
if (report.errors?.length)
  throw new Error("Browser discovery contains errors.");
const targets = [];
function collect(suite) {
  for (const spec of suite.specs || [])
    targets.push(`${spec.file}:${spec.line}`);
  for (const child of suite.suites || []) collect(child);
}
for (const suite of report.suites || []) collect(suite);
if (!targets.length)
  throw new Error(
    "No browser tests matched; zero tests cannot certify a change.",
  );
// Explicit file filters keep native CLI semantics; normal full runs are capped at five isolated logins.
if (
  targets.length <= 5 ||
  args.some((x) => !x.startsWith("-") && x.includes(".spec."))
) {
  process.exit(invoke(args, { stdio: "inherit" }).status ?? 1);
}
const options = args.filter((x) => !x.startsWith("--reporter="));
const reportRoot = resolve("test-results/reports");
mkdirSync(reportRoot, { recursive: true });
for (let offset = 0; offset < targets.length; offset += 5) {
  const batch = offset / 5 + 1;
  const jsonPath = resolve(reportRoot, `batch-${batch}.json`);
  console.log(
    `Browser batch ${batch}: fresh test-only API/Mongo state; owner login limits unchanged.`,
  );
  const result = invoke(
    [
      ...options,
      ...targets.slice(offset, offset + 5),
      "--reporter=line,json",
      "--output",
      `test-results/batch-${batch}`,
    ],
    {
      stdio: "inherit",
      env: { ...process.env, PLAYWRIGHT_JSON_OUTPUT_FILE: jsonPath },
    },
  );
  if (result.status !== 0) process.exit(result.status ?? 1);
  const completed = JSON.parse(readFileSync(jsonPath, "utf8"));
  if (
    (completed.stats?.expected || 0) + (completed.stats?.flaky || 0) === 0 ||
    completed.stats?.unexpected > 0
  )
    throw new Error(
      "Browser batch has no successful executed tests or has failures.",
    );
}
