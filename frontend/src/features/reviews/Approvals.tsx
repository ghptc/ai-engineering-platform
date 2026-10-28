import { useState } from "react";
import {
  App,
  Alert,
  Button,
  Card,
  Input,
  Modal,
  Space,
  Typography,
} from "antd";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, Status, useApi } from "../../components/Common";
import type { Entry, Page } from "../../types";
export function Approvals() {
  const query = useApi<Page<Entry>>("/approvals");
  const [selected, setSelected] = useState<{
    entry: Entry;
    approve: boolean;
  }>();
  const [reason, setReason] = useState("");
  const { message } = App.useApp();
  const client = useQueryClient();
  const decide = useMutation({
    mutationFn: () =>
      api("/approvals/" + selected?.entry.id + "/decision", "POST", {
        approve: selected?.approve,
        reason,
      }),
    onSuccess: () => {
      setSelected(undefined);
      setReason("");
      void client.invalidateQueries();
      message.success("Decision recorded");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <>
      <Heading
        eyebrow="HUMAN CONTROL"
        title="Approvals"
        description="You remain the authority over sensitive repository operations."
      />
      <Alert
        type="info"
        showIcon
        title="Approving delivery authorizes a commit and draft pull request. It does not authorize merging."
        style={{ marginBottom: 24 }}
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <div className="project-grid">
          {query.data?.items.map((entry) => (
            <Card
              key={entry.id}
              title={entry.name}
              extra={<Status value={entry.status} />}
            >
              <Typography.Paragraph>{entry.content}</Typography.Paragraph>
              <Link to={"/executions/" + entry.runId}>
                Inspect execution evidence
              </Link>
              <div className="approval-actions">
                {entry.status === "Pending" ? (
                  <Space>
                    <Button
                      type="primary"
                      onClick={() => setSelected({ entry, approve: true })}
                    >
                      Approve
                    </Button>
                    <Button
                      danger
                      onClick={() => setSelected({ entry, approve: false })}
                    >
                      Reject
                    </Button>
                  </Space>
                ) : (
                  <Typography.Text type="secondary">
                    Decision saved{" "}
                    {entry.metadata.decidedAt &&
                      new Date(entry.metadata.decidedAt).toLocaleString()}
                  </Typography.Text>
                )}
              </div>
            </Card>
          ))}
        </div>
      </QueryState>
      <Modal
        title={selected?.approve ? "Approve delivery" : "Reject delivery"}
        open={!!selected}
        onCancel={() => setSelected(undefined)}
        onOk={() => decide.mutate()}
        confirmLoading={decide.isPending}
        okText={selected?.approve ? "Approve" : "Reject"}
      >
        <Input.TextArea
          aria-label="Decision feedback"
          placeholder="Add feedback for the engineering team"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={4}
        />
      </Modal>
    </>
  );
}
