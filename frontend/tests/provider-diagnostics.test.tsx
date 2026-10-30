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

it("shows the incomplete reason, allowance and recovery action", () => {
  state.content = JSON.stringify({
    Diagnostics: {
      Model: "configured-model",
      ResponseStatus: "incomplete",
      IncompleteReason: "max_output_tokens",
      IncompleteClassification: "OutputLimit",
      MaxOutputTokens: 4000,
      OutputTokens: 4000,
      RecoveryGuidance:
        "Retry the failed checkpoint with a larger configured output allowance.",
    },
  });
  render(<ProviderDiagnostics runId="run" />);
  expect(screen.getByText("max_output_tokens")).toBeInTheDocument();
  expect(screen.getByText("OutputLimit")).toBeInTheDocument();
  expect(screen.getAllByText("4000")).toHaveLength(2);
  expect(screen.getByText(/Retry the failed checkpoint/)).toBeInTheDocument();
});

it("offers reason-specific recovery for persisted historical incomplete responses", () => {
  state.content = JSON.stringify({
    Diagnostics: {
      ResponseStatus: "incomplete",
      IncompleteReason: "content_filter",
    },
  });
  render(<ProviderDiagnostics runId="run" />);
  expect(
    screen.getByText(/Increasing output tokens will not address this reason/),
  ).toBeInTheDocument();
});

it("shows safe output-stage metadata and historical malformed recovery", () => {
  state.content = JSON.stringify({
    Diagnostics: {
      ErrorCategory: "MalformedJson",
      OutputStage: "JsonSyntax",
      MessageCount: 1,
      TextPartCount: 1,
      JsonLineNumber: 0,
      JsonBytePosition: 73,
      OutputFormat: "json_schema_strict",
    },
  });
  render(<ProviderDiagnostics runId="run" />);
  expect(screen.getByText("JsonSyntax")).toBeInTheDocument();
  expect(screen.getByText("json_schema_strict")).toBeInTheDocument();
  expect(screen.getByText("73")).toBeInTheDocument();
  expect(
    screen.getByText(/do not infer output-token exhaustion/),
  ).toBeInTheDocument();
});
