import {
  act,
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { App } from "antd";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import fixture from "./fixtures/devpulse-persisted.json";
import type { Entry, Run } from "../src/types";
import { WorkflowGraph } from "../src/features/executions/WorkflowGraph";
import {
  ExecutionHistory,
  OperationDetails,
  safeArguments,
} from "../src/features/executions/ExecutionHistory";
import {
  classification,
  duration,
  nodeStatus,
  operationalStatus,
  ordered,
  recordedPaths,
  stageInterval,
} from "../src/features/executions/observability";

const run = fixture.run as unknown as Run;
const tools = fixture.tools as unknown as Entry[];
const events = fixture.events as unknown as Entry[];
const item = (
  id: string,
  sequence: number,
  extra: Partial<Entry> = {},
): Entry => ({ ...tools[0], id, sequence, ...extra });
function mount(
  node: React.ReactNode,
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  return {
    ...render(
      <QueryClientProvider client={client}>
        <App>{node}</App>
      </QueryClientProvider>,
    ),
    client,
  };
}
function respond(data: unknown) {
  return Promise.resolve(
    new Response(JSON.stringify(data), {
      headers: { "content-type": "application/json" },
    }),
  );
}
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.restoreAllMocks();
});

it("uses real DevPulse identities and never turns missing build/test evidence into completion", () => {
  expect(run.id).toBe("b09188be6e3743c797092d1e73ededd6");
  expect(nodeStatus("implementation", run, events)).toBe("Blocked");
  expect(nodeStatus("build", run, tools)).toBe("Not observed");
  expect(nodeStatus("tests", run, tools)).toBe("Not observed");
  expect(
    recordedPaths(run, events).every(
      (p) => p.entry || p.id.startsWith("handoff-"),
    ),
  ).toBe(true);
  expect(classification(fixture.handoffs[0] as unknown as Entry)).toBe(
    "Model-authored summary",
  );
});
it("draws directed persisted rework and historical paths; supports keyboard selection and zoom", () => {
  const select = vi.fn();
  const rework = {
    sourceAgent: "QAEngineer",
    targetAgent: "FrontendEngineer",
    reason: "Accessibility failed",
    findingIds: ["check-1"],
    taskId: `${run.id}/task/0`,
    iteration: 2,
    timestamp: "2026-10-30T23:50:00Z",
  };
  const r = { ...run, iteration: 2, reworkPlan: [rework] };
  const { container } = render(
    <WorkflowGraph run={r} events={[]} onSelect={select} />,
  );
  expect(container.querySelectorAll(".flow-rework")).toHaveLength(1);
  expect(
    screen.getByText(/QA Engineer → Frontend Engineer: Accessibility failed/),
  ).toBeInTheDocument();
  fireEvent.keyDown(
    screen.getByRole("button", { name: /Frontend Engineer ·/ }),
    { key: "Enter" },
  );
  expect(select).toHaveBeenCalledWith("FrontendEngineer");
  fireEvent.click(screen.getByRole("button", { name: "Zoom in" }));
  expect(screen.getByText("115%")).toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: /Inspect path records/ }));
  expect(
    screen.getAllByText(/QA Engineer → Frontend Engineer: Accessibility failed/)
      .length,
  ).toBeGreaterThan(1);
});
it("an empty history draws no invented transition or concurrency", () => {
  const { container } = render(
    <WorkflowGraph
      run={{ ...run, agentTransitions: [], reworkPlan: [] }}
      events={[]}
    />,
  );
  expect(container.querySelectorAll(".pipeline-edge")).toHaveLength(0);
  fireEvent.click(screen.getByRole("button", { name: /Inspect path records/ }));
  expect(screen.getByText(/No transitions recorded/)).toBeInTheDocument();
  expect(screen.getByText(/no inferred concurrency/)).toBeInTheDocument();
});
it("maps live state, blocked/failed state and owner approval without assuming a build stage", () => {
  expect(
    nodeStatus("implementation", { ...run, currentStage: "Implementing" }, []),
  ).toBe("Running");
  expect(
    nodeStatus("implementation", { ...run, currentStage: "Failed" }, []),
  ).toBe("Failed");
  expect(
    nodeStatus(
      "approval",
      { ...run, currentStage: "AwaitingHumanApproval" },
      [],
    ),
  ).toBe("Awaiting approval");
  expect(
    nodeStatus("build", run, [
      item("build", 1, {
        name: "RunCommand",
        status: "Failed",
        metadata: { profile: "build" },
      }),
    ]),
  ).toBe("Failed");
});
it("keeps other tasks out of node state and distinguishes historical attempts and verified test receipts", () => {
  const historical = item("build-previous", 1, {
    name: "RunCommand",
    status: "Failed",
    metadata: { profile: "build", taskIndex: "0", iteration: "1" },
  });
  expect(nodeStatus("build", { ...run, taskIndex: 1 }, [historical])).toBe(
    "Not observed",
  );
  expect(nodeStatus("build", { ...run, iteration: 2 }, [historical])).toBe(
    "Failed · iteration 1",
  );
  const report = item("report", 2, {
    kind: "artifact",
    name: "TestReport",
    content: JSON.stringify({
      Success: true,
      EvidenceId: "receipt",
      WorkspaceRevision: "revision",
    }),
  });
  expect(classification(report)).toBe("Verified evidence");
  expect(
    classification({ ...report, content: JSON.stringify({ Success: true }) }),
  ).toBe("Evidence artifact");
});
it("keeps stable sequence order, identity deduplication and exact durations", () => {
  expect(
    ordered([item("b", 20), item("a", 10), item("a", 10)]).map((e) => e.id),
  ).toEqual(["a", "b"]);
  expect(
    duration(
      item("a", 1, {
        metadata: {
          startedAt: "2026-10-30T12:00:00Z",
          completedAt: "2026-10-30T12:00:03Z",
        },
      }),
    ),
  ).toBe(3000);
  expect(duration(item("a", 1, { metadata: {} }))).toBeUndefined();
});
it("shows only timestamp-proven waiting intervals and identifies retry/resume events", () => {
  const start = item("wait", 1, {
    kind: "event",
    name: "StageChanged",
    content: "Reviewing → AwaitingHumanApproval",
    createdAt: "2026-10-30T12:00:00Z",
  });
  const end = item("delivery", 2, {
    kind: "event",
    name: "StageChanged",
    content: "AwaitingHumanApproval → PreparingDelivery",
    createdAt: "2026-10-30T12:02:00Z",
  });
  expect(stageInterval(start, [end, start])).toMatchObject({
    ms: 120000,
    waiting: true,
  });
  expect(stageInterval(start, [start])).toBeUndefined();
  expect(operationalStatus({ ...end, content: "Failed → Implementing" })).toBe(
    "Retry",
  );
  expect(operationalStatus({ ...end, content: "Paused → Implementing" })).toBe(
    "Resumed",
  );
});
it("bounds argument previews and does not render source bodies or unknown secret fields", () => {
  const value = safeArguments({
    path: "backend/File.cs",
    content: "private source",
    token: "secret",
    patch: JSON.stringify({ path: "backend/Patch.cs", replacement: "hidden" }),
  });
  expect(value).toContain("backend/Patch.cs");
  expect(value).not.toContain("private source");
  expect(value).not.toContain("secret");
  expect(value).not.toContain("hidden");
  expect(safeArguments({ query: "x".repeat(100000) }).length).toBeLessThan(
    2100,
  );
});
it("renders actual command, separate stdout/stderr, exit status and bounded output folds", async () => {
  const entry = item("executed", 1, {
    name: "RunTests",
    status: "Failed",
    content: JSON.stringify({
      Success: false,
      Output: "combined",
      ExitCode: 1,
      EvidenceId: "test-receipt",
      WorkspaceRevision: "source-revision",
      Execution: {
        Command: ["dotnet", "test"],
        WorkingDirectory: "/workspace",
        Stdout: "actual stdout\n".repeat(2000),
        Stderr: "actual compiler failure",
      },
    }),
  });
  vi.stubGlobal(
    "fetch",
    vi.fn(() =>
      respond({ entry, tool: entry, toolInput: { profile: "backend" } }),
    ),
  );
  const { container } = mount(
    <OperationDetails entry={entry} runId={run.id} />,
  );
  expect(await screen.findByText("test-receipt")).toBeInTheDocument();
  expect(screen.getByText(/Exit 1/)).toBeInTheDocument();
  expect(screen.getByText(/"dotnet"/)).toBeInTheDocument();
  fireEvent.click(screen.getByText("Stdout", { exact: true }));
  await waitFor(() =>
    expect(container.querySelectorAll("pre")[2].textContent).toContain(
      "actual stdout",
    ),
  );
  expect(container.querySelectorAll("pre")[2].textContent!.length).toBeLessThan(
    9000,
  );
  expect(screen.getByRole("button", { name: "Next output" })).toBeEnabled();
  fireEvent.click(screen.getByText("Stderr", { exact: true }));
  expect(screen.getByText(/actual compiler failure/)).toBeInTheDocument();
  expect(
    screen.queryByText("Successful test evidence"),
  ).not.toBeInTheDocument();
});
it("bounds a large history to 50, searches and paginates with exact server cursors", async () => {
  const rows = Array.from({ length: 1000 }, (_, i) =>
    item(`operation-${i}`, i + 1),
  );
  const fetcher = vi.fn((url: string) =>
    respond(
      url.includes("before=951")
        ? {
            items: rows.slice(900, 950).reverse(),
            hasMore: true,
            nextBefore: 901,
          }
        : { items: rows.slice(950).reverse(), hasMore: true, nextBefore: 951 },
    ),
  );
  vi.stubGlobal("fetch", fetcher);
  const { container } = mount(<ExecutionHistory run={run} />);
  await waitFor(() =>
    expect(container.querySelectorAll(".history-record")).toHaveLength(50),
  );
  expect(
    [...container.querySelectorAll(".history-record")].map((e) =>
      Number(e.getAttribute("data-sequence")),
    ),
  ).toEqual(Array.from({ length: 50 }, (_, i) => 951 + i));
  fireEvent.change(screen.getByLabelText("Search execution history"), {
    target: { value: "operation-999" },
  });
  expect(container.querySelectorAll(".history-record")).toHaveLength(1);
  fireEvent.change(screen.getByLabelText("Search execution history"), {
    target: { value: "" },
  });
  fireEvent.click(screen.getByRole("button", { name: "Older activity" }));
  await waitFor(() =>
    expect(fetcher).toHaveBeenCalledWith(
      expect.stringContaining("before=951"),
      expect.anything(),
    ),
  );
  expect(
    await screen.findByText(/Inspecting older activity/),
  ).toBeInTheDocument();
});
it("live cache updates follow sequence order but pause-follow freezes the current window", async () => {
  const key = [`/runs/${run.id}/stream?kind=tool&limit=50`];
  const first = item("first", 1);
  vi.stubGlobal(
    "fetch",
    vi.fn(() => respond({ items: [first], hasMore: false })),
  );
  const { client, container } = mount(<ExecutionHistory run={run} />);
  await waitFor(() =>
    expect(container.querySelectorAll(".history-record")).toHaveLength(1),
  );
  fireEvent.click(screen.getByRole("button", { name: "Pause-follow" }));
  act(() =>
    client.setQueryData(key, {
      items: [item("next", 2), first],
      hasMore: false,
    }),
  );
  expect(container.querySelectorAll(".history-record")).toHaveLength(1);
  fireEvent.click(screen.getByRole("button", { name: "Follow latest" }));
  expect(container.querySelectorAll(".history-record")).toHaveLength(2);
  fireEvent.click(screen.getByText("Detailed", { exact: true }));
  expect(screen.getByText(/#2 · next/)).toBeInTheDocument();
});
it("shows an honest empty source instead of planned tool results", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(() => respond({ items: [], hasMore: false })),
  );
  mount(<ExecutionHistory run={run} />);
  expect(
    await screen.findByText(/No persisted activity for this source/),
  ).toBeInTheDocument();
  expect(screen.queryByText("Tool succeeded")).not.toBeInTheDocument();
});
it("filters persisted failures and retry markers without treating all tools as diagnostics", async () => {
  const rows = [
    item("failed", 1, {
      status: "Failed",
      metadata: {
        retry: "true",
        agent: "BackendEngineer",
        stage: "Implementing",
      },
    }),
    item("success", 2, {
      status: "Succeeded",
      metadata: {
        retry: "false",
        agent: "BackendEngineer",
        stage: "Implementing",
      },
    }),
  ];
  vi.stubGlobal(
    "fetch",
    vi.fn(() => respond({ items: rows, hasMore: false })),
  );
  const { container } = mount(<ExecutionHistory run={run} />);
  await waitFor(() =>
    expect(container.querySelectorAll(".history-record")).toHaveLength(2),
  );
  expect(screen.getByText("Retry recorded")).toBeInTheDocument();
  fireEvent.mouseDown(screen.getByRole("combobox", { name: "History status" }));
  fireEvent.click(await screen.findByText("Errors / blocked"));
  expect(container.querySelectorAll(".history-record")).toHaveLength(1);
  expect(container.querySelector(".history-record")).toHaveAttribute(
    "data-sequence",
    "1",
  );
  expect(screen.getByText("Tool execution")).toBeInTheDocument();
});
it("progress summaries remain labelled as authored summaries without executing their tool plans", async () => {
  const entry = fixture.handoffs[0] as unknown as Entry;
  vi.stubGlobal(
    "fetch",
    vi.fn(() => respond({ items: [entry], hasMore: false })),
  );
  mount(<ExecutionHistory run={run} initialKind="handoff" />);
  expect(await screen.findByText("Model-authored summary")).toBeInTheDocument();
  expect(
    within(screen.getByLabelText("Persisted operational activity")).queryByText(
      "Tool execution",
    ),
  ).not.toBeInTheDocument();
});
