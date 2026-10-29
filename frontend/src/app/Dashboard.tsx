import { Alert, Button, Card, Empty, Space, Typography } from "antd";
import { Link } from "react-router-dom";
import { Heading, QueryState, Status, useApi } from "../components/Common";
import { human, summary, teamState, terminal } from "../lib/engineering";
import { useLiveEvents, type RecordWindow } from "../hooks/useLiveEvents";
import type { Entry, Page, Project, Run, Settings } from "../types";
function ActiveWork({ run, project }: { run: Run; project?: Project }) {
  useLiveEvents(run.projectId);
  const events = useApi<RecordWindow>(
    `/runs/${run.id}/stream?limit=50`,
    true,
    10000,
  );
  const handoffs = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=handoff&limit=50`,
    true,
    10000,
  );
  const team = teamState(
    run,
    events.data?.items ?? [],
    handoffs.data?.items ?? [],
  );
  const working = team.find(
    (a) => a.state === "Working" || a.state === "Waiting for tool",
  );
  const next = team.find(
    (a) =>
      a.state !== "Completed" &&
      a.state !== "Idle" &&
      a.state !== "Working" &&
      a.state !== "Waiting for tool",
  );
  return (
    <div className="work-row">
      <div>
        <Typography.Text type="secondary">
          {project?.name ?? "Project " + run.projectId.slice(0, 8)}
        </Typography.Text>
        <h3>
          <Link to={`/executions/${run.id}`}>
            {run.tasks?.[run.taskIndex]?.title ?? "Engineering execution"}
          </Link>
        </h3>
        <Space wrap>
          <Status value={run.currentStage} />
          <span>
            {working
              ? `${human(working.role)} · ${working.state}`
              : terminal.has(run.currentStage)
                ? "Owner attention required"
                : "Waiting for recorded agent activity"}
          </span>
        </Space>
        <p className="secondary">
          {events.data?.items[0]
            ? summary(events.data.items[0])
            : "Waiting for persisted progress"}
        </p>
        {next && <small>Next selected specialist: {human(next.role)}</small>}
      </div>
      <Link to={`/executions/${run.id}`}>
        <Button>Open workspace ↗</Button>
      </Link>
    </div>
  );
}
export function Dashboard() {
  const runs = useApi<Page<Run>>("/projects/all/runs?limit=50", true, 15000);
  const projects = useApi<Page<Project>>("/projects?limit=50");
  const activity = useApi<Entry[]>("/activity", true, 15000);
  const approvals = useApi<RecordWindow>("/approvals/feed?limit=30");
  const settings = useApi<Settings>("/settings");
  const all = runs.data?.items ?? [];
  const ongoing = all.filter(
    (r) => !["Completed", "Cancelled"].includes(r.currentStage),
  );
  const deliveries = all
    .filter((r) => r.currentStage === "Completed")
    .slice(0, 5);
  return (
    <>
      <Heading
        eyebrow="ENGINEERING OPERATIONS"
        title="Your engineering workspace"
        description="Work in progress, team decisions and verified deliveries."
        actions={
          <Link to="/projects">
            <Button type="primary">Open projects</Button>
          </Link>
        }
      />
      {settings.data &&
        (!settings.data.openAIConfigured ||
          !settings.data.githubConfigured ||
          !settings.data.sandboxReady) && (
          <Alert
            showIcon
            type="warning"
            title="Engineering runtime needs attention"
            description={
              <Link to="/settings">
                Inspect OpenAI, GitHub and local sandbox readiness
              </Link>
            }
            className="section-gap"
          />
        )}
      <div className="operations-grid">
        <Card
          title="Active engineering"
          extra={<Link to="/executions">All executions</Link>}
        >
          <QueryState loading={runs.isLoading} error={runs.error}>
            {ongoing.length ? (
              ongoing
                .slice(0, 6)
                .map((run) => (
                  <ActiveWork
                    key={run.id}
                    run={run}
                    project={projects.data?.items.find(
                      (p) => p.id === run.projectId,
                    )}
                  />
                ))
            ) : (
              <Empty description="No execution is active. Open a project to describe the next focused task." />
            )}
          </QueryState>
        </Card>
        <Card title="Action required">
          <Space orientation="vertical" style={{ width: "100%" }}>
            {approvals.data?.items
              .filter((e) => e.status === "Pending")
              .map((e) => (
                <Alert
                  key={e.id}
                  type="warning"
                  showIcon
                  title="Human delivery approval"
                  description={
                    <Link to={`/executions/${e.runId}`}>
                      {e.name} → inspect evidence
                    </Link>
                  }
                />
              ))}
            {ongoing
              .filter(
                (r) =>
                  r.currentStage === "Failed" || r.currentStage === "Paused",
              )
              .map((r) => (
                <Alert
                  key={r.id}
                  type={r.currentStage === "Failed" ? "error" : "info"}
                  title={
                    r.failureReason?.startsWith("Human clarification")
                      ? "Clarification requested"
                      : `Execution ${r.currentStage.toLowerCase()}`
                  }
                  description={
                    <Link to={`/executions/${r.id}`}>
                      {(r.failureReason ?? "Resume when ready").slice(0, 180)} →
                      open decision
                    </Link>
                  }
                />
              ))}
            {!ongoing.some((r) =>
              ["Failed", "Paused", "AwaitingHumanApproval"].includes(
                r.currentStage,
              ),
            ) && (
              <Empty
                image={Empty.PRESENTED_IMAGE_SIMPLE}
                description="No blocking owner decisions in recent executions."
              />
            )}
            {projects.data?.items
              .filter(
                (p) =>
                  p.status === "Active" &&
                  !all.some(
                    (r) =>
                      r.projectId === p.id && !terminal.has(r.currentStage),
                  ),
              )
              .slice(0, 3)
              .map((p) => (
                <Link key={p.id} to={`/projects/${p.id}`}>
                  {p.name} · plan the next task ↗
                </Link>
              ))}
          </Space>
        </Card>
      </div>
      <div className="operations-grid section-gap">
        <Card title="Recent deliveries">
          {deliveries.length ? (
            deliveries.map((r) => (
              <div className="compact-row" key={r.id}>
                <div>
                  <Link to={`/executions/${r.id}`}>
                    {r.tasks?.[r.taskIndex]?.title ??
                      "Verified engineering delivery"}
                  </Link>
                  <p className="secondary">
                    Tests, review and delivery artifacts recorded ·{" "}
                    {new Date(r.completedAt ?? r.startedAt).toLocaleString()}
                  </p>
                </div>
                <Status value="Completed" />
              </div>
            ))
          ) : (
            <Empty description="Completed tasks and their commit, PR and validation evidence will appear here." />
          )}
        </Card>
        <Card title="Team activity">
          <QueryState
            loading={activity.isLoading}
            error={activity.error}
            empty={!activity.data?.length}
          >
            {activity.data?.slice(0, 8).map((e) => (
              <div className="compact-row" key={e.id}>
                <div>
                  <Link to={`/executions/${e.runId}`}>{summary(e)}</Link>
                  <p className="secondary">
                    {new Date(e.createdAt).toLocaleString()}
                  </p>
                </div>
              </div>
            ))}
          </QueryState>
        </Card>
      </div>
      <Typography.Paragraph type="secondary">
        Showing recent persisted executions. Operational states come from run
        records, agent handoffs and tool events.
      </Typography.Paragraph>
    </>
  );
}
