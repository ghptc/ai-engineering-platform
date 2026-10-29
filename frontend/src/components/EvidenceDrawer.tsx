import { Button, Drawer, Empty, Space, Typography } from "antd";
import { useState } from "react";
import { useApi, QueryState, Status } from "./Common";
import { JsonDetails, ReadableEntry } from "./Readable";
import type { Entry } from "../types";
import { parse, text } from "../lib/engineering";
interface Details {
  entry: Entry;
  tool?: Entry;
  toolInput?: unknown;
  inputAvailability: string;
  artifacts: { id: string; name: string }[];
}
export function EvidenceDrawer({
  entry,
  onClose,
}: {
  entry?: Entry;
  onClose: () => void;
}) {
  const [raw, setRaw] = useState(false);
  const [reference, setReference] = useState<Entry>();
  const active = reference ?? entry;
  const details = useApi<Details>(
    `/runs/${active?.runId}/details/${active?.kind}/${active?.id}`,
    !!active?.runId,
  );
  const resolved = details.data?.entry ?? active;
  return (
    <Drawer
      open={!!entry}
      title={active?.name ?? "Execution evidence"}
      onClose={onClose}
      size="large"
      destroyOnHidden
      extra={
        <Button
          disabled={entry?.metadata?.payloadPreview === "true" && !details.data}
          onClick={() => setRaw(!raw)}
        >
          {raw ? "Readable view" : "Raw payload"}
        </Button>
      }
    >
      {entry && (
        <>
          {reference && (
            <Button
              onClick={() => {
                setReference(undefined);
                setRaw(false);
              }}
            >
              Back to original event
            </Button>
          )}
          <Space wrap>
            <Status value={active!.status || active!.kind} />
            <Typography.Text type="secondary">
              {new Date(active!.createdAt).toLocaleString()}
            </Typography.Text>
          </Space>
          <Typography.Paragraph copyable code>
            {active!.id}
          </Typography.Paragraph>
          {raw ? (
            <JsonDetails content={resolved!.content} />
          ) : (
            <ReadableEntry entry={resolved!} />
          )}
          {entry.runId && (
            <QueryState loading={details.isLoading} error={details.error}>
              <Typography.Title level={5}>Tool evidence</Typography.Title>
              {details.data?.tool ? (
                <>
                  <Status value={details.data.tool.status} />
                  <Typography.Paragraph>
                    {details.data.tool.name}
                  </Typography.Paragraph>
                  <Typography.Paragraph copyable code>
                    {text(
                      parse(details.data.tool.content),
                      "EvidenceId",
                      "No provider evidence ID recorded",
                    )}
                  </Typography.Paragraph>
                  <Typography.Paragraph copyable code>
                    {text(
                      parse(details.data.tool.content),
                      "WorkspaceRevision",
                    )}
                  </Typography.Paragraph>
                  <JsonDetails content={details.data.tool.content} />
                  {details.data.toolInput && (
                    <JsonDetails
                      content={JSON.stringify(details.data.toolInput)}
                    />
                  )}
                </>
              ) : (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="No related tool checkpoint for this record."
                />
              )}
              <Typography.Paragraph type="secondary">
                {details.data?.inputAvailability}
              </Typography.Paragraph>
              <Typography.Title level={5}>Related artifacts</Typography.Title>
              {details.data?.artifacts.map((a) => (
                <Typography.Paragraph key={a.id} copyable={{ text: a.id }}>
                  <Button
                    type="link"
                    disabled={a.id === active?.id}
                    onClick={() => {
                      setRaw(false);
                      setReference({
                        ...entry,
                        id: a.id,
                        name: a.name,
                        kind: "artifact",
                        content: "Loading original artifact…",
                      });
                    }}
                  >
                    {a.name}
                  </Button>{" "}
                  · {a.id.slice(0, 12)}
                </Typography.Paragraph>
              ))}
            </QueryState>
          )}
        </>
      )}
    </Drawer>
  );
}
