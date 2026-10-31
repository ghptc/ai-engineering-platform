import type { Entry, Run } from "../../types";
import { WorkflowGraph } from "./WorkflowGraph";
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
  return (
    <WorkflowGraph
      run={run}
      events={events}
      handoffs={handoffs}
      onSelect={(id) => {
        const role =
          (
            {
              planning: "BusinessAnalyst",
              architecture: "Architect",
              tests: "TestEngineer",
              qa: "QAEngineer",
              review: "CodeReviewer",
              pr: "CodeReviewer",
            } as Record<string, string>
          )[id] ?? id;
        if (run.selectedAgents?.includes(role)) onSelect(role);
      }}
    />
  );
}
