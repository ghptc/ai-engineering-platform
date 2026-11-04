import { afterEach, expect, it, vi } from "vitest";
import {
  fireEvent,
  render,
  screen,
  waitFor,
  cleanup,
} from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { App, ConfigProvider } from "antd";
import { MemoryRouter } from "react-router-dom";
import {
  DependencyApprovalPanel,
  type DependencyRequest,
} from "../src/features/dependencies/DependencyApprovals";
const entry: DependencyRequest = {
  id: "request",
  projectId: "project",
  runId: "run",
  stage: "Planning",
  agent: "Architect",
  originatingTaskId: "task",
  originatingOperationId: "operation",
  taskIndex: 0,
  status: "Pending",
  version: 0,
  createdAt: "2026-11-01T00:00:00Z",
  audit: [],
  proposal: {
    version: 1,
    ecosystem: "npm",
    packageManager: "npm",
    registry: "https://registry.npmjs.org",
    packages: [
      {
        name: "is-number",
        version: "7.0.0",
        development: false,
        reason: "Numeric validation",
      },
    ],
    capabilities: [],
    securityImplications: "Unknown vulnerabilities",
    alternatives: ["Built-in checks"],
    independentTasks: [],
    blockedTasks: ["Validation"],
    evidenceReferences: [],
  },
};
function setup(request = entry) {
  const fetcher = vi.fn(
    async (_: RequestInfo | URL, init?: RequestInit) =>
      new Response(
        JSON.stringify(
          init?.method === "POST"
            ? request
            : {
                currentStage: "AwaitingDependencyApproval",
                cost: 0.2,
                toolCalls: 26,
                startedAt: new Date().toISOString(),
                policy: {
                  maxDurationMinutes: 60,
                  maxBudgetUsd: 5,
                  maxToolCalls: 100,
                },
              },
        ),
      ),
  );
  vi.stubGlobal("fetch", fetcher);
  render(
    <MemoryRouter>
      <ConfigProvider theme={{ token: { motion: false } }}>
        <App>
          <QueryClientProvider
            client={
              new QueryClient({ defaultOptions: { queries: { retry: false } } })
            }
          >
            <DependencyApprovalPanel entry={request} />
          </QueryClientProvider>
        </App>
      </ConfigProvider>
    </MemoryRouter>,
  );
  return fetcher;
}
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
it("requires explicit registry permission plus an owner reason before resolution", async () => {
  const fetcher = setup();
  await screen.findByText("$0.2 / $5");
  expect(screen.getByText("Awaiting Owner Approval")).toBeInTheDocument();
  fireEvent.click(
    screen.getByRole("button", {
      name: "Authorize bounded registry resolution",
    }),
  );
  expect(screen.getByRole("button", { name: "OK" })).toBeDisabled();
  fireEvent.click(
    screen.getByRole("checkbox", {
      name: /I authorize the controlled executor/,
    }),
  );
  fireEvent.change(screen.getByLabelText("Dependency decision reason"), {
    target: { value: "Allow bounded official registry lookup" },
  });
  fireEvent.click(screen.getByRole("button", { name: "OK" }));
  await waitFor(() =>
    expect(fetcher.mock.calls.some(([, init]) => init?.method === "POST")).toBe(
      true,
    ),
  );
  expect(
    JSON.parse(
      String(
        fetcher.mock.calls.find(([, init]) => init?.method === "POST")![1]
          ?.body,
      ),
    ),
  ).toMatchObject({ action: "Resolve", registryAccess: true, version: 0 });
  expect(
    fetcher.mock.calls.some(([url]) => String(url).includes("/resume")),
  ).toBe(false);
});
it("approves selected package identities without granting network or automatically resuming", async () => {
  const resolved = {
    ...entry,
    status: "ReadyForReview",
    version: 2,
    resolution: {
      planSha256: "plan",
      manifest: "{}",
      lock: "{}",
      cacheSha256: "cache",
      packages: [
        {
          name: "is-number",
          version: "7.0.0",
          development: false,
          direct: true,
          license: "Unknown",
          securityStatus: "Unknown",
          integrity: "sha512-fixture",
          source: "https://registry.npmjs.org",
        },
      ],
    },
  };
  const fetcher = setup(resolved);
  await screen.findByText("$0.2 / $5");
  expect(
    screen.getByRole("button", { name: "Approve selected packages" }),
  ).toBeDisabled();
  fireEvent.click(screen.getAllByRole("checkbox")[1]);
  fireEvent.click(
    screen.getByRole("button", { name: "Approve selected packages" }),
  );
  fireEvent.change(screen.getByLabelText("Dependency decision reason"), {
    target: { value: "Reviewed exact source and integrity" },
  });
  fireEvent.click(screen.getByRole("button", { name: "OK" }));
  await waitFor(() =>
    expect(fetcher.mock.calls.some(([, init]) => init?.method === "POST")).toBe(
      true,
    ),
  );
  const [, init] = fetcher.mock.calls.find(
    ([, init]) => init?.method === "POST",
  )!;
  expect(JSON.parse(String(init?.body))).toMatchObject({
    action: "Approve",
    packageIds: ["is-number@7.0.0"],
    registryAccess: false,
  });
});
it("rejection is a persisted decision rather than a chat message", async () => {
  const fetcher = setup();
  await screen.findByText("$0.2 / $5");
  fireEvent.click(screen.getByRole("button", { name: "Reject request" }));
  fireEvent.change(screen.getByLabelText("Dependency decision reason"), {
    target: { value: "Use built-in validation instead" },
  });
  fireEvent.click(screen.getByRole("button", { name: "OK" }));
  await waitFor(() =>
    expect(
      fetcher.mock.calls.some(
        ([url, init]) =>
          String(url).endsWith("/decision") && init?.method === "POST",
      ),
    ).toBe(true),
  );
  expect(
    fetcher.mock.calls.some(([url]) => String(url).includes("/messages")),
  ).toBe(false);
});

it("requires reconciliation and never offers blind acquisition replay", () => {
  setup({ ...entry, status: "Resolving", diagnostic: "Outcome unknown" });
  expect(
    screen.getByRole("button", { name: "Reconcile existing resolution" }),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /acquisition retry/i }),
  ).not.toBeInTheDocument();
});

it("shows known failures without an endless retry prompt", () => {
  setup({
    ...entry,
    status: "ResolutionFailed",
    diagnostic: "dependency_registry_response_limit",
  });
  expect(
    screen.getByText("Acquisition failed; administrator remediation required"),
  ).toBeInTheDocument();
  expect(
    screen.queryByRole("button", { name: /Authorize.*resolution/i }),
  ).not.toBeInTheDocument();
});
