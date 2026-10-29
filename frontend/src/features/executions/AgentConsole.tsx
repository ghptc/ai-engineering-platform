import { lazy, Suspense, useEffect, useRef, useState } from "react";
import {
  Alert,
  Button,
  Card,
  Collapse,
  Empty,
  Input,
  Space,
  Table,
  Tabs,
  Typography,
} from "antd";
import { useApi, Status } from "../../components/Common";
import { ReadableEntry } from "../../components/Readable";
import { githubUrl, human, parse, text } from "../../lib/engineering";
import type { RecordWindow } from "../../hooks/useLiveEvents";
import type { Entry, Run } from "../../types";
const CodeViewer = lazy(() => import("../../components/CodeViewer"));

function ToolOutput({ entry, runId }: { entry: Entry; runId: string }) {
  const details = useApi<{ entry: Entry }>(
    `/runs/${runId}/details/tool/${entry.id}`,
  );
  const [offset, setOffset] = useState(0);
  const [search, setSearch] = useState("");
  const result = parse(details.data?.entry.content ?? entry.content);
  const execution = parse(
    JSON.stringify(result.Execution ?? result.execution ?? {}),
  );
  const streams = text(execution, "Stdout") || text(execution, "Stderr");
  const output = streams
    ? `stdout:\n${text(execution, "Stdout")}\nstderr:\n${text(execution, "Stderr")}`
    : text(result, "Output");
  const page = output.slice(offset, offset + 8000);
  const display = search
    ? page
        .split("\n")
        .filter((line) => line.toLowerCase().includes(search.toLowerCase()))
        .join("\n")
    : page;
  return (
    <>
      <Space wrap>
        <Status value={entry.status} />
        <span>Exit: {text(result, "ExitCode", "not recorded")}</span>
        <span>Evidence: {text(result, "EvidenceId", "not recorded")}</span>
      </Space>
      <Typography.Paragraph>
        Operation: {entry.metadata.operation ?? entry.name}{" "}
        {entry.metadata.profile
          ? `· authorized profile ${entry.metadata.profile}`
          : ""}
      </Typography.Paragraph>
      <Typography.Paragraph>
        Start: {entry.metadata.startedAt ?? "not recorded"} · End:{" "}
        {entry.metadata.completedAt ?? entry.createdAt}
      </Typography.Paragraph>
      <Typography.Paragraph copyable>
        Workspace revision: {text(result, "WorkspaceRevision", "not recorded")}
      </Typography.Paragraph>
      {Object.keys(execution).length > 0 && (
        <Typography.Paragraph>
          Authorized command:{" "}
          {JSON.stringify(execution.Command ?? execution.command)} · Working
          directory: {text(execution, "WorkingDirectory")}
        </Typography.Paragraph>
      )}
      <Input.Search
        aria-label="Search tool output"
        placeholder="Filter lines in output page"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
      />
      <pre className="execution-terminal">
        {details.error
          ? details.error.message
          : display || "No output in this page."}
      </pre>
      <Typography.Text type="secondary">
        {streams ? "Recorded stdout/stderr" : "Legacy combined output"} ·{" "}
        {offset + 1}–{Math.min(offset + 8000, output.length)} of {output.length}{" "}
        characters.
      </Typography.Text>
      <Space wrap>
        <Button
          disabled={offset === 0}
          onClick={() => setOffset(Math.max(0, offset - 8000))}
        >
          Previous output
        </Button>
        <Button
          disabled={offset + 8000 >= output.length}
          onClick={() => setOffset(offset + 8000)}
        >
          Next output
        </Button>
        <Button
          onClick={() => {
            void navigator.clipboard.writeText(output);
          }}
        >
          Copy output
        </Button>
      </Space>
    </>
  );
}

export function AgentConsole({
  run,
  role,
  handoffs,
}: {
  run: Run;
  role: string;
  handoffs: Entry[];
}) {
  const [before, setBefore] = useState<number>();
  const [follow, setFollow] = useState(true);
  const [frozen, setFrozen] = useState<Entry[]>();
  const [selected, setSelected] = useState<Entry>();
  const [filter, setFilter] = useState("");
  const bottom = useRef<HTMLDivElement>(null);
  const tools = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=tool&limit=30${before != null ? `&before=${before}` : ""}`,
    true,
    follow && before == null ? 3000 : false,
  );
  const items = (frozen ?? tools.data?.items ?? []).filter(
    (e) =>
      e.metadata?.agent === role &&
      (e.metadata.taskIndex == null ||
        e.metadata.taskIndex === String(run.taskIndex)),
  );
  const current = items.filter((e) =>
    `${e.name} ${e.status} ${e.metadata.profile ?? ""}`
      .toLowerCase()
      .includes(filter.toLowerCase()),
  );
  const signature = current.map((e) => e.id).join();
  useEffect(() => {
    if (follow)
      bottom.current?.parentElement?.scrollTo?.({
        top: bottom.current.parentElement.scrollHeight,
      });
  }, [signature, follow]);
  const diff = items.find((e) => e.name === "InspectDiff");
  const diffDetails = useApi<{ entry: Entry }>(
    `/runs/${run.id}/details/tool/${diff?.id}`,
    !!diff,
  );
  const diffResult = parse(diffDetails.data?.entry.content ?? "{}");
  const patch = text(diffResult, "Output");
  const files = patch
    .split("\n")
    .filter((l) => l.startsWith("diff --git "))
    .map((l) => l.replace(/^diff --git a\/(.*?) b\//, "$1 → "));
  const artifacts = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=artifact&limit=30`,
    true,
    10000,
  );
  const messages = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=message&limit=30`,
    true,
    10000,
  );
  const latestResult = messages.data?.items.find(
    (e) => e.name === role && e.metadata?.taskIndex === String(run.taskIndex),
  );
  const activity = handoffs.filter(
    (h) => h.name === role && h.metadata?.taskIndex === String(run.taskIndex),
  );
  return (
    <Card
      title={`${human(role)} · execution workspace`}
      className="section-gap"
    >
      <Tabs
        items={[
          {
            key: "terminal",
            label: "Execution Terminal",
            children: (
              <div className="agent-console">
                <Alert
                  type="info"
                  title="Read-only tool evidence"
                  description="Model tool requests appear in Agent Activity. Only persisted invocations appear here. Output updates after an operation completes."
                />
                <Space wrap className="view-toolbar">
                  <Input
                    aria-label="Filter agent tools"
                    placeholder="Filter tool / profile / status"
                    value={filter}
                    onChange={(e) => setFilter(e.target.value)}
                  />
                  <Button
                    onClick={() => {
                      setFrozen(follow ? tools.data?.items : undefined);
                      setFollow(!follow);
                    }}
                  >
                    {follow ? "Pause-follow" : "Auto-follow"}
                  </Button>
                  <Button
                    onClick={() => {
                      setBefore(undefined);
                      setFrozen(undefined);
                      setFollow(true);
                      setSelected(undefined);
                    }}
                  >
                    Jump to latest
                  </Button>
                </Space>
                <Table
                  size="small"
                  rowKey="id"
                  pagination={false}
                  dataSource={current}
                  columns={[
                    {
                      title: "Tool",
                      render: (_, e: Entry) => (
                        <Button type="link" onClick={() => setSelected(e)}>
                          {e.name}
                        </Button>
                      ),
                    },
                    { title: "Status", dataIndex: "status" },
                    { title: "Time", dataIndex: "createdAt" },
                  ]}
                />
                {selected && (
                  <ToolOutput
                    key={selected.id}
                    entry={selected}
                    runId={run.id}
                  />
                )}
                {!current.length && (
                  <Empty description="No attributed tool evidence in this page. Older runs may not record agent attribution." />
                )}
                <Button
                  disabled={!tools.data?.hasMore}
                  onClick={() => {
                    setBefore(tools.data?.nextBefore);
                    setFrozen(undefined);
                    setFollow(false);
                  }}
                >
                  Older tool activity
                </Button>
                <div ref={bottom} />
              </div>
            ),
          },
          {
            key: "changes",
            label: "Code Changes",
            children: (
              <div className="agent-console">
                <Typography.Paragraph copyable>
                  Workspace revision:{" "}
                  {text(
                    diffResult,
                    "WorkspaceRevision",
                    "No agent-attributed diff in this page",
                  )}
                </Typography.Paragraph>
                {(artifacts.data?.items ?? [])
                  .filter((a) => ["Commit", "PullRequest"].includes(a.name))
                  .map((a) => {
                    const value = parse(a.content);
                    const link = githubUrl(value.html_url ?? value.url);
                    return (
                      <Typography.Paragraph key={a.id}>
                        {a.name === "Commit"
                          ? `Execution commit SHA: ${text(value, "sha", "not recorded")}`
                          : "Execution pull request: "}
                        {link && (
                          <a href={link} target="_blank" rel="noreferrer">
                            {link}
                          </a>
                        )}
                      </Typography.Paragraph>
                    );
                  })}
                {files.length > 0 && (
                  <Collapse
                    items={files.map((file, i) => ({
                      key: String(i),
                      label: `${patch.split("diff --git ")[i + 1]?.includes("new file mode") ? "Added" : patch.split("diff --git ")[i + 1]?.includes("deleted file mode") ? "Removed" : "Modified"}: ${file}`,
                      children: (
                        <pre className="execution-terminal">
                          {patch.split("diff --git ")[i + 1]?.slice(0, 16000)}
                        </pre>
                      ),
                    }))}
                  />
                )}
                {patch ? (
                  <Suspense fallback="Loading diff viewer…">
                    <CodeViewer content={patch} />
                  </Suspense>
                ) : (
                  <Empty description="No recorded agent-attributed diff. Inspect execution artifacts for shared validation and delivery evidence." />
                )}
              </div>
            ),
          },
          {
            key: "activity",
            label: "Agent Activity",
            children: (
              <div className="agent-console">
                {latestResult && (
                  <Card
                    size="small"
                    title="Latest structured operational result"
                  >
                    <ReadableEntry entry={latestResult} />
                  </Card>
                )}
                {activity.length ? (
                  activity.map((entry) => (
                    <Card
                      key={entry.id}
                      size="small"
                      title={`${entry.status} · ${new Date(entry.createdAt).toLocaleTimeString()}`}
                    >
                      <ReadableEntry entry={entry} />
                    </Card>
                  ))
                ) : (
                  <Empty description="No structured handoff recorded yet" />
                )}
                {(run.reworkPlan ?? [])
                  .filter(
                    (p) => p.targetAgent === role || p.sourceAgent === role,
                  )
                  .map((p, i) => (
                    <Alert
                      key={i}
                      type="warning"
                      title={`Rework iteration ${p.iteration}: ${human(String(p.sourceAgent))} → ${human(String(p.targetAgent))}`}
                      description={
                        <>
                          <p>{p.reason}</p>
                          <p>{p.findingIds.join(", ")}</p>
                          <p>Revision: {p.workspaceRevision}</p>
                        </>
                      }
                    />
                  ))}
              </div>
            ),
          },
        ]}
      />
    </Card>
  );
}
