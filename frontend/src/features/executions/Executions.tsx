import { useState } from "react";
import {
  App,
  Alert,
  Button,
  Card,
  Descriptions,
  Empty,
  Input,
  Popconfirm,
  Select,
  Space,
  Table,
  Tabs,
  Typography,
} from "antd";
import { Link, useParams, useSearchParams } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, Status, useApi } from "../../components/Common";
import { useLiveEvents, type RecordWindow } from "../../hooks/useLiveEvents";
import { human, summary, teamState, terminal } from "../../lib/engineering";
import { TeamWorkspace } from "../collaboration/Collaboration";
import { ApprovalCard } from "../reviews/Approvals";
import { Collapse } from "antd";
import { Markdown } from "../../components/Readable";
import { EvidenceDrawer } from "../../components/EvidenceDrawer";
import { ExecutionCapacity } from "./ExecutionCapacity";
import { ProviderDiagnostics } from "./ProviderDiagnostics";
import { ActivityFeed } from "./ActivityFeed";
import type { Entry, Page, Run } from "../../types";
export function Executions() {
  const [status, setStatus] = useState<string>();
  const [offset, setOffset] = useState(0);
  const query = useApi<Page<Run>>(
    `/projects/all/runs?limit=25&offset=${offset}${status ? `&status=${status}` : ""}`,
    true,
    10000,
  );
  return (
    <>
      <Heading
        eyebrow="LIVE ENGINEERING"
        title="Executions"
        description="Follow tasks, specialists, validation and delivery decisions."
      />
      <Select
        allowClear
        placeholder="Filter status"
        aria-label="Filter status"
        className="view-toolbar"
        style={{ width: 240 }}
        value={status}
        onChange={(s) => {
          setStatus(s);
          setOffset(0);
        }}
        options={[
          "Pending",
          "Analyzing",
          "Planning",
          "Implementing",
          "GeneratingTests",
          "RunningTests",
          "Validating",
          "Reviewing",
          "AwaitingHumanApproval",
          "Completed",
          "Failed",
          "Paused",
          "Cancelled",
        ].map((value) => ({ value, label: human(value) }))}
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <Table
          rowKey="id"
          size="small"
          pagination={false}
          scroll={{ x: 720 }}
          dataSource={query.data?.items}
          columns={[
            {
              title: "Task / execution",
              render: (_, r: Run) => (
                <Link to={`/executions/${r.id}`}>
                  {r.tasks?.[r.taskIndex]?.title ?? r.id.slice(0, 12)}
                </Link>
              ),
            },
            {
              title: "Stage",
              dataIndex: "currentStage",
              render: (v: string) => <Status value={v} />,
            },
            { title: "Iteration", dataIndex: "iteration" },
            { title: "Tools", dataIndex: "toolCalls" },
            {
              title: "Started",
              dataIndex: "startedAt",
              render: (v: string) => new Date(v).toLocaleString(),
            },
          ]}
        />
      </QueryState>
      <Space className="history-controls">
        <Button
          disabled={offset === 0}
          onClick={() => setOffset(Math.max(0, offset - 25))}
        >
          Newer executions
        </Button>
        <Button
          disabled={(query.data?.items.length ?? 0) < 25}
          onClick={() => setOffset(offset + 25)}
        >
          Older executions
        </Button>
      </Space>
    </>
  );
}
function Clarification({ run }: { run: Run }) {
  const [content, setContent] = useState("");
  const client = useQueryClient();
  const { message } = App.useApp();
  const mutation = useMutation({
    mutationFn: () =>
      api(`/runs/${run.id}/clarification`, "POST", {
        content,
        version: run.version,
      }),
    onSuccess: () => {
      setContent("");
      void client.invalidateQueries({ queryKey: [`/runs/${run.id}`] });
      message.success("Clarification saved; blocked stage resumed");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card title="Your input is needed" className="section-gap">
      <Typography.Paragraph>
        {run.failureReason?.replace("Human clarification required:", "")}
      </Typography.Paragraph>
      <Input.TextArea
        aria-label="Clarification for engineering team"
        rows={3}
        maxLength={12000}
        value={content}
        onChange={(e) => setContent(e.target.value)}
        placeholder="Answer the blocker. This will be attached to this task's human instructions."
      />
      <Button
        className="section-gap"
        type="primary"
        disabled={!content.trim()}
        loading={mutation.isPending}
        onClick={() => mutation.mutate()}
      >
        Send clarification & retry
      </Button>
    </Card>
  );
}
export function ExecutionDetail() {
  const { id = "" } = useParams();
  const [params, setParams] = useSearchParams();
  const run = useApi<Run>(`/runs/${id}`, !!id, 10000);
  const live = useLiveEvents(run.data?.projectId);
  const events = useApi<RecordWindow>(
    `/runs/${id}/stream?limit=50`,
    !!id,
    10000,
  );
  const handoffs = useApi<RecordWindow>(
    `/runs/${id}/stream?kind=handoff&limit=50`,
    !!id,
    10000,
  );
  const [artifactBefore, setArtifactBefore] = useState<number>();
  const artifacts = useApi<RecordWindow>(
    `/runs/${id}/stream?kind=artifact&limit=50${artifactBefore != null ? `&before=${artifactBefore}` : ""}`,
    !!id,
    10000,
  );
  const approvals = useApi<RecordWindow>(
    `/runs/${id}/stream?kind=approval&limit=30`,
    !!id && run.data?.currentStage === "AwaitingHumanApproval",
    10000,
  );
  const [artifact, setArtifact] = useState<Entry>();
  const client = useQueryClient();
  const { message } = App.useApp();
  const control = useMutation({
    mutationFn: (action: string) => api(`/runs/${id}/${action}`, "POST", {}),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: [`/runs/${id}`] });
    },
    onError: (e) => message.error(e.message),
  });
  const r = run.data;
  const team = r
    ? teamState(r, events.data?.items ?? [], handoffs.data?.items ?? [])
    : [];
  const current = team.find((a) =>
    ["Working", "Waiting for tool", "Failed", "Blocked"].includes(a.state),
  );
  const next = team.find((a) => a.state === "Queued");
  return (
    <QueryState loading={run.isLoading} error={run.error}>
      {r && (
        <>
          <Heading
            eyebrow={`LIVE EXECUTION · ${id.slice(0, 12)}`}
            title={r.tasks?.[r.taskIndex]?.title ?? "Engineering execution"}
            description={
              r.tasks?.[r.taskIndex]?.description?.slice(0, 220) ??
              "Persisted engineering progress and owner decisions."
            }
            actions={
              <Space wrap>
                {!terminal.has(r.currentStage) && (
                  <Button
                    loading={control.isPending}
                    onClick={() => control.mutate("pause")}
                  >
                    Pause
                  </Button>
                )}
                {r.currentStage === "Paused" && (
                  <Button
                    type="primary"
                    onClick={() => control.mutate("resume")}
                  >
                    Resume
                  </Button>
                )}
                {r.currentStage === "Failed" &&
                  !r.failureReason?.startsWith(
                    "Human clarification required:",
                  ) && (
                    <Button
                      type="primary"
                      onClick={() => control.mutate("retry")}
                    >
                      Retry stage
                    </Button>
                  )}
                {!["Completed", "Cancelled"].includes(r.currentStage) && (
                  <Popconfirm
                    title="Cancel this execution?"
                    description="Saved messages, source evidence and execution history will be retained."
                    onConfirm={() => control.mutate("cancel")}
                  >
                    <Button danger>Cancel</Button>
                  </Popconfirm>
                )}
              </Space>
            }
          />
          <Card className="execution-strip" size="small">
            <Descriptions
              size="small"
              column={{ xs: 1, sm: 2, md: 3, lg: 4 }}
              items={[
                {
                  key: "stage",
                  label: "Current stage",
                  children: <Status value={r.currentStage} />,
                },
                {
                  key: "agent",
                  label: "Current agent",
                  children: current
                    ? `${human(current.role)} · ${current.state}`
                    : r.currentStage === "Completed"
                      ? "Team finished"
                      : "No agent currently working",
                },
                {
                  key: "next",
                  label: "Next specialist",
                  children: next
                    ? human(next.role)
                    : r.currentStage === "AwaitingHumanApproval"
                      ? "Owner delivery decision"
                      : "No further agent queued",
                },
                {
                  key: "iteration",
                  label: "Development iteration",
                  children: `${r.iteration} / ${r.policy?.maxIterations ?? "—"}`,
                },
                {
                  key: "tools",
                  label: "Tool calls",
                  children: `${r.toolCalls} / ${r.policy?.maxToolCalls ?? "—"}`,
                },
                {
                  key: "time",
                  label: "Time limit",
                  children: `${r.policy?.maxDurationMinutes ?? "—"} minutes`,
                },
                {
                  key: "task",
                  label: "Task",
                  children: `${(r.taskIndex ?? 0) + 1} / ${r.tasks?.length || 1}`,
                },
                {
                  key: "live",
                  label: "Updates",
                  children: (
                    <span role="status">{live} · persisted replay</span>
                  ),
                },
              ]}
            />
          </Card>
          {r.tasks?.[r.taskIndex] && (
            <Collapse
              className="section-gap"
              items={[
                {
                  key: "task",
                  label: "Task details & acceptance criteria",
                  children: (
                    <>
                      <Markdown content={r.tasks[r.taskIndex].description} />
                      <ul>
                        {r.tasks[r.taskIndex].acceptanceCriteria.map(
                          (criterion, index) => (
                            <li key={index}>{criterion}</li>
                          ),
                        )}
                      </ul>
                    </>
                  ),
                },
              ]}
            />
          )}
          {r.failureReason &&
            !r.failureReason.startsWith("Human clarification required:") && (
              <Alert
                className="section-gap"
                type="error"
                showIcon
                title={`${r.failureCategory?.startsWith("Executor") ? "Executor needs attention" : "Stage failed"}${r.failureCategory ? ` · ${human(r.failureCategory)}` : ""}`}
                description={r.failureReason}
              />
            )}
          {r.failureCategory?.startsWith("OpenAI") && (
            <ProviderDiagnostics runId={r.id} />
          )}
          {r.failureReason?.startsWith("Human clarification required:") && (
            <Clarification run={r} />
          )}
          {/budget/i.test(r.failureReason ?? "") && (
            <Alert
              className="section-gap"
              type="warning"
              title="Budget attention required"
              description={
                <Link to="/usage">
                  Inspect enforced limits and recorded usage
                </Link>
              }
            />
          )}
          {r.currentStage === "AwaitingHumanApproval" &&
            approvals.data?.items
              .filter((a) => a.status === "Pending")
              .map((a) => (
                <div className="section-gap" key={a.id}>
                  <ApprovalCard entry={a} />
                </div>
              ))}
          {r.currentStage === "AwaitingHumanApproval" &&
            !approvals.data?.items.some((a) => a.status === "Pending") && (
              <Alert
                className="section-gap"
                type="warning"
                title="Owner delivery decision required"
                description={
                  <Link to="/approvals">
                    Open pending approvals and delivery evidence
                  </Link>
                }
              />
            )}
          {r.toolCalls / r.policy.maxToolCalls >= 0.8 && (
            <Alert
              className="section-gap"
              type={
                r.toolCalls / r.policy.maxToolCalls >= 0.95
                  ? "error"
                  : "warning"
              }
              title={`${r.toolCalls / r.policy.maxToolCalls >= 0.95 ? "95%" : "80%"} tool allowance reached`}
              description="Checkpoint current work. Review the Capacity tab to explicitly approve an extension."
            />
          )}
          <Tabs
            activeKey={params.get("tab") ?? "team"}
            onChange={(tab) => setParams({ tab }, { replace: true })}
            items={[
              {
                key: "team",
                label: "Agent collaboration",
                children: (
                  <TeamWorkspace
                    key={r.id}
                    run={r}
                    events={events.data?.items ?? []}
                  />
                ),
              },
              {
                key: "capacity",
                label: "Capacity",
                children: <ExecutionCapacity run={r} />,
              },
              {
                key: "timeline",
                label: "Execution timeline",
                children: <ActivityFeed key={id} runId={id} />,
              },
              {
                key: "artifacts",
                label: `Artifacts (${artifacts.data?.items.length ?? 0})`,
                children: (
                  <Card title="Source, validation and delivery evidence">
                    <QueryState
                      loading={artifacts.isLoading}
                      error={artifacts.error}
                      empty={!artifacts.data?.items.length}
                    >
                      <Table
                        size="small"
                        rowKey="id"
                        pagination={{ pageSize: 10 }}
                        dataSource={artifacts.data?.items}
                        columns={[
                          { title: "Artifact", dataIndex: "name" },
                          {
                            title: "Summary",
                            render: (_, e: Entry) => summary(e),
                          },
                          {
                            title: "Inspect",
                            render: (_, e: Entry) => (
                              <Button onClick={() => setArtifact(e)}>
                                Open artifact
                              </Button>
                            ),
                          },
                        ]}
                      />
                    </QueryState>
                    <Space className="history-controls">
                      <Button
                        disabled={!artifacts.data?.hasMore}
                        onClick={() =>
                          setArtifactBefore(artifacts.data?.nextBefore)
                        }
                      >
                        Older artifacts
                      </Button>
                      <Button
                        disabled={artifactBefore == null}
                        onClick={() => setArtifactBefore(undefined)}
                      >
                        Latest artifacts
                      </Button>
                    </Space>
                  </Card>
                ),
              },
            ]}
          />
          <EvidenceDrawer
            key={artifact?.id ?? "none"}
            entry={artifact}
            onClose={() => setArtifact(undefined)}
          />
        </>
      )}
    </QueryState>
  );
}
