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
import { Link } from "react-router-dom";
import {
  Heading,
  QueryState,
  Status,
  useApi,
  jsonText,
} from "../../components/Common";
import type { Entry, Page } from "../../types";
const CodeViewer = lazy(() => import("../../components/CodeViewer"));
export function Reviews() {
  const query = useApi<Page<Entry>>("/projects/all/reviews");
  const [review, setReview] = useState<Entry>();
  const artifacts = useApi<Page<Entry>>(
    "/runs/" + review?.runId + "/artifacts",
    !!review,
  );
  const details = review
    ? (JSON.parse(review.content) as {
        Summary?: string;
        summary?: string;
        Findings?: Finding[];
        findings?: Finding[];
      })
    : undefined;
  const findings = (details?.Findings ?? details?.findings ?? []).map(
    (finding) => ({
      severity: finding.Severity ?? finding.severity,
      file: finding.File ?? finding.file,
      line: finding.Line ?? finding.line,
      description: finding.Description ?? finding.description,
      correction: finding.SuggestedCorrection ?? finding.suggestedCorrection,
    }),
  );
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
            <Status value={review.status} />
            <Card title="Findings">
              <Typography.Paragraph className="preserve">
                {details?.Summary ?? details?.summary}
              </Typography.Paragraph>
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
            <Typography.Title level={5}>Repository evidence</Typography.Title>
            {artifacts.data?.items
              .filter(
                (x) => x.name === "CodeDiff" && x.metadata.format === "diff",
              )
              .map((x) => (
                <Suspense key={x.id} fallback="Loading diff viewer…">
                  <CodeViewer content={x.content} />
                </Suspense>
              ))}
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
  const pullRequest = JSON.parse(content) as {
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
