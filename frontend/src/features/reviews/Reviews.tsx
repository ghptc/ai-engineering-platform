import { lazy, Suspense, useState } from "react";
import {
  Button,
  Card,
  Empty,
  Modal,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import { parse, text } from "../../lib/engineering";
import {
  Markdown,
  ReadableEntry,
  JsonDetails,
} from "../../components/Readable";
import { Link } from "react-router-dom";
import {
  Heading,
  QueryState,
  Status,
  useApi,
  jsonText,
} from "../../components/Common";
import type { RecordWindow } from "../../hooks/useLiveEvents";
import type { Entry, Page, Project, Run } from "../../types";
const CodeViewer = lazy(() => import("../../components/CodeViewer"));
export function Reviews() {
  const [before, setBefore] = useState<number>();
  const query = useApi<RecordWindow>(
    `/reviews/feed?limit=30${before != null ? `&before=${before}` : ""}`,
  );
  const [review, setReview] = useState<Entry>();
  const artifacts = useApi<Page<Entry>>(
    "/runs/" + review?.runId + "/artifacts?limit=500",
    !!review,
  );
  const run = useApi<Run>(`/runs/${review?.runId}`, !!review?.runId);
  const project = useApi<Project>(
    `/projects/${run.data?.projectId}`,
    !!run.data?.projectId,
  );
  const diff =
    artifacts.data?.items.find(
      (a) => a.id === review?.metadata?.diffArtifactId,
    ) ??
    artifacts.data?.items
      .filter(
        (a) =>
          a.name === "CodeDiff" &&
          a.metadata.format === "diff" &&
          new Date(a.createdAt) <= new Date(review?.createdAt ?? 0),
      )
      .at(-1);
  const pr =
    artifacts.data?.items.find(
      (a) => a.id === review?.metadata?.prArtifactId,
    ) ?? artifacts.data?.items.find((a) => a.name === "PullRequest");
  const details = review
    ? (parse(review.content) as {
        Summary?: string;
        summary?: string;
        Findings?: Finding[];
        findings?: Finding[];
      })
    : undefined;
  const findings = (
    Array.isArray(details?.Findings)
      ? details.Findings
      : Array.isArray(details?.findings)
        ? details.findings
        : []
  ).map((finding) => ({
    severity: finding.Severity ?? finding.severity,
    file: finding.File ?? finding.file,
    line: finding.Line ?? finding.line,
    description: finding.Description ?? finding.description,
    correction: finding.SuggestedCorrection ?? finding.suggestedCorrection,
  }));
  return (
    <>
      <Heading
        eyebrow="QUALITY GATES"
        title="Code reviews"
        description="Review findings come from actual repository changes. Local reviews and published GitHub reviews are distinct."
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <Table
          rowKey="id"
          dataSource={query.data?.items}
          columns={[
            { title: "Review", dataIndex: "name" },
            {
              title: "Decision",
              dataIndex: "status",
              render: (value: string) => <Status value={value} />,
            },
            {
              title: "Execution",
              dataIndex: "runId",
              render: (value: string) => (
                <Link to={"/executions/" + value}>{value.slice(0, 12)}</Link>
              ),
            },
            {
              title: "Inspect",
              render: (_: unknown, entry: Entry) => (
                <Button onClick={() => setReview(entry)}>
                  View findings & diff
                </Button>
              ),
            },
          ]}
        />
      </QueryState>
      <Space className="history-controls">
        <Button
          disabled={!query.data?.hasMore}
          onClick={() => setBefore(query.data?.nextBefore)}
        >
          Older reviews
        </Button>
        <Button disabled={before == null} onClick={() => setBefore(undefined)}>
          Latest reviews
        </Button>
      </Space>
      <Modal
        title="Code review"
        open={!!review}
        onCancel={() => setReview(undefined)}
        footer={null}
        width="90vw"
        destroyOnHidden
      >
        {review && (
          <Space orientation="vertical" style={{ width: "100%" }}>
            <Space wrap>
              <Status value={review.status} />
              <Tag>Internal AI review</Tag>
              <Link to={"/executions/" + review.runId}>
                Execution workspace ↗
              </Link>
            </Space>
            <Typography.Paragraph>
              {project.data?.repositoryConnection
                ? project.data.repositoryConnection.owner +
                  "/" +
                  project.data.repositoryConnection.repository
                : "Repository metadata unavailable"}
            </Typography.Paragraph>
            {pr && (
              <Typography.Title level={5}>
                {text(
                  parse(pr.content),
                  "title",
                  "Pull request #" + text(parse(pr.content), "number"),
                )}
              </Typography.Title>
            )}
            {diff && (
              <Typography.Paragraph>
                Files in reviewed diff:{" "}
                {[...diff.content.matchAll(/^diff --git a\/(.+) b\/(.+)$/gm)]
                  .map((m) => m[2])
                  .join(", ")}
              </Typography.Paragraph>
            )}
            <Card title="Findings">
              <Markdown
                content={
                  details?.Summary ??
                  details?.summary ??
                  (Object.keys(parse(review.content)).length
                    ? review.name
                    : review.content.slice(0, 3000))
                }
              />
              {findings.length ? (
                <Table
                  rowKey={(_, index) => String(index)}
                  size="small"
                  pagination={false}
                  dataSource={findings}
                  columns={[
                    {
                      title: "Severity",
                      dataIndex: "severity",
                      render: (value: string) => (
                        <Tag
                          color={
                            value === "Critical" || value === "High"
                              ? "red"
                              : "gold"
                          }
                        >
                          {value}
                        </Tag>
                      ),
                    },
                    { title: "File", dataIndex: "file" },
                    {
                      title: "Line",
                      dataIndex: "line",
                      render: (value?: number) => value ?? "Not specified",
                    },
                    { title: "Finding", dataIndex: "description" },
                    { title: "Suggested correction", dataIndex: "correction" },
                  ]}
                />
              ) : (
                <Empty description="No code findings recorded for this review." />
              )}
            </Card>
            <Card title="Test evidence & GitHub publication">
              {artifacts.data?.items
                .filter(
                  (x) => x.name === "TestReport" || x.name === "CodeReview",
                )
                .map((x) => (
                  <div className="compact-row" key={x.id}>
                    <div>
                      <strong>{x.name}</strong>
                      <ReadableEntry entry={x} />
                    </div>
                  </div>
                ))}
            </Card>
            <JsonDetails content={review.content} />
            <Typography.Title level={5}>Repository evidence</Typography.Title>
            {diff ? (
              <Suspense fallback="Loading diff viewer…">
                <CodeViewer content={diff.content} />
              </Suspense>
            ) : (
              <Empty description="No original diff artifact is available for this review. Inspect its execution records." />
            )}
            {artifacts.data?.items
              .filter((x) => x.name === "PullRequest")
              .map((x) => (
                <PullRequestLink key={x.id} content={x.content} />
              ))}
          </Space>
        )}
      </Modal>
    </>
  );
}

interface Finding {
  Severity?: string;
  severity?: string;
  File?: string;
  file?: string;
  Line?: number;
  line?: number;
  Description?: string;
  description?: string;
  SuggestedCorrection?: string;
  suggestedCorrection?: string;
}
function PullRequestLink({ content }: { content: string }) {
  const pullRequest = parse(content) as {
    html_url?: string;
    number?: number;
  };
  return (
    <Card title={"Pull request #" + pullRequest.number}>
      {pullRequest.html_url?.startsWith("https://github.com/") ? (
        <a
          href={pullRequest.html_url}
          target="_blank"
          rel="noopener noreferrer"
        >
          Open pull request on GitHub
        </a>
      ) : (
        <pre>{jsonText(content)}</pre>
      )}
    </Card>
  );
}
