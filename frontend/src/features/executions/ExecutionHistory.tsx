import { useEffect, useRef, useState } from "react";
import { Button, Empty, Input, Segmented, Select, Spin, Tag } from "antd";
import { EvidenceDrawer } from "../../components/EvidenceDrawer";
import { QueryState, Status, useApi } from "../../components/Common";
import { field, human, parse, summary, text } from "../../lib/engineering";
import type { Entry, Run } from "../../types";
import type { RecordWindow } from "../../hooks/useLiveEvents";
import {
  attributedAgent,
  classification,
  duration,
  elapsed,
  operationalStatus,
  ordered,
  operationIdentity,
  progressSummary,
  resultEvidence,
  stageId,
  stageInterval,
} from "./observability";

export function safeArguments(input: unknown): string {
  if (!input || typeof input !== "object" || Array.isArray(input))
    return "Arguments not recorded for this operation.";
  const safe: Record<string, unknown> = {};
  for (const [key, value] of Object.entries(input)) {
    if (
      [
        "path",
        "profile",
        "branch",
        "commit",
        "query",
        "kind",
        "id",
        "offset",
        "limit",
        "expectedRevision",
      ].includes(key)
    )
      safe[key] =
        typeof value === "string"
          ? value.slice(0, 500)
          : typeof value === "number"
            ? value
            : "[bounded value]";
    else if (["content", "patch"].includes(key)) {
      safe[key] =
        `[${typeof value === "string" ? value.length : 0} characters stored; inspect original evidence]`;
      if (key === "patch" && typeof value === "string") {
        const path = text(parse(value), "path");
        if (path) safe.path = path.slice(0, 500);
      }
    } else safe[key] = "[value omitted]";
  }
  return JSON.stringify(safe, null, 2).slice(0, 2000);
}
const clean = (value: string) =>
  value
    .replace(/\u001b\[[0-9;]*[A-Za-z]/g, "")
    .replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, "");
export function OutputBlock({ content }: { content: string }) {
  return (
    <pre className="execution-terminal terminal-code">
      {clean(content)
        .split("\n")
        .map((line, i) => (
          <span
            key={i}
            className={
              /^\+/.test(line)
                ? "code-added"
                : /^-|error|failed|exception/i.test(line)
                  ? "code-error"
                  : /^\s*"[^"\n]+":/.test(line)
                    ? "code-key"
                    : ""
            }
          >
            {line}
            {"\n"}
          </span>
        ))}
    </pre>
  );
}
export function OperationDetails({
  entry,
  runId,
}: {
  entry: Entry;
  runId: string;
}) {
  const details = useApi<{
    entry: Entry;
    tool?: Entry;
    toolInput?: unknown;
    inputAvailability?: string;
  }>(`/runs/${runId}/details/${entry.kind}/${entry.id}`);
  const [offset, setOffset] = useState(0);
  const [stream, setStream] = useState("Output");
  const resolved = details.data?.tool ?? details.data?.entry ?? entry;
  const result = parse(resolved.content);
  const executionValue = field(result, "Execution");
  const execution =
    executionValue && typeof executionValue === "object"
      ? (executionValue as Record<string, unknown>)
      : {};
  const output =
    stream === "Output" ? text(result, "Output") : text(execution, stream);
  const evidence = resultEvidence(resolved);
  return (
    <div className="operation-details">
      <QueryState loading={details.isLoading} error={details.error}>
        <dl className="operation-metadata">
          <div>
            <dt>
              {resolved.kind === "tool" || resolved.metadata?.operationId
                ? "Operation ID"
                : "Record ID"}
            </dt>
            <dd>{operationIdentity(resolved)}</dd>
          </div>
          <div>
            <dt>Stage / agent</dt>
            <dd>
              {human(resolved.metadata?.stage || "Not recorded")} ·{" "}
              {human(attributedAgent(resolved) || "Not attributed")}
            </dd>
          </div>
          <div>
            <dt>Started / ended</dt>
            <dd>
              {resolved.metadata?.startedAt || "Not recorded"} /{" "}
              {resolved.metadata?.completedAt || "Not recorded"}
            </dd>
          </div>
          <div>
            <dt>Attempt / recovery</dt>
            <dd>
              {resolved.metadata?.retry === "true"
                ? "Retry recorded"
                : "No retry marker"}{" "}
              ·{" "}
              {resolved.metadata?.iteration
                ? `iteration ${resolved.metadata.iteration}`
                : "iteration not recorded"}
            </dd>
          </div>
        </dl>
        {resolved.kind === "tool" ? (
          <>
            <div className="terminal-section-label">
              Safe recorded arguments
            </div>
            <OutputBlock content={safeArguments(details.data?.toolInput)} />
            {Object.keys(execution).length > 0 && (
              <>
                <div className="terminal-section-label">
                  Actual command ·{" "}
                  {text(
                    execution,
                    "WorkingDirectory",
                    "directory not recorded",
                  )}
                </div>
                <OutputBlock
                  content={JSON.stringify(
                    field(execution, "Command") ?? "Command not recorded",
                    null,
                    2,
                  ).slice(0, 4000)}
                />
              </>
            )}
            <div className="terminal-output-toolbar">
              <Segmented
                size="small"
                aria-label="Output stream"
                value={stream}
                onChange={(v) => {
                  setStream(String(v));
                  setOffset(0);
                }}
                options={
                  Object.keys(execution).length
                    ? ["Output", "Stdout", "Stderr"]
                    : ["Output"]
                }
              />
              <span>Exit {text(result, "ExitCode", "not recorded")}</span>
              <Button
                size="small"
                onClick={() => {
                  void navigator.clipboard?.writeText(
                    output.slice(offset, offset + 8000),
                  );
                }}
              >
                Copy output page
              </Button>
            </div>
            <OutputBlock
              content={
                output.slice(offset, offset + 8000) ||
                "No output recorded for this stream."
              }
            />
            <div className="terminal-output-toolbar">
              <Button
                size="small"
                disabled={!offset}
                onClick={() => setOffset(Math.max(0, offset - 8000))}
              >
                Previous output
              </Button>
              <span>
                {output.length ? offset + 1 : 0}–
                {Math.min(offset + 8000, output.length)} / {output.length}{" "}
                characters
              </span>
              <Button
                size="small"
                disabled={offset + 8000 >= output.length}
                onClick={() => setOffset(offset + 8000)}
              >
                Next output
              </Button>
            </div>
            <div className="evidence-reference">
              <strong>
                {evidence.success &&
                evidence.id &&
                evidence.revision &&
                resolved.name === "RunTests"
                  ? "Successful test evidence"
                  : "Recorded evidence"}
              </strong>
              <code>{evidence.id || "Evidence ID not recorded"}</code>
              <span>
                Revision <code>{evidence.revision || "not recorded"}</code>
              </span>
            </div>
          </>
        ) : (
          <>
            <div className="terminal-section-label">
              {classification(resolved)}
            </div>
            <OutputBlock
              content={
                classification(resolved) === "Model-authored summary"
                  ? progressSummary(resolved).slice(0, 8000)
                  : resolved.content.slice(0, 8000)
              }
            />
            <span className="muted">
              Original details are available in the evidence drawer. Model
              summaries do not establish tool execution.
            </span>
          </>
        )}
      </QueryState>
    </div>
  );
}

export function ExecutionHistory({
  run,
  initialKind = "tool",
  initialView = "Compact",
  selectedAgent = "",
  selectedStage = "",
  onSelectionClear,
}: {
  run: Run;
  initialKind?: string;
  initialView?: string;
  selectedAgent?: string;
  selectedStage?: string;
  onSelectionClear?: () => void;
}) {
  const [kind, setKind] = useState(initialKind);
  const [view, setView] = useState(initialView);
  const [pages, setPages] = useState<number[]>([]);
  const [follow, setFollow] = useState(true);
  const [frozen, setFrozen] = useState<Entry[]>();
  const [search, setSearch] = useState("");
  const [status, setStatus] = useState("all");
  const [open, setOpen] = useState<string>();
  const [selected, setSelected] = useState<Entry>();
  const viewport = useRef<HTMLDivElement>(null);
  const before = pages.at(-1);
  const feed = useApi<RecordWindow>(
    `/runs/${run.id}/stream?kind=${kind}&limit=50${before != null ? `&before=${before}` : ""}`,
    true,
    follow && before == null ? 5000 : false,
  );
  const items = ordered((frozen ?? feed.data?.items ?? []).slice(0, 50));
  const filtered = items.filter(
    (e) =>
      (!selectedAgent || attributedAgent(e) === selectedAgent) &&
      (!selectedStage ||
        (selectedStage === "build"
          ? e.name === "RunCommand" && e.metadata?.profile === "build"
          : stageId(e.metadata?.stage) === selectedStage)) &&
      (status === "all" ||
        (status === "errors"
          ? /Failed|Blocked|Rejected|Exceeded/.test(`${e.status} ${e.name}`)
          : status === "retry"
            ? e.metadata?.retry === "true" ||
              /Retry|Recover|Reused/.test(e.name)
            : e.status === status)) &&
      `${e.id} ${e.name} ${summary(e)} ${attributedAgent(e)} ${e.metadata?.stage ?? ""} ${e.content.slice(0, 2000)}`
        .toLowerCase()
        .includes(search.toLowerCase()),
  );
  const signature = items.map((e) => e.id).join();
  useEffect(() => {
    if (follow && before == null)
      viewport.current?.scrollTo?.({ top: viewport.current.scrollHeight });
  }, [signature, follow, before]);
  const reset = () => {
    setPages([]);
    setFrozen(undefined);
    setFollow(true);
    setOpen(undefined);
  };
  const freeze = () => {
    setFrozen(items);
    setFollow(false);
  };
  const starts = items
    .map((e) => Date.parse(e.metadata?.startedAt ?? e.createdAt))
    .filter(Number.isFinite);
  const ends = items
    .map((e) => Date.parse(e.metadata?.completedAt ?? e.createdAt))
    .filter(Number.isFinite);
  const min = Math.min(...starts),
    max = Math.max(...ends);
  return (
    <section className="history-panel" aria-label="Execution history">
      <div className="history-heading">
        <div>
          <strong>Operational activity</strong>
          <span className="muted">
            {" "}
            {selectedAgent ? human(selectedAgent) : "All agents"} ·{" "}
            {human(run.currentStage)}
          </span>
        </div>
        <div className="pipeline-controls">
          <Button
            size="small"
            aria-pressed={follow}
            onClick={() => {
              if (follow) freeze();
              else reset();
            }}
          >
            {follow ? "Pause-follow" : "Follow latest"}
          </Button>
          <Button size="small" onClick={reset}>
            Jump to latest
          </Button>
        </div>
      </div>
      <div className="history-toolbar">
        <Segmented
          size="small"
          aria-label="History view"
          value={view}
          onChange={(v) => setView(String(v))}
          options={["Compact", "Timeline", "Detailed"]}
        />
        <Select
          size="small"
          aria-label="History source"
          value={kind}
          onChange={(v) => {
            setKind(v);
            setPages([]);
            setFrozen(undefined);
            setOpen(undefined);
          }}
          options={[
            { value: "tool", label: "Tool executions" },
            { value: "event", label: "System events" },
            { value: "handoff", label: "Progress summaries" },
            { value: "artifact", label: "Evidence artifacts" },
          ]}
        />
        <Input
          size="small"
          aria-label="Search execution history"
          placeholder="Search this page / operation ID"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          allowClear
        />
        <Select
          size="small"
          aria-label="History status"
          value={status}
          onChange={setStatus}
          options={[
            { value: "all", label: "All statuses" },
            { value: "errors", label: "Errors / blocked" },
            { value: "Succeeded", label: "Succeeded" },
            { value: "retry", label: "Retries / recovery" },
          ]}
        />
        {(selectedAgent || selectedStage) && (
          <Button size="small" onClick={onSelectionClear}>
            Clear activity filter
          </Button>
        )}
      </div>
      <div className="history-window-note">
        <span>
          {before == null
            ? "Latest persisted window"
            : "Inspecting older activity"}{" "}
          · {items.length} / 50 records · sequence ascending
        </span>
        <span>
          {feed.isFetching && <Spin size="small" />}{" "}
          {follow ? "Following" : "Follow paused"} · Search & filters apply to
          this page
        </span>
      </div>
      <QueryState loading={feed.isLoading} error={feed.error}>
        <div
          ref={viewport}
          style={{
            height: open
              ? 490
              : Math.min(
                  390,
                  Math.max(
                    160,
                    filtered.length * (view === "Compact" ? 60 : 85) + 35,
                  ),
                ),
          }}
          className="execution-history-window activity-window"
          data-testid="activity-window"
          role="region"
          aria-label="Persisted operational activity"
          aria-live={follow ? "polite" : "off"}
          onWheel={() => {
            if (follow) freeze();
          }}
        >
          {!filtered.length && (
            <Empty
              image={Empty.PRESENTED_IMAGE_SIMPLE}
              description={
                items.length
                  ? "No matching records in this page. Clear filters or inspect another page."
                  : "No persisted activity for this source. Nothing is inferred from model intentions."
              }
            />
          )}
          {view === "Timeline" && filtered.length > 0 && (
            <div className="timeline-scale">
              <span>{new Date(min).toLocaleTimeString()}</span>
              <span>
                Observed window · unmeasured gaps are not execution time
              </span>
              <span>{new Date(max).toLocaleTimeString()}</span>
            </div>
          )}
          {filtered.map((entry, i) => {
            const interval = stageInterval(entry, items);
            const agent = attributedAgent(entry),
              stage = entry.metadata?.stage || "Unattributed stage",
              ms = duration(entry) ?? interval?.ms;
            const previous = filtered[i - 1];
            const group = `${agent}/${stage}/${entry.metadata?.iteration ?? ""}`;
            const isNewGroup =
              !previous ||
              `${attributedAgent(previous)}/${previous.metadata?.stage || "Unattributed stage"}/${previous.metadata?.iteration ?? ""}` !==
                group;
            const start = Date.parse(
              entry.metadata?.startedAt ?? entry.createdAt,
            );
            const width =
              ms == null || max <= min
                ? undefined
                : Math.max(0.5, (ms / (max - min)) * 100);
            return (
              <div
                key={entry.id}
                className="history-record"
                data-sequence={entry.sequence}
              >
                {view === "Compact" && isNewGroup && (
                  <div className="activity-group">
                    <strong>{human(agent || "System")}</strong>
                    <span>
                      {human(stage)}
                      {entry.metadata?.iteration
                        ? ` · iteration ${entry.metadata.iteration}`
                        : ""}
                    </span>
                  </div>
                )}
                <div className="terminal-row">
                  <button
                    className="activity-row terminal-row-main"
                    aria-expanded={open === entry.id}
                    onClick={() => {
                      freeze();
                      setOpen(open === entry.id ? undefined : entry.id);
                    }}
                  >
                    <time dateTime={entry.createdAt} title={entry.createdAt}>
                      {new Date(entry.createdAt).toLocaleTimeString()}
                    </time>
                    <span aria-hidden className="terminal-icon">
                      {/Failed|Blocked/.test(operationalStatus(entry))
                        ? "!"
                        : entry.kind === "tool"
                          ? "›_"
                          : "·"}
                    </span>
                    <span className="activity-summary">
                      <strong>
                        {entry.name === "ContextInventory" ||
                        entry.name === "ContextSourceInventory"
                          ? "Context sources measured"
                          : human(entry.name)}
                      </strong>
                      <span>
                        {classification(entry) === "Model-authored summary"
                          ? progressSummary(entry).slice(0, 180)
                          : summary(entry) === "Context sources measured"
                            ? ""
                            : summary(entry).slice(0, 180)}
                      </span>
                    </span>
                    <Tag variant="filled">{classification(entry)}</Tag>
                    {entry.metadata?.retry === "true" && (
                      <Tag color="gold">Retry recorded</Tag>
                    )}
                    <Status value={operationalStatus(entry)} />
                    <span className="duration-label">
                      {ms == null ? "—" : elapsed(ms)}
                    </span>
                    <span aria-hidden>{open === entry.id ? "⌄" : "›"}</span>
                  </button>
                  <Button
                    size="small"
                    type="text"
                    aria-label={`Inspect evidence ${entry.id}`}
                    onClick={() => {
                      freeze();
                      setSelected(entry);
                    }}
                  >
                    ↗
                  </Button>
                </div>
                {view === "Timeline" && (
                  <div
                    className="timeline-track"
                    aria-label={`${entry.name}: ${ms == null ? "duration not recorded" : elapsed(ms)}`}
                  >
                    <span
                      className={
                        entry.status === "Failed"
                          ? "timeline-bar failed"
                          : "timeline-bar"
                      }
                      style={{
                        marginLeft: `${max > min ? ((start - min) / (max - min)) * 100 : 0}%`,
                        width: width == null ? 2 : `${width}%`,
                      }}
                    />
                  </div>
                )}
                {view === "Timeline" && interval && (
                  <div className="record-identity">
                    {interval.waiting ? "Waiting period" : "Stage interval"}:{" "}
                    {human(interval.stage)} · {elapsed(interval.ms)} · to{" "}
                    {new Date(interval.end).toLocaleTimeString()}
                  </div>
                )}
                {view === "Detailed" && (
                  <div className="record-identity">
                    <code>
                      #{entry.sequence} · {operationIdentity(entry)}
                    </code>
                    <span>
                      {human(agent || "Agent not recorded")} · {human(stage)} ·{" "}
                      {entry.metadata?.retry === "true"
                        ? "Retry"
                        : "No retry marker"}
                    </span>
                  </div>
                )}
                {open === entry.id && (
                  <OperationDetails
                    key={entry.id}
                    entry={entry}
                    runId={run.id}
                  />
                )}
              </div>
            );
          })}
        </div>
      </QueryState>
      <div className="history-footer">
        <span>
          Scheduled → Running → Tool Execution → Result → Next Action · shown
          only when recorded
        </span>
        <div className="pipeline-controls">
          <Button
            size="small"
            disabled={!pages.length}
            onClick={() => {
              setPages((p) => p.slice(0, -1));
              setFrozen(undefined);
              setOpen(undefined);
            }}
          >
            Newer activity
          </Button>
          <Button
            size="small"
            disabled={!feed.data?.hasMore || feed.data?.nextBefore == null}
            onClick={() => {
              setFollow(false);
              setFrozen(undefined);
              setOpen(undefined);
              setPages((p) => [...p, feed.data!.nextBefore!]);
            }}
          >
            Older activity
          </Button>
        </div>
      </div>
      <EvidenceDrawer
        key={selected?.id ?? "none"}
        entry={selected}
        onClose={() => setSelected(undefined)}
      />
    </section>
  );
}
