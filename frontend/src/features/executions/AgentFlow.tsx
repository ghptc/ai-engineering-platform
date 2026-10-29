import { human, roleName, teamState } from "../../lib/engineering";
import type { Entry, Run } from "../../types";

export function AgentFlow({
  run,
  events,
  handoffs,
  onSelect,
}: {
  run: Run;
  events: Entry[];
  handoffs: Entry[];
  onSelect: (role: string) => void;
}) {
  const team = teamState(run, events, handoffs);
  const plans = [...(run.reworkPlan ?? [])].reverse();
  const height = Math.max(250, 190 + plans.length * 35);
  const width = Math.max(260, team.length * 190);
  const x = (role: string) => team.findIndex((a) => a.role === role) * 190 + 95;
  return (
    <div
      className="agent-flow"
      role="region"
      aria-label="Live directed engineering workflow"
    >
      <svg
        viewBox={`0 0 ${width} ${height}`}
        style={{ minWidth: width, width, height, maxHeight: "none" }}
        role="img"
        aria-label={`Engineering flow · ${human(run.currentStage)}`}
      >
        <defs>
          <marker
            id={`arrow-${run.id}`}
            viewBox="0 0 10 10"
            refX="9"
            refY="5"
            markerWidth="7"
            markerHeight="7"
            orient="auto-start-reverse"
          >
            <path d="M 0 0 L 10 5 L 0 10 z" fill="currentColor" />
          </marker>
        </defs>
        {team.slice(1).map((a, i) => {
          const source = team[i];
          const completed = (run.agentTransitions ?? []).some(
            (t) =>
              roleName(t.sourceAgent) === source.role &&
              roleName(t.targetAgent) === a.role &&
              t.taskIndex === run.taskIndex &&
              t.iteration === run.iteration,
          );
          const active =
            completed && ["Working", "Waiting for tool"].includes(a.state);
          return (
            <path
              key={a.role}
              className={`flow-edge ${active ? "flow-active" : completed ? "flow-completed" : ""}`}
              d={`M ${x(source.role) + 77} 70 L ${x(a.role) - 77} 70`}
              markerEnd={`url(#arrow-${run.id})`}
            >
              <title>
                {human(source.role)} → {human(a.role)} ·{" "}
                {completed ? "handoff recorded" : "planned"}
              </title>
            </path>
          );
        })}
        {(run.agentTransitions ?? [])
          .filter(
            (t) =>
              t.taskIndex === run.taskIndex &&
              Math.abs(
                team.findIndex((a) => a.role === roleName(t.sourceAgent)) -
                  team.findIndex((a) => a.role === roleName(t.targetAgent)),
              ) > 1,
          )
          .map((t, i) => (
            <path
              key={`skip-${i}`}
              className={`flow-edge ${t.iteration === run.iteration && team.some((a) => a.role === roleName(t.targetAgent) && ["Working", "Waiting for tool"].includes(a.state)) ? "flow-active" : "flow-completed"}`}
              d={`M ${x(roleName(t.sourceAgent))} 30 V 12 H ${x(roleName(t.targetAgent))} V 30`}
              markerEnd={`url(#arrow-${run.id})`}
            >
              <title>
                {human(roleName(t.sourceAgent))} →{" "}
                {human(roleName(t.targetAgent))} · handoff recorded
              </title>
            </path>
          ))}
        {plans.map((p, i) => (
          <path
            key={`${p.targetAgent}-${i}`}
            className={`flow-edge flow-rework ${run.currentStage === "Implementing" && p.iteration === run.iteration && team.some((a) => a.role === roleName(p.targetAgent) && ["Working", "Waiting for tool"].includes(a.state)) ? "flow-active" : "flow-completed"}`}
            d={`M ${x(roleName(p.sourceAgent))} 105 V ${160 + i * 35} H ${x(roleName(p.targetAgent))} V 105`}
            markerEnd={`url(#arrow-${run.id})`}
          >
            <title>
              {human(roleName(p.sourceAgent))} →{" "}
              {human(roleName(p.targetAgent))}: {p.reason}
            </title>
          </path>
        ))}
        {team.map((a) => (
          <g
            key={a.role}
            transform={`translate(${x(a.role) - 78},30)`}
            role="button"
            tabIndex={0}
            aria-label={`${human(a.role)} · ${a.state}`}
            onClick={() => onSelect(a.role)}
            onKeyDown={(e) => {
              if (e.key === "Enter" || e.key === " ") {
                e.preventDefault();
                onSelect(a.role);
              }
            }}
            className={`flow-node ${["Working", "Waiting for tool"].includes(a.state) ? "flow-node-active" : ""}`}
          >
            <rect width="156" height="76" rx="10" />
            <text x="78" y="30" textAnchor="middle">
              {human(a.role)}
            </text>
            <text x="78" y="55" textAnchor="middle" className="flow-status">
              {a.state}
            </text>
          </g>
        ))}
      </svg>
    </div>
  );
}
