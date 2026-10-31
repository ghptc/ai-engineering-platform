import { useState } from "react";
import { Alert, Button, Collapse, Empty, Space, Table, Typography } from "antd";
import { Status, useApi } from "../../components/Common";
import { human } from "../../lib/engineering";
import type { AgentTaskWindow, AgentTaskView } from "../../types";

export function AgentTasks({
  runId,
  agent = "",
}: {
  runId: string;
  agent?: string;
}) {
  const [before, setBefore] = useState<string>();
  const [following, setFollowing] = useState(true);
  const path = `/runs/${runId}/agent-tasks?limit=25${before ? `&before=${encodeURIComponent(before)}` : ""}`;
  const tasks = useApi<AgentTaskWindow>(
    path,
    following || Boolean(before),
    following && !before ? 5000 : false,
  );
  const items = (tasks.data?.items ?? []).filter(
    (task) => !agent || task.agent === agent,
  );
  return (
    <Collapse
      size="small"
      items={[
        {
          key: "tasks",
          label: "Independent agent tasks",
          children: (
            <>
              <Space wrap className="workspace-layout-toolbar">
                <Button
                  size="small"
                  aria-pressed={following}
                  onClick={() => {
                    setFollowing(!following);
                    if (!following) setBefore(undefined);
                  }}
                >
                  {following ? "Pause task follow" : "Follow latest tasks"}
                </Button>
                <Button
                  size="small"
                  disabled={!before}
                  onClick={() => {
                    setBefore(undefined);
                    setFollowing(true);
                  }}
                >
                  Latest tasks
                </Button>
                <Button
                  size="small"
                  disabled={!tasks.data?.hasMore}
                  onClick={() => setBefore(tasks.data?.nextBefore)}
                >
                  Older tasks
                </Button>
                <Typography.Text type="secondary">
                  25 tasks per page · completion here describes a model
                  operation, not verified delivery
                </Typography.Text>
              </Space>
              {tasks.error ? (
                <Alert
                  type="error"
                  title="Worker history unavailable"
                  description={tasks.error.message}
                />
              ) : !tasks.isLoading && !items.length ? (
                <Empty
                  image={Empty.PRESENTED_IMAGE_SIMPLE}
                  description="No persisted independent worker tasks in this page. Legacy execution uses operational history below."
                />
              ) : (
                <Table<AgentTaskView>
                  size="small"
                  rowKey="id"
                  dataSource={items}
                  loading={tasks.isLoading}
                  pagination={false}
                  scroll={{ x: 700, y: 320 }}
                  expandable={{
                    expandedRowRender: (task) => (
                      <div className="worker-task-details">
                        <Typography.Paragraph copyable>
                          Task: {task.id}
                        </Typography.Paragraph>
                        <Typography.Paragraph>
                          Scheduled: {task.createdAt} · Started:{" "}
                          {task.startedAt ?? "not recorded"} · Result recorded:{" "}
                          {task.completedAt ?? "not recorded"}
                        </Typography.Paragraph>
                        <Typography.Paragraph copyable>
                          Source revision:{" "}
                          {task.sourceRevision ?? "not recorded"}
                        </Typography.Paragraph>
                        {task.summary ? (
                          <>
                            <strong>Model-authored progress summary</strong>
                            <p>{task.summary}</p>
                          </>
                        ) : (
                          <p>No model summary recorded.</p>
                        )}
                        {task.failure && (
                          <Alert
                            type="warning"
                            title="Reconciliation diagnostics"
                            description={task.failure}
                          />
                        )}
                        <p>
                          Actual commands, stdout/stderr, tool receipts and
                          verification evidence appear in operational history
                          below.
                        </p>
                      </div>
                    ),
                  }}
                  columns={[
                    { title: "Agent", dataIndex: "agent", render: human },
                    { title: "Stage", dataIndex: "stage", render: human },
                    {
                      title: "State",
                      dataIndex: "status",
                      render: (value: string) => <Status value={value} />,
                    },
                    {
                      title: "Attempt",
                      dataIndex: "attempt",
                      render: (value: number) => value + 1,
                    },
                    {
                      title: "Assigned objective",
                      dataIndex: "objective",
                      ellipsis: true,
                    },
                  ]}
                />
              )}
            </>
          ),
        },
      ]}
    />
  );
}
