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
import { ReadableEntry } from "../../components/Readable";
import type { Entry, Project, Run, Page } from "../../types";
import type { RecordWindow } from "../../hooks/useLiveEvents";
export function ApprovalCard({ entry }: { entry: Entry }) {
  const [decision, setDecision] = useState<boolean>();
  const [reason, setReason] = useState("");
  const { message } = App.useApp();
  const client = useQueryClient();
  const project = useApi<Project>(
    `/projects/${entry.projectId}`,
    !!entry.projectId,
  );
  const run = useApi<Run>(`/runs/${entry.runId}`, !!entry.runId);
  const decide = useMutation({
    mutationFn: () =>
      api(`/approvals/${entry.id}/decision`, "POST", {
        approve: decision,
        reason,
      }),
    onSuccess: () => {
      setDecision(undefined);
      setReason("");
      void client.invalidateQueries({
        predicate: (q) =>
          String(q.queryKey[0]).includes("approvals") ||
          String(q.queryKey[0]).includes(`/runs/${entry.runId}`),
      });
      message.success("Decision recorded");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card
      size="small"
      title={entry.name}
      extra={<Status value={entry.status} />}
    >
      <Typography.Text type="secondary">
        {project.data?.name ?? "Project"} · Orchestrator / Code Reviewer
      </Typography.Text>
      <ReadableEntry entry={entry} />
      <Typography.Paragraph>
        Task:{" "}
        {run.data?.tasks?.[run.data.taskIndex]?.title ?? "See execution scope"}
      </Typography.Paragraph>
      <Alert
        type="info"
        showIcon
        title="Delivery approval permits a commit and draft PR. It never permits merging."
      />
      <Space wrap className="section-gap">
        <Link to={`/executions/${entry.runId}?tab=artifacts`}>
          Inspect tests, diff and review evidence
        </Link>
        {entry.status === "Pending" ? (
          <>
            <Button type="primary" onClick={() => setDecision(true)}>
              Approve
            </Button>
            <Button danger onClick={() => setDecision(false)}>
              Reject
            </Button>
          </>
        ) : (
          <Typography.Text type="secondary">
            Decision saved{" "}
            {entry.metadata?.decidedAt &&
              new Date(entry.metadata.decidedAt).toLocaleString()}
          </Typography.Text>
        )}
      </Space>
      <Modal
        title={decision ? "Approve delivery" : "Reject delivery"}
        open={decision !== undefined}
        onCancel={() => setDecision(undefined)}
        onOk={() => decide.mutate()}
        okText={decision ? "Approve" : "Reject"}
        confirmLoading={decide.isPending}
      >
        <Typography.Paragraph>
          Review actual test and diff evidence before authorizing repository
          changes. This action is checked by the server.
        </Typography.Paragraph>
        <Input.TextArea
          aria-label="Decision feedback"
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          rows={4}
        />
      </Modal>
    </Card>
  );
}
export function Approvals() {
  const [before, setBefore] = useState<number>();
  const query = useApi<RecordWindow>(
    `/approvals/feed?limit=30${before != null ? `&before=${before}` : ""}`,
    true,
    10000,
  );
  return (
    <>
      <Heading
        eyebrow="OWNER DECISIONS"
        title="Approvals"
        description="Review evidence and authorize sensitive repository delivery. No automatic merge."
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <div className="approval-grid">
          {query.data?.items.map((e) => (
            <ApprovalCard key={e.id} entry={e} />
          ))}
        </div>
      </QueryState>
      <Space className="history-controls">
        <Button
          disabled={!query.data?.hasMore}
          onClick={() => setBefore(query.data?.nextBefore)}
        >
          Older decisions
        </Button>
        <Button disabled={before == null} onClick={() => setBefore(undefined)}>
          Latest decisions
        </Button>
      </Space>
    </>
  );
}
