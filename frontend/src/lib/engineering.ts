import type { Entry, Run } from "../types";
export type Data = Record<string, unknown>;
export function parse(content: string): Data {
  try {
    const v: unknown = JSON.parse(content);
    return v && typeof v === "object" && !Array.isArray(v) ? (v as Data) : {};
  } catch {
    return {};
  }
}
export function field(data: Data, key: string): unknown {
  return data[
    Object.keys(data).find((k) => k.toLowerCase() === key.toLowerCase()) ?? key
  ];
}
export function text(data: Data, key: string, fallback = ""): string {
  const v = field(data, key);
  return typeof v === "string" || typeof v === "number" ? String(v) : fallback;
}
export function list(data: Data, key: string): unknown[] {
  const v = field(data, key);
  return Array.isArray(v) ? v : [];
}
export const roles = [
  "BusinessAnalyst",
  "Architect",
  "BackendEngineer",
  "FrontendEngineer",
  "TestEngineer",
  "QAEngineer",
  "CodeReviewer",
  "SecurityEngineer",
  "InfrastructureEngineer",
];
export function human(value = "") {
  return value
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/([A-Z]+)([A-Z][a-z])/g, "$1 $2")
    .replace(/_/g, " ");
}
export function roleName(value: unknown): string {
  return typeof value === "number"
    ? (roles[value] ?? "Unknown agent")
    : String(value ?? "");
}
export const terminal = new Set([
  "Completed",
  "Cancelled",
  "Failed",
  "Paused",
  "AwaitingHumanApproval",
]);
export function mergeEntries(
  old: Entry[] = [],
  incoming: Entry[] = [],
  limit = 50,
): Entry[] {
  return [...new Map([...old, ...incoming].map((e) => [e.id, e])).values()]
    .sort((a, b) => b.sequence - a.sequence)
    .slice(0, limit);
}
export function summary(entry: Entry): string {
  if (
    entry.name === "ContextInventory" ||
    entry.name === "ContextSourceInventory"
  )
    return "Context sources measured";
  const d = parse(entry.content);
  if (
    !Object.keys(d).length &&
    /^\s*[\[{]/.test(entry.content) &&
    !entry.name.includes("Context")
  )
    return human(entry.name) + " — recorded details";
  if (entry.name === "ContextBudgetEvaluated")
    return `Context prepared — ${Number(field(d, "InputTokensUpperBound") ?? 0).toLocaleString()} estimated tokens`;
  if (
    entry.name === "ContextInventory" ||
    entry.name === "ContextSourceInventory"
  )
    return "Context sources measured";
  if (entry.name === "AgentStarted")
    return `${human(entry.content)} — started work`;
  if (entry.name === "AgentHandoffCreated")
    return `${human(roleName(field(d, "role")))} — handed over work`;
  if (entry.name.startsWith("ToolInvocation"))
    return `${human(text(d, "name", entry.content))} — ${entry.name.endsWith("Started") ? "running" : field(d, "Success") === false ? "failed" : "completed"}`;
  if (entry.name === "TestExecutionCompleted")
    return field(d, "Success") === true
      ? "Test execution passed — evidence recorded"
      : "Test execution failed";
  return text(
    d,
    "Summary",
    text(
      d,
      "title",
      text(
        d,
        "Output",
        Object.keys(d).length ? human(entry.name) : entry.content,
      ),
    ),
  )
    .replace(/\s+/g, " ")
    .slice(0, 240);
}
export function teamState(run: Run, events: Entry[], handoffs: Entry[]) {
  const selected = run.selectedAgents ?? [];
  const currentHandoffs = handoffs.filter(
    (h) =>
      (!h.metadata?.taskIndex ||
        h.metadata.taskIndex === String(run.taskIndex ?? 0)) &&
      (!h.metadata?.iteration ||
        h.metadata.iteration === String(run.iteration)),
  );
  const completed = new Set(
    currentHandoffs.filter((h) => h.status === "Completed").map((h) => h.name),
  );
  const correctionTargets = (run.reworkPlan ?? [])
    .filter((p) => p.iteration === run.iteration)
    .map((p) => roleName(p.targetAgent));
  if (correctionTargets.length) {
    const revisited = new Set([
      ...correctionTargets,
      "TestEngineer",
      "QAEngineer",
      "CodeReviewer",
    ]);
    handoffs
      .filter(
        (h) =>
          h.status === "Completed" &&
          h.metadata?.taskIndex === String(run.taskIndex ?? 0) &&
          !revisited.has(h.name),
      )
      .forEach((h) => completed.add(h.name));
  }
  const sorted = events
    .filter(
      (e) =>
        (!e.metadata?.taskIndex ||
          e.metadata.taskIndex === String(run.taskIndex ?? 0)) &&
        (!e.metadata?.iteration ||
          e.metadata.iteration === String(run.iteration)),
    )
    .sort((a, b) => b.sequence - a.sequence);
  const planned = sorted.find(
    (e) =>
      e.name === "AgentsSelected" &&
      Number(field(parse(e.content), "TaskIndex")) === (run.taskIndex ?? 0),
  );
  const start = sorted.find(
    (e) =>
      e.name === "AgentStarted" && (!planned || e.sequence > planned.sequence),
  );
  const stageRole: Record<string, string> = {
    Analyzing: "BusinessAnalyst",
    Planning: "Architect",
    Implementing:
      run.reworkPlan
        ?.filter((p) => p.iteration === run.iteration)
        .map((p) => roleName(p.targetAgent))
        .find((r) => !completed.has(r)) ??
      selected.find((r) => /Engineer/.test(r) && !/Test|QA/.test(r)) ??
      "BackendEngineer",
    GeneratingTests: "TestEngineer",
    RunningTests: "TestEngineer",
    Validating: "QAEngineer",
    Reviewing: "CodeReviewer",
    PreparingDelivery: "CodeReviewer",
  };
  const active =
    start && selected.includes(start.content) && !completed.has(start.content)
      ? start.content
      : run.currentStage === "Failed" || run.currentStage === "Paused"
        ? stageRole[run.resumeStage]
        : currentHandoffs.find(
            (h) => h.status === "Working" && !completed.has(h.name),
          )?.name;
  const tool = sorted.find(
    (e) =>
      e.name === "ToolInvocationStarted" ||
      e.name === "ToolInvocationCompleted",
  );
  return selected.map((role) => ({
    role,
    state:
      run.currentStage === "Failed" && role === active
        ? "Failed"
        : run.currentStage === "Paused" && role === active
          ? "Blocked"
          : run.currentStage === "AwaitingHumanApproval" &&
              role === active &&
              !completed.has(role)
            ? "Blocked"
            : completed.has(role)
              ? "Completed"
              : role === active && !terminal.has(run.currentStage)
                ? tool?.name === "ToolInvocationStarted"
                  ? "Waiting for tool"
                  : "Working"
                : run.currentStage === "Pending"
                  ? "Queued"
                  : run.currentStage === "Cancelled" ||
                      run.currentStage === "Completed"
                    ? "Idle"
                    : "Queued",
  }));
}
export function githubUrl(value: unknown): string | undefined {
  if (typeof value !== "string") return;
  try {
    const u = new URL(value);
    return u.protocol === "https:" && u.hostname === "github.com"
      ? u.href
      : undefined;
  } catch {
    return;
  }
}
