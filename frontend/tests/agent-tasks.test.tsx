import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App } from "antd";
import { AgentTasks } from "../src/features/executions/AgentTasks";
import type { AgentTaskView } from "../src/types";

const task: AgentTaskView = {
  id: "persisted-task-1",
  agent: "BackendEngineer",
  stage: "Implementing",
  status: "Completed",
  objective: "Implement the assigned API",
  sourceRevision: "a".repeat(64),
  attempt: 0,
  createdAt: "2026-10-31T12:00:00Z",
  startedAt: "2026-10-31T12:00:01Z",
  completedAt: "2026-10-31T12:00:03Z",
  summary: "Model proposes a source change; verification remains separate",
};
function mount() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  render(
    <QueryClientProvider client={client}>
      <App>
        <AgentTasks runId="persisted-run" />
      </App>
    </QueryClientProvider>,
  );
  fireEvent.click(screen.getByText("Independent agent tasks"));
  return client;
}
function response(data: unknown) {
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
it("labels persisted task completion separately from verified delivery and expands actual safe details", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(() => response({ items: [task], hasMore: false })),
  );
  mount();
  expect(await screen.findByText("Implement the assigned API")).toBeVisible();
  expect(
    screen.getByText(/completion here describes a model operation/),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: /Expand row/i }));
  expect(screen.getByText("Model-authored progress summary")).toBeVisible();
  expect(screen.getByText(task.summary!)).toBeVisible();
  expect(screen.getByText(/persisted-task-1/)).toBeVisible();
  expect(screen.queryByText(/tests passed/i)).not.toBeInTheDocument();
});
it("uses a bounded server cursor and preserves task identity across history pages", async () => {
  const fetcher = vi.fn((url: string) =>
    response(
      url.includes("before=")
        ? {
            items: [
              {
                ...task,
                id: "older-task",
                objective: "Earlier recorded objective",
              },
            ],
            hasMore: false,
          }
        : {
            items: Array.from({ length: 25 }, (_, i) => ({
              ...task,
              id: `task-${i}`,
            })),
            hasMore: true,
            nextBefore: "task-24",
          },
    ),
  );
  vi.stubGlobal("fetch", fetcher);
  mount();
  await screen.findAllByText("Implement the assigned API");
  expect(screen.getAllByRole("row")).toHaveLength(26);
  fireEvent.click(screen.getByRole("button", { name: "Older tasks" }));
  expect(await screen.findByText("Earlier recorded objective")).toBeVisible();
  expect(fetcher).toHaveBeenLastCalledWith(
    expect.stringContaining("limit=25&before=task-24"),
    expect.anything(),
  );
  expect(
    screen.queryByText("Implement the assigned API"),
  ).not.toBeInTheDocument();
});
it("shows an honest legacy empty state and retains it while task follow is paused", async () => {
  const fetcher = vi.fn(() => response({ items: [], hasMore: false }));
  vi.stubGlobal("fetch", fetcher);
  const client = mount();
  expect(
    await screen.findByText(/No persisted independent worker tasks/),
  ).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "Pause task follow" }));
  const calls = fetcher.mock.calls.length;
  await client.invalidateQueries();
  expect(fetcher).toHaveBeenCalledTimes(calls);
  fireEvent.click(screen.getByRole("button", { name: "Follow latest tasks" }));
  await waitFor(() => expect(fetcher.mock.calls.length).toBeGreaterThan(calls));
});
it("renders reconciliation failures and API errors without fabricating tool output", async () => {
  vi.stubGlobal(
    "fetch",
    vi.fn(() =>
      response({
        items: [
          {
            ...task,
            status: "OutcomeUnknown",
            summary: undefined,
            failure: "InvocationDeadlineExpired",
            completedAt: undefined,
          },
        ],
        hasMore: false,
      }),
    ),
  );
  mount();
  expect(await screen.findByText("Outcome Unknown")).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: /Expand row/i }));
  expect(screen.getByText("No model summary recorded.")).toBeVisible();
  expect(screen.getByText("InvocationDeadlineExpired")).toBeVisible();
  expect(screen.getByText(/Actual commands, stdout\/stderr/)).toBeVisible();
});
