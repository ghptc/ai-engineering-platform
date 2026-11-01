import type { Entry, Run } from "../../types";
import { field, human, parse, roleName, text } from "../../lib/engineering";

export const stages = [
  {
    id: "planning",
    title: "Planning",
    detail: "Business analysis",
    x: 24,
    y: 42,
  },
  {
    id: "architecture",
    title: "Architecture",
    detail: "Decisions & context",
    x: 250,
    y: 42,
  },
  {
    id: "implementation",
    title: "Implementation",
    detail: "Selected specialists",
    x: 476,
    y: 42,
  },
  {
    id: "build",
    title: "Build",
    detail: "Recorded build commands",
    x: 702,
    y: 42,
  },
  {
    id: "tests",
    title: "Tests",
    detail: "Test authoring & execution",
    x: 702,
    y: 174,
  },
  { id: "qa", title: "QA", detail: "Acceptance validation", x: 476, y: 174 },
  {
    id: "review",
    title: "Code Review",
    detail: "Actual diff review",
    x: 250,
    y: 174,
  },
  {
    id: "pr",
    title: "PR Preparation",
    detail: "Commit / push / draft PR",
    x: 250,
    y: 306,
  },
  {
    id: "approval",
    title: "Human Approval",
    detail: "Owner delivery decision",
    x: 24,
    y: 306,
  },
  {
    id: "delivery",
    title: "Delivery",
    detail: "Run outcome & delivery evidence",
    x: 476,
    y: 306,
  },
  {
    id: "control",
    title: "Control / Recovery",
    detail: "Pause, failure & explicit retry",
    x: 702,
    y: 306,
  },
] as const;
export type StageId = (typeof stages)[number]["id"];
const stageMap: Record<string, StageId> = {
  Analyzing: "planning",
  Planning: "architecture",
  PreparingContext: "architecture",
  Implementing: "implementation",
  GeneratingTests: "tests",
  RunningTests: "tests",
  Validating: "qa",
  Reviewing: "review",
  AwaitingHumanApproval: "approval",
  PreparingDelivery: "pr",
  Completed: "delivery",
  Paused: "control",
  Failed: "control",
  Cancelled: "control",
};
export const stageId = (stage: string) => stageMap[stage];
export function agentStage(role: string): StageId | undefined {
  return (
    {
      BusinessAnalyst: "planning",
      Architect: "architecture",
      BackendEngineer: "implementation",
      FrontendEngineer: "implementation",
      TestEngineer: "tests",
      QAEngineer: "qa",
      CodeReviewer: "review",
      SecurityEngineer: "review",
      InfrastructureEngineer: "implementation",
    } as Record<string, StageId>
  )[role];
}
export const ordered = (items: Entry[]) =>
  [...new Map(items.map((e) => [e.id, e])).values()].sort(
    (a, b) => a.sequence - b.sequence || a.id.localeCompare(b.id),
  );
export function classification(entry: Entry) {
  if (entry.kind === "tool") return "Tool execution";
  if (entry.kind === "message" || entry.kind === "handoff")
    return "Model-authored summary";
  if (
    /Failed|Blocked|Rejected|Exceeded/.test(entry.name) ||
    /Failed|Blocked/.test(entry.status)
  )
    return "Error diagnostics";
  if (entry.kind === "artifact") {
    const data = parse(entry.content);
    return entry.name === "TestReport" &&
      field(data, "Success") === true &&
      text(data, "EvidenceId") &&
      text(data, "WorkspaceRevision")
      ? "Verified evidence"
      : "Evidence artifact";
  }
  return "System event";
}
export function operationalStatus(e: Entry) {
  if (e.kind === "tool") return e.status || "Recorded";
  if (/Failed|Rejected/.test(e.name)) return "Failed";
  if (/Blocked|Exceeded/.test(e.name)) return "Blocked";
  if (e.name === "ToolInvocationStarted") return "Tool Execution";
  if (e.name === "AgentStarted") return "Running";
  if (/Completed/.test(e.name)) return "Result";
  if (e.name === "RunQueued") return "Scheduled";
  if (e.name === "StageChanged" && /^(Failed|Paused) → /.test(e.content))
    return e.content.startsWith("Failed") ? "Retry" : "Resumed";
  if (e.kind === "handoff") return "Next Action";
  return e.status || "Recorded";
}
export function attributedAgent(e: Entry) {
  if (e.metadata?.agent) return roleName(e.metadata.agent);
  if (["message", "handoff"].includes(e.kind)) return e.name;
  if (e.name === "AgentStarted") return e.content;
  return "";
}
export interface PathRecord {
  id: string;
  source: StageId;
  target: StageId;
  sourceAgent?: string;
  targetAgent?: string;
  label: string;
  iteration?: number;
  timestamp: string;
  rework: boolean;
  entry?: Entry;
}
export function recordedPaths(run: Run, events: Entry[]): PathRecord[] {
  const paths: PathRecord[] = [];
  for (const event of ordered(events)) {
    if (event.name !== "StageChanged") continue;
    const [from, to] = event.content.split(" → ");
    const source = stageId(from),
      target = stageId(to);
    if (source && target && source !== target)
      paths.push({
        id: event.id,
        source,
        target,
        label: `${human(from)} → ${human(to)}`,
        timestamp: event.createdAt,
        rework:
          to === "Implementing" &&
          [
            "Validating",
            "Reviewing",
            "RunningTests",
            "PreparingDelivery",
          ].includes(from),
        entry: event,
      });
  }
  (run.agentTransitions ?? [])
    .filter((t) => t.taskIndex === run.taskIndex)
    .forEach((t, i) => {
      const source = agentStage(roleName(t.sourceAgent)),
        target = agentStage(roleName(t.targetAgent));
      if (source && target)
        paths.push({
          id: `handoff-${i}`,
          source,
          target,
          sourceAgent: roleName(t.sourceAgent),
          targetAgent: roleName(t.targetAgent),
          iteration: t.iteration,
          timestamp: t.timestamp,
          rework: false,
          label: `${human(roleName(t.sourceAgent))} → ${human(roleName(t.targetAgent))}`,
        });
    });
  (run.reworkPlan ?? [])
    .filter((p) => !p.taskId || p.taskId === `${run.id}/task/${run.taskIndex}`)
    .forEach((p, i) => {
      const source = agentStage(roleName(p.sourceAgent)),
        target = agentStage(roleName(p.targetAgent));
      if (source && target)
        paths.push({
          id: `rework-${i}`,
          source,
          target,
          sourceAgent: roleName(p.sourceAgent),
          targetAgent: roleName(p.targetAgent),
          iteration: p.iteration,
          timestamp: p.timestamp,
          rework: true,
          label: `${human(roleName(p.sourceAgent))} → ${human(roleName(p.targetAgent))}: ${p.reason}`,
        });
    });
  return paths.sort(
    (a, b) => Date.parse(a.timestamp) - Date.parse(b.timestamp),
  );
}
export function nodeStatus(id: StageId, run: Run, entries: Entry[]) {
  if (
    id === "control" &&
    ["Paused", "Failed", "Cancelled"].includes(run.currentStage)
  )
    return run.currentStage;
  const active = stageId(
    ["Paused", "Failed", "Cancelled"].includes(run.currentStage)
      ? run.resumeStage
      : run.currentStage,
  );
  if (active === id) {
    if (id === "delivery" && run.currentStage === "Completed")
      return "Completed · run state";
    if (run.currentStage === "Paused")
      return run.failureReason ? "Blocked" : "Paused";
    if (["Failed", "Cancelled", "Completed"].includes(run.currentStage))
      return run.currentStage;
    return id === "approval" ? "Awaiting approval" : "Running";
  }
  const tools = ordered(
    entries.filter(
      (e) =>
        e.kind === "tool" &&
        (e.metadata?.taskIndex == null ||
          e.metadata.taskIndex === String(run.taskIndex)) &&
        (id === "build"
          ? e.name === "RunCommand" && e.metadata?.profile === "build"
          : stageId(e.metadata?.stage) === id),
    ),
  );
  if (tools.length) {
    const last = tools.at(-1)!;
    return `${last.status || "Recorded"}${last.metadata?.iteration && last.metadata.iteration !== String(run.iteration) ? ` · iteration ${last.metadata.iteration}` : ""}`;
  }
  if (
    entries.some(
      (e) =>
        e.name === "StageChanged" && stageId(e.content.split(" → ")[0]) === id,
    )
  )
    return "Transition recorded";
  if (
    (run.agentTransitions ?? []).some(
      (t) =>
        t.taskIndex === run.taskIndex &&
        agentStage(roleName(t.sourceAgent)) === id,
    )
  )
    return "Handoff recorded";
  return "Not observed";
}
export function duration(entry: Entry) {
  const start = entry.metadata?.startedAt,
    end = entry.metadata?.completedAt;
  if (!start || !end) return undefined;
  const ms = Date.parse(end) - Date.parse(start);
  return Number.isFinite(ms) && ms >= 0 ? ms : undefined;
}
export const elapsed = (ms: number) =>
  ms < 1000
    ? `${ms} ms`
    : ms < 60000
      ? `${(ms / 1000).toFixed(1)} s`
      : `${(ms / 60000).toFixed(1)} min`;
export function stageInterval(entry: Entry, entries: Entry[]) {
  if (entry.name !== "StageChanged") return undefined;
  const stage = entry.content.split(" → ")[1];
  const end = ordered(entries).find(
    (e) => e.sequence > entry.sequence && e.name === "StageChanged",
  );
  if (!stage || !end || end.content.split(" → ")[0] !== stage) return undefined;
  const ms = Date.parse(end.createdAt) - Date.parse(entry.createdAt);
  return Number.isFinite(ms) && ms >= 0
    ? {
        ms,
        stage,
        waiting: ["Paused", "AwaitingHumanApproval"].includes(stage),
        end: end.createdAt,
      }
    : undefined;
}
export function operationIdentity(e: Entry) {
  return e.metadata?.operationId || e.id;
}
export function progressSummary(e: Entry) {
  const data = parse(e.content);
  return (
    text(data, "Summary") ||
    text(data, "CompletedWork") ||
    (Object.keys(data).length ? "No progress summary recorded." : e.content)
  );
}
export function resultEvidence(e: Entry) {
  const data = parse(e.content);
  return {
    id: text(data, "EvidenceId"),
    revision: text(data, "WorkspaceRevision"),
    success: field(data, "Success") === true,
  };
}
