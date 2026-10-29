import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { App } from "antd";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { AgentFlow } from "../src/features/executions/AgentFlow";
import { AgentConsole } from "../src/features/executions/AgentConsole";
import { ExecutionCapacity } from "../src/features/executions/ExecutionCapacity";
import type { Entry, Run } from "../src/types";
const run = {
  projectId: "p",
  resumeStage: "Implementing",
  status: "Implementing",
  retryCount: 0,
  startedAt: "2026-10-29T12:00:00Z",
  cost: 0,
  tokenUsage: 0,
  version: 1,
  id: "r",
  taskIndex: 0,
  iteration: 2,
  currentStage: "Implementing",
  selectedAgents: [
    "BackendEngineer",
    "FrontendEngineer",
    "TestEngineer",
    "QAEngineer",
    "CodeReviewer",
  ],
  tasks: [],
  policy: {
    maxToolCalls: 300,
    maxIterations: 10,
    maxRetries: 3,
    maxDurationMinutes: 180,
    maxBudgetUsd: 10,
    requireHumanApprovalForMerge: true,
    reviewBeforeCommit: true,
  },
  toolCalls: 285,
  reworkPlan: [
    {
      sourceAgent: "QAEngineer",
      targetAgent: "FrontendEngineer",
      reason: "Button inaccessible",
      findingIds: ["artifact#/Checks/0"],
      iteration: 2,
      taskId: "r/task/0",
      timestamp: "2026-10-29T12:00:00Z",
    },
  ],
} as Run;
const wrap = (element: React.ReactNode) =>
  render(
    <QueryClientProvider
      client={
        new QueryClient({ defaultOptions: { queries: { retry: false } } })
      }
    >
      <App>{element}</App>
    </QueryClientProvider>,
  );
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
});
it("draws only persisted rework routes and opens the responsible developer", () => {
  const select = vi.fn();
  const { container, rerender } = render(
    <AgentFlow run={run} events={[]} handoffs={[]} onSelect={select} />,
  );
  expect(container.querySelectorAll(".flow-rework")).toHaveLength(1);
  expect(
    screen.getByText(/QA Engineer → Frontend Engineer: Button inaccessible/),
  ).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: /Frontend Engineer/ }));
  expect(select).toHaveBeenCalledWith("FrontendEngineer");
  rerender(
    <AgentFlow
      run={{ ...run, reworkPlan: [] }}
      events={[]}
      handoffs={[]}
      onSelect={select}
    />,
  );
  expect(container.querySelectorAll(".flow-rework")).toHaveLength(0);
});
it("failed execution has no animated working nodes or transitions", () => {
  const { container } = render(
    <AgentFlow
      run={{ ...run, currentStage: "Failed", resumeStage: "Implementing" }}
      events={[]}
      handoffs={[]}
      onSelect={() => {}}
    />,
  );
  expect(
    container.querySelectorAll(".flow-active, .flow-node-active"),
  ).toHaveLength(0);
});
it("warns at 95 percent and renders the persisted usage breakdown", () => {
  wrap(
    <ExecutionCapacity
      run={{
        ...run,
        usage: [
          {
            agent: "BackendEngineer",
            taskIndex: 0,
            modelRequests: 20,
            toolInvocations: 285,
            failedToolInvocations: 4,
            retriedToolInvocations: 2,
          },
        ],
      }}
    />,
  );
  expect(screen.getByText(/95% tool allowance/)).toBeInTheDocument();
  expect(screen.getByText("Model requests")).toBeInTheDocument();
  expect(screen.getByText("Retried tools")).toBeInTheDocument();
});
it("shows actual bounded tool output, revision and profile without arbitrary command controls", async () => {
  const entry = {
    projectId: "p",
    sequence: 1,
    id: "tool1",
    runId: "r",
    name: "RunTests",
    kind: "tool",
    status: "Failed",
    metadata: {
      agent: "FrontendEngineer",
      taskIndex: "0",
      profile: "frontend",
    },
    createdAt: "2026-10-29T12:00:00Z",
    content: JSON.stringify({
      Success: false,
      Output: "output\n".repeat(20000),
      ExitCode: 1,
      EvidenceId: "real-evidence",
      WorkspaceRevision: "real-revision",
    }),
  } as Entry;
  vi.stubGlobal(
    "fetch",
    vi.fn(
      async (url: string) =>
        new Response(
          JSON.stringify(
            url.includes("details")
              ? { entry }
              : { items: [entry], hasMore: false },
          ),
          { status: 200, headers: { "Content-Type": "application/json" } },
        ),
    ),
  );
  const { container } = wrap(
    <AgentConsole run={run} role="FrontendEngineer" handoffs={[]} />,
  );
  fireEvent.click(await screen.findByRole("button", { name: "RunTests" }));
  await waitFor(() =>
    expect(screen.getByText(/real-evidence/)).toBeInTheDocument(),
  );
  expect(
    container.querySelector("pre")!.textContent!.length,
  ).toBeLessThanOrEqual(8000);
  expect(screen.getByRole("button", { name: "Next output" })).toBeEnabled();
  expect(screen.getByText(/authorized profile frontend/)).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: "Run command" }),
  ).not.toBeInTheDocument();
  vi.unstubAllGlobals();
});
