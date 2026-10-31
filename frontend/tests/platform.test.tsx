import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { App, ConfigProvider } from "antd";
import { Projects } from "../src/features/projects/Projects";
import { Approvals } from "../src/features/reviews/Approvals";
import { ConversationPanel } from "../src/features/conversations/Conversations";
import { ExecutionDetail } from "../src/features/executions/Executions";
import { api, ApiError } from "../src/services/api";
vi.mock("../src/hooks/useLiveEvents", () => ({ useLiveEvents: () => {} }));
function wrapper(element: React.ReactNode) {
  return render(
    <QueryClientProvider
      client={
        new QueryClient({
          defaultOptions: { queries: { retry: false, refetchInterval: false } },
        })
      }
    >
      <MemoryRouter initialEntries={["/executions/run"]}>
        <ConfigProvider wave={{ disabled: true }}>
          <App>{element}</App>
        </ConfigProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  );
}
beforeEach(() => {
  vi.stubGlobal("fetch", vi.fn());
});
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
function respond(data: unknown, status = 200) {
  return Promise.resolve(
    new Response(JSON.stringify(data), {
      status,
      headers: { "content-type": "application/json" },
    }),
  );
}
describe("real API client", () => {
  it("sends credentials and CSRF header", async () => {
    vi.mocked(fetch).mockImplementation(() => respond({ id: "p" }));
    await api("/projects", "POST", { name: "Example" });
    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/projects",
      expect.objectContaining({
        credentials: "same-origin",
        headers: expect.objectContaining({
          "X-Requested-With": "AIEngineeringPlatform",
        }),
      }),
    );
  });
  it("surfaces standardized failures", async () => {
    vi.mocked(fetch).mockImplementation(() =>
      respond({ detail: "Budget exhausted" }, 409),
    );
    await expect(api("/projects")).rejects.toThrow("Budget exhausted");
  });
  it("does not fabricate data on unauthorized responses", async () => {
    vi.mocked(fetch).mockImplementation(() => respond({}, 401));
    await expect(api("/projects")).rejects.toBeInstanceOf(ApiError);
  });
});
describe("connected screens", () => {
  it("shows empty project guidance", async () => {
    vi.mocked(fetch).mockImplementation(() => respond({ items: [] }));
    wrapper(<Projects />);
    expect(await screen.findByText(/Nothing here yet/)).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: /New project/ }));
    expect(await screen.findByText("Create project")).toBeInTheDocument();
    expect(screen.getByLabelText("Project name")).toBeInTheDocument();
  });
  it("shows server project data", async () => {
    vi.mocked(fetch).mockImplementation(() =>
      respond({
        items: [
          {
            id: "p",
            name: "Persisted product",
            description: "Real response",
            status: "Active",
            technologyStack: ["C#"],
            updatedAt: new Date().toISOString(),
          },
        ],
      }),
    );
    wrapper(<Projects />);
    expect(await screen.findByText("Persisted product")).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: /Open workspace/ }),
    ).toHaveAttribute("href", "/projects/p");
  });
  it("renders persisted agent messages", async () => {
    vi.mocked(fetch).mockImplementation((url) =>
      respond(
        String(url).includes("/messages") || String(url).includes("/feed")
          ? {
              items: [
                {
                  id: "m",
                  name: "BackendEngineer",
                  content: "Actual saved output",
                  createdAt: new Date().toISOString(),
                  runId: "r",
                },
              ],
            }
          : {
              items: [
                {
                  id: "c",
                  name: "AgentConversation",
                  createdAt: new Date().toISOString(),
                  runId: "r",
                },
              ],
            },
      ),
    );
    wrapper(<ConversationPanel projectId="p" type="AgentConversation" />);
    expect(await screen.findByText("Actual saved output")).toBeInTheDocument();
    expect(screen.getByText("Backend Engineer")).toBeInTheDocument();
  });
  it("records approval only through the API", async () => {
    vi.mocked(fetch).mockImplementation((url, options) =>
      respond(
        options?.method === "POST"
          ? {}
          : {
              items: [
                {
                  id: "a",
                  name: "Prepare delivery",
                  status: "Pending",
                  content: "Review local evidence",
                  runId: "r",
                  metadata: {},
                },
              ],
            },
      ),
    );
    wrapper(<Approvals />);
    fireEvent.click(await screen.findByRole("button", { name: /^Approve$/ }));
    const buttons = await screen.findAllByRole("button", { name: /^Approve$/ });
    fireEvent.click(buttons[buttons.length - 1]);
    await waitFor(() =>
      expect(fetch).toHaveBeenCalledWith(
        "/api/v1/approvals/a/decision",
        expect.objectContaining({
          method: "POST",
          body: JSON.stringify({ approve: true, reason: "" }),
        }),
      ),
    );
  });
});

describe("execution boundaries", () => {
  it("shows real policy budgets, iterations and tool limits", async () => {
    vi.mocked(fetch).mockImplementation((url) =>
      respond(
        String(url).includes("/stream")
          ? { items: [] }
          : {
              id: "run",
              projectId: "project",
              currentStage: "Implementing",
              iteration: 2,
              retryCount: 1,
              cost: 0.125,
              tokenUsage: 1500,
              toolCalls: 7,
              policy: {
                maxIterations: 3,
                maxToolCalls: 25,
                maxDurationMinutes: 15,
                maxBudgetUsd: 2,
                maxRetries: 2,
              },
            },
      ),
    );
    wrapper(
      <Routes>
        <Route path="/executions/:id" element={<ExecutionDetail />} />
      </Routes>,
    );
    expect(await screen.findByText("2/3")).toBeInTheDocument();
    expect(screen.getByText("2/3")).toBeInTheDocument();
    expect(screen.queryByText("Estimated cost")).not.toBeInTheDocument();
    expect(screen.queryByText("Tokens used")).not.toBeInTheDocument();
    expect(screen.getByText("7/25")).toBeInTheDocument();
    expect(screen.getByText("Retries", { exact: false })).toBeInTheDocument();
  });
});
