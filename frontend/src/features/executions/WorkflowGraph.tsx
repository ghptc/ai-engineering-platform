import { useEffect, useId, useRef, useState } from "react";
import { Button, Select, theme } from "antd";
import { human, teamState } from "../../lib/engineering";
import type { Entry, Run } from "../../types";
import { nodeStatus, recordedPaths, stages } from "./observability";

export function WorkflowGraph({
  run,
  events,
  tools = [],
  handoffs = [],
  onSelect,
}: {
  run: Run;
  events: Entry[];
  tools?: Entry[];
  handoffs?: Entry[];
  onSelect?: (id: string) => void;
}) {
  const { token } = theme.useToken();
  const marker = useId().replace(/:/g, "");
  const viewport = useRef<HTMLDivElement>(null);
  const drag = useRef<{
    x: number;
    y: number;
    left: number;
    top: number;
  } | null>(null);
  const [zoom, setZoom] = useState(1);
  const [iteration, setIteration] = useState("all");
  const [expanded, setExpanded] = useState(false);
  const paths = recordedPaths(run, events);
  const visible = paths
    .filter((p) => iteration === "all" || p.iteration === Number(iteration))
    .slice(-60);
  const iterations = [
    ...new Set(
      paths.flatMap((p) => (p.iteration == null ? [] : [p.iteration])),
    ),
  ].sort((a, b) => a - b);
  const team = teamState(run, events, handoffs);
  const specialists = team
    .filter((a) =>
      [
        "BackendEngineer",
        "FrontendEngineer",
        "InfrastructureEngineer",
      ].includes(a.role),
    )
    .map((a) => ({
      ...a,
      state:
        a.state === "Queued"
          ? "Selected"
          : a.state === "Blocked" &&
              run.currentStage === "Paused" &&
              !run.failureReason
            ? "Paused"
            : a.state,
    }));
  const specialistSpacing = specialists.length > 2 ? 20 : 25;
  const specialistHeight = specialists.length > 2 ? 18 : 22;
  const specialistCenter = 74 + specialistHeight / 2;
  const fit = () => {
    setZoom(
      Math.max(
        0.3,
        Math.min(
          1.6,
          (viewport.current?.clientWidth || 940) / 940,
          (viewport.current?.clientHeight || 410) / 410,
        ),
      ),
    );
    viewport.current?.scrollTo?.({ left: 0, top: 0 });
  };
  useEffect(() => {
    fit();
  }, []);
  return (
    <section className="pipeline-panel" aria-label="Engineering pipeline">
      <div className="pipeline-toolbar">
        <div>
          <strong>Engineering pipeline</strong>
          <span className="muted">
            {" "}
            Persisted paths · no inferred concurrency
          </span>
        </div>
        <div className="pipeline-controls">
          <Select
            size="small"
            aria-label="Workflow iteration"
            value={iteration}
            onChange={setIteration}
            options={[
              { value: "all", label: "All iterations" },
              ...iterations.map((i) => ({
                value: String(i),
                label: `Iteration ${i}`,
              })),
            ]}
          />
          <Button
            size="small"
            aria-label="Zoom out"
            onClick={() => setZoom((z) => Math.max(0.3, z - 0.15))}
          >
            −
          </Button>
          <span className="zoom-label">{Math.round(zoom * 100)}%</span>
          <Button
            size="small"
            aria-label="Zoom in"
            onClick={() => setZoom((z) => Math.min(2, z + 0.15))}
          >
            +
          </Button>
          <Button size="small" onClick={fit}>
            Fit to view
          </Button>
        </div>
      </div>
      <div
        className="pipeline-viewport"
        ref={viewport}
        tabIndex={0}
        role="region"
        aria-label="Live directed engineering workflow"
        onPointerDown={(e) => {
          if (
            e.button !== 0 ||
            (e.target as Element).closest('[role="button"]')
          )
            return;
          drag.current = {
            x: e.clientX,
            y: e.clientY,
            left: e.currentTarget.scrollLeft,
            top: e.currentTarget.scrollTop,
          };
          e.currentTarget.setPointerCapture?.(e.pointerId);
        }}
        onPointerMove={(e) => {
          if (drag.current) {
            e.currentTarget.scrollLeft =
              drag.current.left + drag.current.x - e.clientX;
            e.currentTarget.scrollTop =
              drag.current.top + drag.current.y - e.clientY;
          }
        }}
        onPointerUp={() => {
          drag.current = null;
        }}
        onPointerCancel={() => {
          drag.current = null;
        }}
      >
        <svg
          viewBox="0 0 940 410"
          style={{ width: 940 * zoom, height: 410 * zoom }}
          aria-label={`Engineering delivery lifecycle · ${human(run.currentStage)}`}
        >
          <defs>
            {[false, true].map((rework) => (
              <marker
                key={String(rework)}
                id={`${marker}${rework ? "-rework" : ""}`}
                viewBox="0 0 10 10"
                refX="9"
                refY="5"
                markerWidth="6"
                markerHeight="6"
                orient="auto-start-reverse"
              >
                <path
                  d="M0 0 L10 5 L0 10z"
                  fill={
                    rework ? token.colorWarningTextActive : token.colorPrimary
                  }
                />
              </marker>
            ))}
          </defs>
          {[
            "01 / DEFINE & IMPLEMENT",
            "02 / VERIFY",
            "03 / OWNER GATE & DELIVERY",
          ].map((label, i) => (
            <text
              key={label}
              className="pipeline-phase"
              x="24"
              y={22 + i * 132}
              fill={token.colorTextSecondary}
            >
              {label}
            </text>
          ))}
          {stages.map((n) => {
            const state = nodeStatus(n.id, run, [...events, ...tools]),
              active = state === "Running";
            const color = /Failed|Blocked/.test(state)
              ? token.colorErrorTextActive
              : /Paused|approval/.test(state)
                ? token.colorWarningTextActive
                : /Succeeded|Completed|recorded/.test(state)
                  ? token.colorSuccessTextActive
                  : active
                    ? token.colorPrimaryTextActive
                    : token.colorTextSecondary;
            return (
              <g
                key={n.id}
                transform={`translate(${n.x},${n.y})`}
                className={`pipeline-node ${active ? "flow-node-active" : ""}`}
                role="button"
                tabIndex={0}
                aria-label={`${n.title} · ${state}`}
                onClick={() => onSelect?.(n.id)}
                onKeyDown={(e) => {
                  if (e.key === "Enter" || e.key === " ") {
                    e.preventDefault();
                    onSelect?.(n.id);
                  }
                }}
              >
                <rect
                  width="200"
                  height={n.id === "implementation" ? 102 : 78}
                  rx="6"
                  fill={token.colorBgContainer}
                  stroke={color}
                  strokeWidth={active ? 2 : 1}
                />
                <circle cx="14" cy="18" r="3" fill={color} />
                <text
                  x="25"
                  y="22"
                  fill={token.colorText}
                  className="pipeline-title"
                >
                  {n.title}
                </text>
                {n.id !== "implementation" && (
                  <text
                    x="12"
                    y="42"
                    fill={token.colorTextSecondary}
                    className="pipeline-subtitle"
                  >
                    {n.detail}
                  </text>
                )}
                <text
                  x="12"
                  y={n.id === "implementation" ? 95 : 64}
                  fill={color}
                  className="pipeline-status"
                >
                  {state}
                </text>
              </g>
            );
          })}
          {visible
            .filter(
              (p) =>
                p.source !== p.target ||
                (p.sourceAgent &&
                  p.targetAgent &&
                  p.sourceAgent !== p.targetAgent),
            )
            .map((p, i) => {
              const from = stages.find((n) => n.id === p.source)!,
                to = stages.find((n) => n.id === p.target)!;
              const specialistIndex = specialists.findIndex(
                (a) => a.role === p.targetAgent,
              );
              const sx = from.x + 100,
                sy = from.y + (from.id === "implementation" ? 102 : 78),
                tx = p.rework && specialistIndex >= 0 ? to.x + 8 : to.x + 100,
                ty =
                  p.rework && specialistIndex >= 0
                    ? specialistCenter + specialistIndex * specialistSpacing
                    : to.y + (to.id === "implementation" ? 102 : 78),
                lane = (i % 3) * 3;
              const d =
                p.source === p.target && from.id !== "implementation"
                  ? `M${from.x + 190} ${from.y + 52} C${from.x + 235} ${from.y + 30},${from.x + 235} ${from.y + 85},${from.x + 190} ${from.y + 66}`
                  : p.source === p.target
                    ? `M672 ${
                        specialistCenter +
                        Math.max(
                          0,
                          specialists.findIndex(
                            (a) => a.role === p.sourceAgent,
                          ),
                        ) *
                          specialistSpacing
                      } V${specialistCenter + Math.max(0, specialistIndex) * specialistSpacing} H668`
                    : from.y === to.y
                      ? `M${sx} ${sy} C${sx} ${sy + 35 + lane},${tx} ${ty + 35 + lane},${tx} ${ty}`
                      : `M${sx} ${sy} C${sx} ${sy + 35 + lane},${tx} ${ty - 35 - lane},${tx} ${ty}`;
              return (
                <path
                  key={p.id}
                  d={d}
                  className={`pipeline-edge ${p.rework ? "flow-rework" : ""}`}
                  stroke={
                    p.rework ? token.colorWarningTextActive : token.colorPrimary
                  }
                  markerEnd={`url(#${marker}${p.rework ? "-rework" : ""})`}
                  opacity={
                    p.iteration != null && p.iteration !== run.iteration
                      ? 0.4
                      : 0.85
                  }
                >
                  <title>
                    {p.label} ·{" "}
                    {p.iteration != null
                      ? `iteration ${p.iteration}`
                      : "stage event"}{" "}
                    · {p.timestamp}
                  </title>
                </path>
              );
            })}
          {specialists.map((a, i) => (
            <g
              key={a.role}
              transform={`translate(484,${74 + i * specialistSpacing})`}
              className="pipeline-node"
              role="button"
              tabIndex={0}
              aria-label={`${human(a.role)} · ${a.state}`}
              onClick={() => onSelect?.(a.role)}
              onKeyDown={(e) => {
                if (e.key === "Enter" || e.key === " ") {
                  e.preventDefault();
                  onSelect?.(a.role);
                }
              }}
            >
              <rect
                width="184"
                height={specialistHeight}
                rx="3"
                fill={token.colorFillAlter}
                stroke={token.colorBorder}
              />
              <text
                x="7"
                y={specialistHeight - 7}
                fill={token.colorText}
                className="pipeline-specialist"
              >
                {i + 1}. {human(a.role)}
              </text>
              <title>
                {a.state} · selected order; concurrency is not inferred
              </title>
            </g>
          ))}
          <text
            x="24"
            y="198"
            fill={token.colorTextSecondary}
            className="pipeline-subtitle"
          >
            Only persisted transitions create edges.
          </text>
          <text
            x="24"
            y="218"
            fill={token.colorTextSecondary}
            className="pipeline-subtitle"
          >
            Build is observed from a fixed-profile tool.
          </text>
          <text
            x="24"
            y="238"
            fill={token.colorTextSecondary}
            className="pipeline-subtitle"
          >
            Owner approval precedes delivery writes.
          </text>
        </svg>
      </div>
      <div className="pipeline-legend">
        <span>● Observed state</span>
        <span>→ Recorded transition</span>
        <span
          className="rework-key"
          style={{ color: token.colorWarningTextActive }}
        >
          ↶ Rework
        </span>
        <span>Not observed ≠ pending or complete</span>
        <Button
          type="text"
          size="small"
          aria-expanded={expanded}
          onClick={() => setExpanded(!expanded)}
        >
          {expanded ? "Hide" : "Inspect"} path records ({paths.length})
        </Button>
      </div>
      {expanded && (
        <div className="pipeline-paths">
          {visible.length ? (
            visible.map((p) => (
              <button key={p.id} onClick={() => onSelect?.(p.target)}>
                <time>{new Date(p.timestamp).toLocaleTimeString()}</time>
                <span>{p.label}</span>
                <small>
                  {p.iteration ? `Iteration ${p.iteration}` : `Event ${p.id}`}
                </small>
              </button>
            ))
          ) : (
            <p>
              No transitions recorded in the loaded event window. Stage
              placement is a lifecycle map, not evidence of execution.
            </p>
          )}
          {paths.length > 60 && (
            <p>Showing at most 60 paths. Original records remain in history.</p>
          )}
        </div>
      )}
    </section>
  );
}
