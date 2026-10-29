import { useState } from "react";
import { Avatar, Button, Card, Empty, Select, Space, Typography } from "antd";
import { RobotOutlined, UserOutlined } from "@ant-design/icons";
import { Link, useSearchParams } from "react-router-dom";
import { Heading, QueryState, Status, useApi } from "../../components/Common";
import { EvidenceDrawer } from "../../components/EvidenceDrawer";
import { ReadableEntry } from "../../components/Readable";
import { human, teamState, text, parse } from "../../lib/engineering";
import { useLiveEvents, type RecordWindow } from "../../hooks/useLiveEvents";
import { AgentFlow } from "../executions/AgentFlow";
import { AgentConsole } from "../executions/AgentConsole";
import type { Entry, Page, Project, Run } from "../../types";
export function TeamWorkspace({ run, events }: { run: Run; events: Entry[] }) {
  const [selected, setSelected] = useState<Entry>();
  const [agent, setAgent] = useState<string>();
  const [before, setBefore] = useState<number>();
  const handoffs = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=handoff&limit=50`,
    true,
    10000,
  );
  const messages = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=message&limit=30${before != null ? `&before=${before}` : ""}`,
    true,
    before == null ? 10000 : false,
  );
  const currentHandoffs = (handoffs.data?.items ?? []).filter(
    (e) =>
      (e.metadata?.taskIndex == null ||
        e.metadata.taskIndex === String(run.taskIndex ?? 0)) &&
      (e.metadata?.iteration == null ||
        e.metadata.iteration === String(run.iteration)),
  );
  const team = teamState(run, events, handoffs.data?.items ?? []);
  return (
    <div className="collaboration-workspace">
      <Card
        title="Engineering team"
        extra={
          <Typography.Text type="secondary">
            {team.length} selected specialists
          </Typography.Text>
        }
      >
        <AgentFlow
          run={run}
          events={events}
          handoffs={handoffs.data?.items ?? []}
          onSelect={setAgent}
        />
        {!team.length && (
          <Empty description="This older execution has no persisted specialist selection." />
        )}
      </Card>
      {(agent ??
        team.find((a) =>
          ["Working", "Waiting for tool", "Failed", "Blocked"].includes(
            a.state,
          ),
        )?.role ??
        team[0]?.role) && (
        <AgentConsole
          key={agent ?? "active"}
          run={run}
          role={
            agent ??
            team.find((a) =>
              ["Working", "Waiting for tool", "Failed", "Blocked"].includes(
                a.state,
              ),
            )?.role ??
            team[0].role
          }
          handoffs={handoffs.data?.items ?? []}
        />
      )}
      <Card title="Recorded handoffs">
        <QueryState
          loading={handoffs.isLoading}
          error={handoffs.error}
          empty={!currentHandoffs.length}
        >
          <div
            className="handoff-strip"
            style={{ maxHeight: 240, overflow: "auto" }}
          >
            {[...currentHandoffs]
              .reverse()
              .filter((e) => e.status !== "Working")
              .map((entry) => {
                const next =
                  entry.metadata?.targetAgent ||
                  run.agentTransitions?.find(
                    (t) =>
                      String(t.sourceAgent) === entry.name &&
                      t.iteration === Number(entry.metadata?.iteration) &&
                      t.taskIndex === Number(entry.metadata?.taskIndex),
                  )?.targetAgent;
                return (
                  <button
                    key={entry.id}
                    className="handoff-card"
                    onClick={() => setSelected(entry)}
                  >
                    <strong>
                      {human(entry.name)}{" "}
                      {next ? `→ ${human(String(next))}` : "· result recorded"}
                    </strong>
                    <span>
                      {text(parse(entry.content), "Summary").slice(0, 180)}
                    </span>
                    <Status value={entry.status || "Recorded"} />
                    <time>
                      {new Date(entry.createdAt).toLocaleTimeString()}
                    </time>
                  </button>
                );
              })}
          </div>
        </QueryState>
      </Card>
      <details>
        <summary>Saved agent conversation</summary>
        <Card
          title="Agent conversation"
          extra={
            <Typography.Text type="secondary">
              Saved messages · chronological within page
            </Typography.Text>
          }
        >
          <QueryState
            loading={messages.isLoading}
            error={messages.error}
            empty={!messages.data?.items.length}
          >
            <div
              className="message-list"
              style={{ maxHeight: 400, overflow: "auto" }}
              role="region"
              aria-label="Agent conversation"
            >
              {[...(messages.data?.items ?? [])].reverse().map((entry) => (
                <article className="message" key={entry.id}>
                  <Avatar
                    icon={
                      entry.name === "Client" ? (
                        <UserOutlined />
                      ) : (
                        <RobotOutlined />
                      )
                    }
                  />
                  <div>
                    <div className="message-meta">
                      <strong>{human(entry.name)}</strong>
                      <time>{new Date(entry.createdAt).toLocaleString()}</time>
                      <Status
                        value={
                          entry.name === "Client"
                            ? "Client message"
                            : "Agent message"
                        }
                      />
                    </div>
                    <Typography.Paragraph type="secondary">
                      {entry.metadata?.taskIndex != null
                        ? (run.tasks?.[Number(entry.metadata.taskIndex)]
                            ?.title ??
                          `Task ${Number(entry.metadata.taskIndex) + 1}`)
                        : "Task association not recorded in this legacy message"}
                    </Typography.Paragraph>
                    <ReadableEntry entry={entry} />
                    <Button type="link" onClick={() => setSelected(entry)}>
                      Inspect message & references
                    </Button>
                  </div>
                </article>
              ))}
            </div>
          </QueryState>
          <Space className="history-controls">
            <Button
              disabled={!messages.data?.hasMore}
              onClick={() => setBefore(messages.data?.nextBefore)}
            >
              Older messages
            </Button>
            <Button
              disabled={before == null}
              onClick={() => setBefore(undefined)}
            >
              Latest messages
            </Button>
          </Space>
        </Card>
      </details>
      <EvidenceDrawer
        key={selected?.id ?? "none"}
        entry={selected}
        onClose={() => setSelected(undefined)}
      />
    </div>
  );
}
export function Collaboration() {
  const projects = useApi<Page<Project>>("/projects");
  const [params, setParams] = useSearchParams();
  const project = params.get("project") ?? undefined;
  const runs = useApi<Page<Run>>(
    `/projects/${project}/runs?limit=50`,
    !!project,
  );
  const selected = params.get("run") ?? runs.data?.items[0]?.id;
  const run = useApi<Run>(`/runs/${selected}`, !!selected, 10000);
  const events = useApi<RecordWindow>(
    `/runs/${selected}/stream?limit=50`,
    !!selected,
    10000,
  );
  useLiveEvents(project);
  return (
    <>
      <Heading
        eyebrow="ENGINEERING TEAM"
        title="Agent collaboration"
        description="Follow the selected team, recorded handoffs and actual engineering conversations."
      />
      {(projects.error || runs.error || run.error) && (
        <QueryState
          loading={false}
          error={projects.error ?? runs.error ?? run.error}
        >
          {null}
        </QueryState>
      )}
      <Space wrap className="view-toolbar">
        <Select
          aria-label="Select project"
          placeholder="Select project"
          style={{ width: 280 }}
          value={project}
          onChange={(id) => setParams({ project: id })}
          options={projects.data?.items.map((p) => ({
            label: p.name,
            value: p.id,
          }))}
        />
        <Select
          aria-label="Select execution"
          placeholder="Select execution"
          style={{ width: 280 }}
          value={selected}
          onChange={(id) => setParams({ project: project!, run: id })}
          options={runs.data?.items.map((r) => ({
            label: `${r.tasks?.[r.taskIndex]?.title ?? r.id.slice(0, 12)} · ${human(r.currentStage)}`,
            value: r.id,
          }))}
        />
        {selected && (
          <Link to={`/executions/${selected}`}>Open live execution ↗</Link>
        )}
      </Space>
      {run.data ? (
        <TeamWorkspace
          key={run.data.id}
          run={run.data}
          events={events.data?.items ?? []}
        />
      ) : (
        <Empty description="Select a project and execution to inspect its real engineering team." />
      )}
    </>
  );
}
