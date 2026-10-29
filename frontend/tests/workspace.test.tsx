import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App, ConfigProvider } from "antd";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { Dashboard } from "../src/app/Dashboard";
import { Markdown } from "../src/components/Readable";
import { ActivityFeed } from "../src/features/executions/ActivityFeed";
import { TeamWorkspace } from "../src/features/collaboration/Collaboration";
import { ExecutionDetail } from "../src/features/executions/Executions";
import { Usage } from "../src/features/usage/Usage";
import { mergeEntries, summary, teamState } from "../src/lib/engineering";
import type { Entry, Run } from "../src/types";
vi.mock("@microsoft/signalr", () => ({
  LogLevel: { None: 0 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return {
        on: vi.fn(),
        onreconnected: vi.fn(),
        onreconnecting: vi.fn(),
        onclose: vi.fn(),
        start: () => Promise.resolve(),
        stop: () => Promise.resolve(),
        invoke: () => Promise.resolve(0),
      };
    }
  },
}));
const run = {
  id: "r",
  projectId: "p",
  currentStage: "Implementing",
  resumeStage: "Implementing",
  iteration: 1,
  taskIndex: 0,
  tasks: [
    {
      title: "Answer fixture",
      description: "Change answer",
      acceptanceCriteria: ["Tests pass"],
    },
  ],
  selectedAgents: [
    "BackendEngineer",
    "TestEngineer",
    "QAEngineer",
    "CodeReviewer",
  ],
  toolCalls: 2,
  version: 4,
  cost: 0.1,
  tokenUsage: 999,
  policy: {
    maxIterations: 3,
    maxToolCalls: 30,
    maxDurationMinutes: 15,
    maxBudgetUsd: 1,
    maxRetries: 2,
  },
  startedAt: "2026-10-28T10:00:00Z",
} as Run;
const entry = (
  id: string,
  sequence: number,
  name = "Progress",
  content = "Saved progress",
) =>
  ({
    id,
    sequence,
    name,
    content,
    kind: "event",
    runId: "r",
    projectId: "p",
    status: "",
    metadata: {},
    createdAt: "2026-10-28T10:00:00Z",
  }) as Entry;
function mount(
  node: React.ReactNode,
  client = new QueryClient({ defaultOptions: { queries: { retry: false } } }),
) {
  const view = render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={["/executions/r"]}>
        <ConfigProvider wave={{ disabled: true }}>
          <App>{node}</App>
        </ConfigProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
  return { ...view, client };
}
function response(value: unknown) {
  return Promise.resolve(
    new Response(JSON.stringify(value), {
      headers: { "content-type": "application/json" },
    }),
  );
}
beforeEach(() => vi.stubGlobal("fetch", vi.fn()));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
describe("engineering workspace", () => {
  it("dashboard puts work and decisions first without financial metrics", async () => {
    vi.mocked(fetch).mockImplementation((url) =>
      response(
        String(url).includes("/stream")
          ? { items: [], hasMore: false }
          : String(url).includes("/runs")
            ? { items: [run] }
            : String(url).includes("/projects?")
              ? { items: [{ id: "p", name: "Fixture", status: "Active" }] }
              : String(url).includes("/settings")
                ? {
                    openAIConfigured: true,
                    githubConfigured: true,
                    sandboxReady: true,
                  }
                : String(url).includes("/activity")
                  ? []
                  : { items: [] },
      ),
    );
    mount(<Dashboard />);
    expect(await screen.findByText("Answer fixture")).toBeInTheDocument();
    expect(screen.getByText("Active engineering")).toBeInTheDocument();
    expect(screen.getByText("Action required")).toBeInTheDocument();
    expect(screen.getByText("Recent deliveries")).toBeInTheDocument();
    expect(
      screen.queryByText(
        /Estimated model cost|Tokens used|Model pricing|Total API cost/,
      ),
    ).not.toBeInTheDocument();
    expect(
      vi.mocked(fetch).mock.calls.some(([u]) => String(u).includes("/usage")),
    ).toBe(false);
  });
  it("orders, deduplicates and bounds 1000 events", () => {
    const events = Array.from({ length: 1000 }, (_, i) => entry("e" + i, i));
    const merged = mergeEntries(events, [events[8], entry("new", 1001)], 50);
    expect(merged).toHaveLength(50);
    expect(merged[0].id).toBe("new");
    expect(new Set(merged.map((e) => e.id)).size).toBe(50);
    expect(merged.at(-1)?.sequence).toBe(951);
  });
  it("derives selective working/tool and completed states from persisted records", () => {
    const start = entry("s", 1, "AgentStarted", "BackendEngineer");
    const tool = entry("t", 2, "ToolInvocationStarted", "ReadFile");
    const team = teamState(run, [start, tool], []);
    expect(team).toHaveLength(4);
    expect(team[0].state).toBe("Waiting for tool");
    expect(team.some((a) => a.role === "Architect")).toBe(false);
    expect(
      teamState({ ...run, currentStage: "Completed" }, [], []).every(
        (a) => a.state === "Idle",
      ),
    ).toBe(true);
  });
  it("presents a real handoff and chronological agent message without raw JSON", async () => {
    const handoff = {
      ...entry(
        "h",
        2,
        "BackendEngineer",
        JSON.stringify({
          Summary: "Changed the existing fixture",
          NextAction: "Run the updated test",
          RelevantArtifacts: ["diff-1"],
        }),
      ),
      kind: "handoff",
      status: "Completed",
      metadata: { taskIndex: "0", iteration: "1" },
    };
    const msg = {
      ...entry(
        "m",
        1,
        "BackendEngineer",
        JSON.stringify({ Summary: "Repository inspected", ToolCalls: [] }),
      ),
      kind: "message",
    };
    vi.mocked(fetch).mockImplementation((url) =>
      response({
        items: [String(url).includes("kind=handoff") ? handoff : msg],
        hasMore: false,
      }),
    );
    mount(<TeamWorkspace run={run} events={[]} />);
    expect(
      await screen.findByText("Backend Engineer → Test Engineer"),
    ).toBeInTheDocument();
    expect(screen.getByText("Repository inspected")).toBeInTheDocument();
    expect(screen.queryByText(/"ToolCalls"/)).not.toBeInTheDocument();
    expect(
      screen.getByLabelText("Selected engineering team").children,
    ).toHaveLength(4);
  });
  it("keeps activity to 50 rows, pauses updates and jumps to latest", async () => {
    const events = Array.from({ length: 1000 }, (_, i) =>
      entry("e" + i, 1000 - i, "Progress", "Step " + i),
    );
    vi.mocked(fetch).mockImplementation(() =>
      response({ items: events, hasMore: true, nextBefore: 951 }),
    );
    const { client, container } = mount(<ActivityFeed runId="r" />);
    expect(await screen.findByText("Step 0")).toBeInTheDocument();
    expect(container.querySelectorAll(".activity-row")).toHaveLength(50);
    const viewport = screen.getByTestId("activity-window");
    fireEvent.scroll(viewport, { target: { scrollTop: 80 } });
    expect(screen.getByText("Follow latest")).toBeInTheDocument();
    client.setQueryData(["/runs/r/stream?limit=50"], {
      items: [
        entry("new", 1001, "Progress", "Newest step"),
        ...events.slice(0, 49),
      ],
      hasMore: true,
      nextBefore: 952,
    });
    expect(screen.queryByText("Newest step")).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("Jump to Latest"));
    expect(await screen.findByText("Newest step")).toBeInTheDocument();
    expect(viewport.scrollTop).toBe(0);
    fireEvent.click(screen.getByText("Older activity"));
    await waitFor(() =>
      expect(
        vi
          .mocked(fetch)
          .mock.calls.some(([url]) => String(url).includes("before=")),
      ).toBe(true),
    );
  });
  it("opens a lazy large JSON Drawer with copy and controlled expansion", async () => {
    const event = entry(
      "big",
      1,
      "ContextInventory",
      JSON.stringify({ large: "x".repeat(100000) + "END_PAYLOAD" }),
    );
    vi.mocked(fetch).mockImplementation((url) =>
      response(
        String(url).includes("/details/")
          ? {
              entry: event,
              inputAvailability: "Input not recorded",
              artifacts: [],
            }
          : { items: [event], hasMore: false },
      ),
    );
    mount(<ActivityFeed runId="r" />);
    fireEvent.click(await screen.findByText("Context sources measured"));
    expect(await screen.findByRole("dialog")).toBeInTheDocument();
    expect(screen.queryByText(/END_PAYLOAD/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("Raw payload"));
    expect(screen.getByText("Copy complete payload")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /Payload · 1 fields/ }));
    expect(screen.queryByText(/END_PAYLOAD/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByText("Show complete value"));
    expect(screen.getByText(/END_PAYLOAD/)).toBeInTheDocument();
  });
  it("renders Markdown while removing executable markup and external images", () => {
    const { container } = mount(
      <Markdown
        content={
          '# Product objective\n\n**Important**\n\n<script>window.pwned=true</script><img src="https://bad.invalid/a" onerror="alert(1)">\n\n[unsafe](javascript:alert(1))'
        }
      />,
    );
    expect(
      screen.getByRole("heading", { name: "Product objective" }),
    ).toBeInTheDocument();
    expect(container.querySelector("strong")?.textContent).toBe("Important");
    expect(container.querySelector("script,img")).toBeNull();
    expect(container.innerHTML).not.toContain('href="javascript:');
  });
  it.each(["Failed", "Paused", "Completed", "Pending"])(
    "shows factual %s state and no spend dashboard",
    async (status) => {
      vi.mocked(fetch).mockImplementation((url) =>
        response(
          String(url).includes("/stream")
            ? { items: [], hasMore: false }
            : {
                ...run,
                currentStage: status,
                failureReason: status === "Failed" ? "Tool failed" : undefined,
              },
        ),
      );
      mount(
        <Routes>
          <Route path="/executions/:id" element={<ExecutionDetail />} />
        </Routes>,
      );
      expect(
        (await screen.findAllByText(status, { selector: ".ant-tag" })).length,
      ).toBeGreaterThan(0);
      expect(screen.queryByText("Estimated cost")).not.toBeInTheDocument();
      if (status === "Paused")
        expect(screen.getByText("Resume")).toBeInTheDocument();
      if (status === "Failed")
        expect(screen.getByText("Retry stage")).toBeInTheDocument();
    },
  );
  it("attaches human clarification through the authorized backend action", async () => {
    vi.mocked(fetch).mockImplementation((url, options) =>
      response(
        options?.method === "POST"
          ? {}
          : String(url).includes("/stream")
            ? { items: [], hasMore: false }
            : {
                ...run,
                currentStage: "Failed",
                failureReason: "Human clarification required: Which behavior?",
              },
      ),
    );
    mount(
      <Routes>
        <Route path="/executions/:id" element={<ExecutionDetail />} />
      </Routes>,
    );
    expect(await screen.findByText("Your input is needed")).toBeInTheDocument();
    fireEvent.change(
      screen.getByLabelText("Clarification for engineering team"),
      { target: { value: "Use the existing API" } },
    );
    fireEvent.click(screen.getByText("Send clarification & retry"));
    await waitFor(() =>
      expect(fetch).toHaveBeenCalledWith(
        "/api/v1/runs/r/clarification",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ content: "Use the existing API", version: 4 }),
        }),
      ),
    );
  });
  it("usage distinguishes recorded tokens, estimates and unavailable billing", async () => {
    const g = {
      key: "",
      inputTokens: 20,
      outputTokens: 10,
      estimatedCostUsd: 0.01,
      calls: 1,
    };
    vi.mocked(fetch).mockImplementation((url) =>
      response(
        String(url).includes("/usage/ledger")
          ? {
              total: g,
              projects: [],
              executions: [],
              agents: [],
              models: [],
              days: [],
              items: [],
              hasMore: false,
              costBasis: "Estimated from configured prices",
            }
          : String(url).endsWith("/agents")
            ? []
            : { items: [] },
      ),
    );
    mount(<Usage />);
    expect(
      await screen.findByText("Costs are estimates, not provider invoices"),
    ).toBeInTheDocument();
    expect(screen.getByText(/Unavailable — no billing/)).toBeInTheDocument();
    expect(screen.getByText("Input tokens · recorded")).toBeInTheDocument();
  });
  it("keeps raw context inventory out of summaries", () => {
    expect(
      summary(
        entry("i", 1, "ContextInventory", "[" + "large ".repeat(10000) + "]"),
      ),
    ).toBe("Context sources measured");
  });
});
