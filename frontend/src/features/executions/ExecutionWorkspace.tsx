import { useState } from "react";
import { Button, InputNumber, Tag } from "antd";
import { useApi } from "../../components/Common";
import type { RecordWindow } from "../../hooks/useLiveEvents";
import { human, parse, text } from "../../lib/engineering";
import type { Entry, Run } from "../../types";
import { AgentTasks } from "./AgentTasks";
import { WorkflowGraph } from "./WorkflowGraph";
import { ExecutionHistory } from "./ExecutionHistory";
import { ordered } from "./observability";

export function ExecutionWorkspace({
  run,
  events,
  handoffs,
}: {
  run: Run;
  events: Entry[];
  handoffs: Entry[];
}) {
  const [agent, setAgent] = useState("");
  const [stage, setStage] = useState("");
  const [height, setHeight] = useState(400);
  const [graphOpen, setGraphOpen] = useState(true);
  const tools = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=tool&limit=50`,
    true,
    5000,
  );
  const latest = ordered(
    handoffs.filter(
      (h) =>
        h.metadata?.taskIndex === String(run.taskIndex) &&
        h.metadata?.iteration === String(run.iteration),
    ),
  ).at(-1);
  const objective = latest ? text(parse(latest.content), "Summary") : "";
  return (
    <div className="execution-observatory">
      <div className="workspace-context">
        <div>
          <span className="terminal-section-label">Task objective</span>
          <strong>
            {run.tasks?.[run.taskIndex]?.title ?? "Engineering execution"}
          </strong>
        </div>
        <div className="workspace-objective">
          <Tag>{latest ? human(latest.name) : "Agent not attributed"}</Tag>
          <span className="muted">Model-authored progress summary</span>
          <p>
            {objective
              ? objective.slice(0, 350)
              : "No progress summary recorded for this task and iteration."}
          </p>
        </div>
      </div>
      <div className="workspace-layout-toolbar">
        <Button
          size="small"
          aria-expanded={graphOpen}
          onClick={() => setGraphOpen(!graphOpen)}
        >
          {graphOpen ? "Collapse" : "Expand"} pipeline
        </Button>
        <label>
          Canvas height{" "}
          <InputNumber
            size="small"
            aria-label="Pipeline canvas height"
            min={200}
            max={600}
            step={50}
            value={height}
            onChange={(v) => setHeight(Number(v) || 400)}
          />
        </label>
        <span className="muted">
          Read-only observability · no deployment or merge actions
        </span>
      </div>
      {graphOpen && (
        <div
          className="resizable-pipeline"
          style={{ height, minHeight: 200, maxHeight: 600 }}
        >
          <WorkflowGraph
            run={run}
            events={events}
            tools={tools.data?.items ?? []}
            handoffs={handoffs}
            onSelect={(id) => {
              if (id.endsWith("Engineer")) {
                setAgent(id);
                setStage("");
              } else {
                setStage(id);
                setAgent("");
              }
            }}
          />
        </div>
      )}
      {(agent || stage) && (
        <div className="selection-strip">
          <span>
            Inspecting{" "}
            {agent
              ? human(agent)
              : stage === "pr"
                ? "PR Preparation"
                : human(stage)}
          </span>
          <Button
            size="small"
            onClick={() => {
              setAgent("");
              setStage("");
            }}
          >
            Clear selection
          </Button>
        </div>
      )}
      <AgentTasks key={run.id} runId={run.id} agent={agent} />
      <ExecutionHistory
        run={run}
        selectedAgent={agent}
        selectedStage={stage}
        onSelectionClear={() => {
          setAgent("");
          setStage("");
        }}
      />
    </div>
  );
}
