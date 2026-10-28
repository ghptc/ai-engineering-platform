import { useState, lazy, Suspense } from "react";
import {
  App,
  Button,
  Card,
  Descriptions,
  Modal,
  Space,
  Table,
  Tabs,
  Timeline,
  Typography,
  Statistic,
  Alert,
  Select,
} from "antd";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import {
  Heading,
  QueryState,
  Status,
  useApi,
  jsonText,
} from "../../components/Common";
import { useLiveEvents } from "../../hooks/useLiveEvents";
import type { Entry, Page, Run } from "../../types";
const CodeViewer = lazy(() => import("../../components/CodeViewer"));
export function Executions() {
  const query = useApi<Page<Run>>("/projects/all/runs");
  const [status, setStatus] = useState<string>();
  return (
    <>
      <Heading
        eyebrow="DURABLE WORKFLOWS"
        title="Executions"
        description="Follow each engineering cycle from context to reviewed delivery."
      />
      <Select
        allowClear
        placeholder="Filter status"
        aria-label="Filter status"
        value={status}
        onChange={setStatus}
        style={{ width: 220, marginBottom: 20 }}
        options={[
          "Pending",
          "Analyzing",
          "Implementing",
          "RunningTests",
          "AwaitingHumanApproval",
          "Completed",
          "Failed",
          "Paused",
          "Cancelled",
        ].map((value) => ({ value }))}
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <Table
          rowKey="id"
          dataSource={query.data?.items.filter(
            (x) => !status || x.currentStage === status,
          )}
          scroll={{ x: 720 }}
          columns={[
            {
              title: "Execution",
              dataIndex: "id",
              render: (value: string) => (
                <Link to={"/executions/" + value}>{value.slice(0, 12)}</Link>
              ),
            },
            {
              title: "Stage",
              dataIndex: "currentStage",
              render: (value: string) => <Status value={value} />,
            },
            { title: "Iteration", dataIndex: "iteration" },
            { title: "Tokens", dataIndex: "tokenUsage" },
            {
              title: "Cost (USD)",
              dataIndex: "cost",
              render: (value: number) => value.toFixed(4),
            },
            {
              title: "Started",
              dataIndex: "startedAt",
              render: (value: string) => new Date(value).toLocaleString(),
            },
          ]}
        />
      </QueryState>
    </>
  );
}
export function ExecutionDetail() {
  const { id = "" } = useParams();
  const run = useApi<Run>("/runs/" + id);
  useLiveEvents(run.data?.projectId);
  const events = useApi<Page<Entry>>("/runs/" + id + "/events?limit=500");
  const handoffs = useApi<Page<Entry>>("/runs/" + id + "/handoffs?limit=500");
  const artifacts = useApi<Page<Entry>>("/runs/" + id + "/artifacts?limit=500");
  const [artifact, setArtifact] = useState<Entry>();
  const { message } = App.useApp();
  const client = useQueryClient();
  const control = useMutation({
    mutationFn: (action: string) =>
      api("/runs/" + id + "/" + action, "POST", {}),
    onSuccess: () => {
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  const stage = run.data?.currentStage;
  const runnable =
    stage &&
    ![
      "Completed",
      "Failed",
      "Paused",
      "Cancelled",
      "AwaitingHumanApproval",
    ].includes(stage);
  return (
    <QueryState loading={run.isLoading} error={run.error}>
      {run.data && (
        <>
          <Heading
            eyebrow="EXECUTION DETAIL"
            title={"Run " + id.slice(0, 12)}
            description="Persisted stages and artifacts are the source of truth."
            actions={
              <Space>
                {runnable && (
                  <Button onClick={() => control.mutate("pause")}>Pause</Button>
                )}
                {stage === "Paused" && (
                  <Button
                    type="primary"
                    onClick={() => control.mutate("resume")}
                  >
                    Resume
                  </Button>
                )}
                {stage === "Failed" && (
                  <Button
                    type="primary"
                    onClick={() => control.mutate("retry")}
                  >
                    Retry stage
                  </Button>
                )}
                {!["Completed", "Cancelled"].includes(stage || "") && (
                  <Button danger onClick={() => control.mutate("cancel")}>
                    Cancel
                  </Button>
                )}
              </Space>
            }
          />
          {run.data.failureReason && (
            <Alert
              type="error"
              showIcon
              title="Stage failed"
              description={run.data.failureReason}
              style={{ marginBottom: 24 }}
            />
          )}
          <div className="metrics">
            <Card>
              <Statistic
                title="Current stage"
                valueRender={() => <Status value={run.data!.currentStage} />}
              />
            </Card>
            <Card>
              <Statistic
                title="Development iteration"
                value={run.data.iteration}
                suffix={
                  run.data.policy
                    ? `/ ${run.data.policy.maxIterations}`
                    : undefined
                }
              />
            </Card>
            <Card>
              <Statistic title="Tokens used" value={run.data.tokenUsage} />
            </Card>
            <Card>
              <Statistic
                title="Estimated cost"
                prefix="$"
                value={run.data.cost}
                suffix={
                  run.data.policy
                    ? `/ $${run.data.policy.maxBudgetUsd}`
                    : undefined
                }
                precision={4}
              />
            </Card>
          </div>
          <Card size="small" style={{ marginBottom: 24 }}>
            <Descriptions
              size="small"
              items={[
                {
                  key: "tools",
                  label: "Tool calls",
                  children: `${run.data.toolCalls} / ${run.data.policy?.maxToolCalls ?? "—"}`,
                },
                {
                  key: "time",
                  label: "Time limit",
                  children: `${run.data.policy?.maxDurationMinutes ?? "—"} minutes`,
                },
                {
                  key: "retry",
                  label: "Stage retries",
                  children: `${run.data.retryCount} / ${run.data.policy?.maxRetries ?? "—"}`,
                },
              ]}
            />
          </Card>
          {run.data.tasks?.length > 0 && <Card title={"Task " + (run.data.taskIndex + 1) + " of " + run.data.tasks.length} style={{ marginBottom: 24 }}><Typography.Title level={4}>{run.data.tasks[run.data.taskIndex]?.title}</Typography.Title><Typography.Paragraph>{run.data.tasks[run.data.taskIndex]?.description}</Typography.Paragraph><Typography.Text>Selected agents: {run.data.selectedAgents?.join(", ")}</Typography.Text></Card>}
          <Tabs
            defaultActiveKey="timeline"
            items={[
              { key: "handoffs", label: "Agent handoffs", children: <Space orientation="vertical" style={{ width: "100%" }}>{handoffs.data?.items.map(entry => <Card key={entry.id} title={entry.name}><pre className="artifact-preview">{jsonText(entry.content)}</pre></Card>)}</Space> },
              {
                key: "timeline",
                label: "Execution timeline",
                children: (
                  <Card>
                    <QueryState
                      loading={events.isLoading}
                      error={events.error}
                      empty={!events.data?.items.length}
                    >
                      <Timeline
                        items={events.data?.items.map((event) => ({
                          color:
                            event.name === "RunFailed"
                              ? "red"
                              : event.name === "RunCompleted"
                                ? "green"
                                : "blue",
                          content: (
                            <div>
                              <Space>
                                <strong>
                                  {event.name.replace(
                                    /([a-z])([A-Z])/g,
                                    "$1 $2",
                                  )}
                                </strong>
                                <Typography.Text type="secondary">
                                  {new Date(event.createdAt).toLocaleString()}
                                </Typography.Text>
                              </Space>
                              <pre className="event-content">
                                {jsonText(event.content)}
                              </pre>
                            </div>
                          ),
                        }))}
                      />
                    </QueryState>
                  </Card>
                ),
              },
              {
                key: "artifacts",
                label: `Artifacts (${artifacts.data?.items.length || 0})`,
                children: (
                  <Table
                    rowKey="id"
                    dataSource={artifacts.data?.items}
                    columns={[
                      { title: "Artifact", dataIndex: "name" },
                      {
                        title: "Created",
                        dataIndex: "createdAt",
                        render: (value: string) =>
                          new Date(value).toLocaleString(),
                      },
                      {
                        title: "Inspect",
                        render: (_: unknown, value: Entry) => (
                          <Button onClick={() => setArtifact(value)}>
                            Open artifact
                          </Button>
                        ),
                      },
                    ]}
                  />
                ),
              },
            ]}
          />
          <Modal
            open={!!artifact}
            title={artifact?.name}
            onCancel={() => setArtifact(undefined)}
            footer={null}
            width="90vw"
            destroyOnHidden
          >
            {artifact && (
              <>
                <Descriptions
                  items={[
                    {
                      key: "hash",
                      label: "SHA-256",
                      children: artifact.metadata.sha256,
                    },
                  ]}
                />
                <Suspense fallback="Loading code viewer…">
                  <CodeViewer
                    content={jsonText(artifact.content)}
                    language={artifact.name === "CodeDiff" ? "diff" : "json"}
                  />
                </Suspense>
              </>
            )}
          </Modal>
        </>
      )}
    </QueryState>
  );
}
