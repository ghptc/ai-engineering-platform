import { Card, Statistic, Space, Table, Typography, Button, Alert } from "antd";
import { PlusOutlined, ArrowRightOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import { Heading, QueryState, Status, useApi } from "../components/Common";
import type { Entry, Page, Run, Settings } from "../types";
export function Dashboard() {
  const runs = useApi<Page<Run>>("/projects/all/runs");
  const usage = useApi<{
    tokenUsage: number;
    estimatedCostUsd: number;
    activeProjects: number;
    runningExecutions: number;
    completedExecutions: number;
    failedExecutions: number;
    pendingApprovals: number;
  }>("/usage");
  const activity = useApi<Entry[]>("/activity");
  const settings = useApi<Settings>("/settings");
  const all = runs.data?.items || [];
  return (
    <>
      <Heading
        eyebrow="ENGINEERING OPERATIONS"
        title="Your engineering workspace"
        description="From product intent to verified software, with every decision recorded."
        actions={
          <Link to="/projects">
            <Button type="primary" icon={<PlusOutlined />}>
              Create a project
            </Button>
          </Link>
        }
      />
      <div className="hero-banner">
        <div>
          <span className="eyebrow">AUTONOMY WITH ACCOUNTABILITY</span>
          <Typography.Title level={3}>
            One context. A coordinated engineering team.
          </Typography.Title>
          <Typography.Paragraph>
            Plan, implement, test, validate, and review—with evidence at every
            gate.
          </Typography.Paragraph>
        </div>
        <div className="hero-mark">&lt;/&gt;</div>
      </div>
      {settings.data &&
        (!settings.data.openAIConfigured ||
          !settings.data.sandboxConfigured ||
          !settings.data.githubConfigured) && (
          <Alert
            showIcon
            type="warning"
            title="Integrations need configuration"
            description={
              <span>
                Configure OpenAI, a secure remote sandbox, and a GitHub App to
                enable a complete engineering cycle.{" "}
                <Link to="/settings">
                  Open settings <ArrowRightOutlined />
                </Link>
              </span>
            }
            style={{ marginBottom: 24 }}
          />
        )}
      <div className="metrics">
        {[
          ["Active projects", usage.data?.activeProjects || 0],
          ["Running executions", usage.data?.runningExecutions || 0],
          ["Completed", usage.data?.completedExecutions || 0],
          ["Pending approvals", usage.data?.pendingApprovals || 0],
        ].map(([label, value]) => (
          <Card key={String(label)}>
            <Statistic title={label} value={value} />
          </Card>
        ))}
      </div>
      <div className="workspace-grid">
        <Card
          title="Recent executions"
          extra={<Link to="/executions">View all</Link>}
        >
          <QueryState
            loading={runs.isLoading}
            error={runs.error}
            empty={!all.length}
          >
            <Table
              rowKey="id"
              size="small"
              pagination={false}
              dataSource={all.slice(0, 6)}
              columns={[
                {
                  title: "Run",
                  dataIndex: "id",
                  render: (value: string) => (
                    <Link to={"/executions/" + value}>{value.slice(0, 8)}</Link>
                  ),
                },
                {
                  title: "Stage",
                  dataIndex: "currentStage",
                  render: (value: string) => <Status value={value} />,
                },
                { title: "Iteration", dataIndex: "iteration" },
              ]}
            />
          </QueryState>
        </Card>
        <Card title="Usage & reliability">
          <Space orientation="vertical" size={24}>
            <Statistic
              title="Tokens used"
              value={usage.data?.tokenUsage || 0}
            />
            <Statistic
              title="Estimated model cost"
              prefix="$"
              precision={4}
              value={usage.data?.estimatedCostUsd || 0}
            />
            <Statistic
              title="Failed executions"
              value={usage.data?.failedExecutions || 0}
            />
          </Space>
        </Card>
      </div>
      <Card title="Recent agent activity" style={{ marginTop: 24 }}>
        <QueryState
          loading={activity.isLoading}
          error={activity.error}
          empty={!activity.data?.length}
        >
          <Table
            size="small"
            rowKey="id"
            pagination={false}
            dataSource={activity.data?.slice(0, 8)}
            columns={[
              { title: "Agent", dataIndex: "content" },
              {
                title: "Time",
                dataIndex: "createdAt",
                render: (value: string) => new Date(value).toLocaleString(),
              },
              {
                title: "Execution",
                dataIndex: "runId",
                render: (value: string) => (
                  <Link to={"/executions/" + value}>{value.slice(0, 12)}</Link>
                ),
              },
            ]}
          />
        </QueryState>
      </Card>
      <Card title="Engineering team" style={{ marginTop: 24 }}>
        <div className="team-grid">
          {[
            "Business Analyst",
            "Software Architect",
            "Backend Engineer",
            "Frontend Engineer",
            "Test Engineer",
            "QA Engineer",
            "Code Reviewer",
          ].map((role, i) => (
            <div className="team-member" key={role}>
              <span className="role-dot">0{i + 1}</span>
              <Typography.Text>{role}</Typography.Text>
            </div>
          ))}
        </div>
      </Card>
    </>
  );
}
