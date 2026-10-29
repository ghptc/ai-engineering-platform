import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ProviderDiagnostics } from "../src/features/executions/ProviderDiagnostics";
const state = vi.hoisted(() => ({ content: "" }));
vi.mock("../src/components/Common", () => ({
  useApi: () => ({ data: { items: [{ content: state.content }] } }),
}));
afterEach(cleanup);
it("shows sanitized provider failure metadata and missing usage clearly", () => {
  state.content = JSON.stringify({
    Diagnostics: {
      Model: "configured-model",
      ErrorCategory: "InvalidToolArguments",
      ResponseStatus: "completed",
      RequestId: "req_example",
      InputTokens: 100,
      OutputTokens: null,
    },
  });
  render(<ProviderDiagnostics runId="run" />);
  expect(screen.getByText("InvalidToolArguments")).toBeInTheDocument();
  expect(screen.getByText("req_example")).toBeInTheDocument();
  expect(screen.getByText("configured-model")).toBeInTheDocument();
  expect(screen.getAllByText("Unavailable").length).toBeGreaterThan(0);
  expect(
    screen.getByText(/Successful tool checkpoints remain available/),
  ).toBeInTheDocument();
});
it("tolerates missing historical diagnostics", () => {
  state.content = "invalid historical record";
  const { container } = render(<ProviderDiagnostics runId="run" />);
  expect(container).toBeEmptyDOMElement();
});
