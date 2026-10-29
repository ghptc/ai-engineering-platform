import { Card, Descriptions, Typography } from "antd";
import { useApi } from "../../components/Common";
import type { RecordWindow } from "../../hooks/useLiveEvents";

export function ProviderDiagnostics({ runId }: { runId: string }) {
  const requests = useApi<RecordWindow>(
    `/runs/${runId}/stream?kind=providerRequest&limit=1`,
    !!runId,
    10000,
  );
  const record = requests.data?.items[0];
  if (!record) return null;
  let diagnostics: Record<string, unknown>;
  try {
    diagnostics = JSON.parse(record.content).Diagnostics;
    if (!diagnostics || typeof diagnostics !== "object") return null;
  } catch {
    return null;
  }
  const fields = [
    "Model",
    "ResponseStatus",
    "IncompleteReason",
    "ErrorCategory",
    "RequestId",
    "ResponseId",
    "InputTokens",
    "OutputTokens",
    "Attempt",
    "HttpStatus",
  ];
  return (
    <Card title="Latest provider request" className="section-gap">
      <Descriptions
        size="small"
        column={2}
        items={fields.map((key) => ({
          key,
          label: key.replace(/([a-z])([A-Z])/g, "$1 $2"),
          children:
            typeof diagnostics[key] === "string" ||
            typeof diagnostics[key] === "number"
              ? String(diagnostics[key])
              : "Unavailable",
        }))}
      />
      <Typography.Paragraph type="secondary">
        Unknown request usage reserves the configured maximum cost. Parsing
        failures require inspection before resuming. Successful tool checkpoints
        remain available.
      </Typography.Paragraph>
    </Card>
  );
}
