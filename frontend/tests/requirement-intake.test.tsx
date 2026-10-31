import { afterEach, describe, expect, it, vi } from "vitest";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { App, ConfigProvider } from "antd";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { RequirementIntake } from "../src/features/projects/RequirementIntake";
import type { RequirementScope } from "../src/features/projects/requirementScope";
const scope: RequirementScope = {
  projectId: "p",
  revision: "revision-1",
  parentRevision: null,
  version: 1,
  createdAt: "2026-10-31T12:00:00Z",
  reason: "Fixture import",
  readyForEstimation: false,
  warnings: ["Review draft candidates."],
  document: {
    key: "requirements",
    fileName: "requirements.md",
    sha256: "source-sha",
    mediaType: "text/markdown",
    extractedText: "Untrusted <script>never execute</script> source",
  },
  requirements: Array.from({ length: 125 }, (_, i) => ({
    id: `req-${i + 1}`,
    description: `REQ-${i + 1}: Source requirement ${i + 1}`,
    acceptanceCriteria: [],
    priority: null,
    classification: "Unclassified",
    dependencies: [],
    complexity: "Unknown",
    implementationState: "NotStarted",
    components: [],
    milestone: null,
    roles: [],
    verificationStrategy: "",
    sources: [
      {
        documentSha256: "source-sha",
        location: `line ${i + 1}`,
        originalLabel: `REQ-${i + 1}`,
        text: `REQ-${i + 1}: Source requirement ${i + 1}`,
      },
    ],
    warnings: ["Owner review required"],
    ownerReviewed: false,
    reviewNote: "",
  })),
};
function setup(value: RequirementScope | null = scope, postStatus = 200) {
  const fetcher = vi.fn(async (url: RequestInfo | URL, init?: RequestInit) => {
    const path = String(url);
    if (init?.method === "POST")
      return new Response(
        JSON.stringify(
          postStatus === 409 ? { title: "Scope changed. Reload." } : value,
        ),
        { status: postStatus },
      );
    return new Response(
      JSON.stringify(
        path.endsWith("/current") || path.includes("/revision-")
          ? value
          : { items: [], nextBefore: null },
      ),
    );
  });
  vi.stubGlobal("fetch", fetcher);
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <ConfigProvider theme={{ token: { motion: false } }}>
      <App>
        <QueryClientProvider client={client}>
          <RequirementIntake projectId="p" />
        </QueryClientProvider>
      </App>
    </ConfigProvider>,
  );
  return fetcher;
}
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});
describe("project requirement intake", () => {
  it("renders only twenty of 125 requirements and searches the complete scope", async () => {
    setup();
    await screen.findAllByText("125 requirements");
    expect(screen.getAllByRole("row")).toHaveLength(21);
    expect(
      screen.queryByText("REQ-125: Source requirement 125"),
    ).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("Search imported requirements"), {
      target: { value: "REQ-125:" },
    });
    expect(
      await screen.findByText("REQ-125: Source requirement 125"),
    ).toBeInTheDocument();
    expect(screen.getAllByRole("row")).toHaveLength(2);
    expect(
      screen.getByText(/Imports do not authorize spending/),
    ).toBeInTheDocument();
  });
  it("shows an honest empty state and rejects unsupported files without a request", async () => {
    const fetcher = setup(null);
    await screen.findByText(/No imported requirements/);
    fireEvent.change(screen.getByLabelText("Requirements document"), {
      target: { files: [new File(["pdf"], "scope.pdf")] },
    });
    fireEvent.click(screen.getByRole("button", { name: "Import draft scope" }));
    fireEvent.click(
      await screen.findByRole("button", { name: "Import draft" }),
    );
    await screen.findByText(
      "Choose a .md, .txt or .docx file, up to 750000 bytes.",
    );
    expect(
      fetcher.mock.calls.filter(([, init]) => init?.method === "POST"),
    ).toHaveLength(0);
  });
  it("uploads raw document bytes only after confirmation with CSRF and scope revision", async () => {
    const fetcher = setup();
    await screen.findAllByText("125 requirements");
    const file = new File(["REQ-1: true source"], "requirements.md", {
      type: "text/markdown",
    });
    fireEvent.change(screen.getByLabelText("Requirements document"), {
      target: { files: [file] },
    });
    fireEvent.click(screen.getByRole("button", { name: "Import draft scope" }));
    expect(
      fetcher.mock.calls.filter(([, init]) => init?.method === "POST"),
    ).toHaveLength(0);
    fireEvent.click(
      await screen.findByRole("button", { name: "Import draft" }),
    );
    await waitFor(() =>
      expect(
        fetcher.mock.calls.some(([, init]) => init?.method === "POST"),
      ).toBe(true),
    );
    const [path, request] = fetcher.mock.calls.find(
      ([, init]) => init?.method === "POST",
    )!;
    expect(String(path)).toContain("expectedRevision=revision-1");
    expect(request?.body).toBe(file);
    expect(request?.headers).toMatchObject({
      "Content-Type": "application/octet-stream",
      "X-Requested-With": "AIEngineeringPlatform",
    });
  });
  it("preserves source references during review and surfaces stale revision conflicts", async () => {
    const fetcher = setup(scope, 409);
    await screen.findAllByText("125 requirements");
    fireEvent.click(
      screen.getAllByRole("button", { name: "Review" })[0],
    );
    fireEvent.change(
      screen.getByLabelText("Acceptance criteria (one per line)"),
      { target: { value: "Actual scenario" } },
    );
    fireEvent.click(screen.getByRole("button", { name: "Save owner review" }));
    await screen.findByText("Review was not saved");
    const [path, request] = fetcher.mock.calls.find(
      ([, init]) => init?.method === "POST",
    )!;
    expect(String(path)).toContain("/revision-1/requirements/req-1/review");
    const body = JSON.parse(String(request?.body));
    expect(body.acceptanceCriteria).toEqual(["Actual scenario"]);
    expect(body.sources).toBeUndefined();
    expect(body.implementationState).toBe("NotStarted");
    expect(
      screen.getByRole("button", { name: "Reload scope" }),
    ).toBeInTheDocument();
    fireEvent.click(screen.getByText("1 original source reference(s)"));
    expect(await screen.findByText("source-sha · line 1")).toBeInTheDocument();
  }, 15000);
});
