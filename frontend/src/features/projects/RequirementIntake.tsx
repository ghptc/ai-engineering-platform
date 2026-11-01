import { useState } from "react";
import {
  Alert,
  App,
  Button,
  Collapse,
  Drawer,
  Empty,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api, ApiError } from "../../services/api";
import { QueryState, useApi } from "../../components/Common";
import type {
  RequirementScope,
  ScopedRequirement,
  ScopeHistory,
} from "./requirementScope";

const states = [
  "NotStarted",
  "PartiallyImplemented",
  "ImplementedUnverified",
  "Blocked",
  "OutOfScope",
];
const roles = [
  "BusinessAnalyst",
  "Architect",
  "BackendEngineer",
  "FrontendEngineer",
  "TestEngineer",
  "QAEngineer",
  "CodeReviewer",
];
const lines = (s: string) =>
  s
    .split("\n")
    .map((x) => x.trim())
    .filter(Boolean);
export function RequirementIntake({ projectId }: { projectId: string }) {
  const path = `/projects/${projectId}/requirement-scopes`;
  const query = useApi<RequirementScope | null>(path + "/current");
  const [historyBefore, setHistoryBefore] = useState<string>();
  const history = useApi<ScopeHistory>(
    path + (historyBefore ? `?before=${historyBefore}` : ""),
  );
  const [historicalRevision, setHistoricalRevision] = useState<string>();
  const historical = useApi<RequirementScope>(
    path + "/" + historicalRevision,
    !!historicalRevision,
  );
  const scope = historicalRevision ? historical.data : query.data;
  const [search, setSearch] = useState("");
  const [filter, setFilter] = useState("all");
  const [reviewRevision, setReviewRevision] = useState<string>();
  const [importRevision, setImportRevision] = useState<string>();
  const [selected, setSelected] = useState<ScopedRequirement>();
  const [file, setFile] = useState<File>();
  const [key, setKey] = useState("requirements");
  const [confirmation, setConfirmation] = useState(false);
  const [failure, setFailure] = useState<string>();
  const [sourceOffset, setSourceOffset] = useState(0);
  const [form] = Form.useForm();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const refresh = () => {
    void cache.invalidateQueries({
      predicate: (q) => String(q.queryKey[0]).startsWith(path),
    });
    setHistoryBefore(undefined);
  };
  const upload = useMutation({
    mutationFn: async () => {
      if (
        !file ||
        file.size === 0 ||
        file.size > 750000 ||
        !/\.(md|txt|docx)$/i.test(file.name)
      )
        throw new Error(
          "Choose a .md, .txt or .docx file, up to 750000 bytes.",
        );
      const params = new URLSearchParams({
        documentKey: key,
        fileName: file.name,
      });
      if (importRevision) params.set("expectedRevision", importRevision);
      const response = await fetch("/api/v1" + path + "/import?" + params, {
        method: "POST",
        credentials: "same-origin",
        headers: {
          "Content-Type": "application/octet-stream",
          "X-Requested-With": "AIEngineeringPlatform",
        },
        body: file,
      });
      if (!response.ok) {
        const error = await response.json().catch(() => ({}));
        throw new ApiError(
          response.status,
          error.detail || error.title || "Import failed",
        );
      }
      return response.json() as Promise<RequirementScope>;
    },
    onSuccess: () => {
      setFailure(undefined);
      setConfirmation(false);
      setHistoricalRevision(undefined);
      setSourceOffset(0);
      refresh();
      void message.success(
        "Draft scope imported. No execution or model processing was started.",
      );
    },
    onError: (e) => {
      setConfirmation(false);
      setFailure(e.message);
    },
  });
  const review = useMutation({
    mutationFn: (values: Record<string, unknown>) => {
      if (!selected || !scope || !reviewRevision || historicalRevision)
        throw new Error("Select a current requirement.");
      const { criteria, componentLines, ...fields } = values;
      return api(
        path + `/${reviewRevision}/requirements/${selected.id}/review`,
        "POST",
        {
          ...fields,
          acceptanceCriteria: lines(String(criteria || "")),
          components: lines(String(componentLines || "")),
          priority: values.priority ?? null,
          milestone: values.milestone || null,
          dependencies: values.dependencies || [],
          roles: values.roles || [],
          verificationStrategy: values.verificationStrategy || "",
          reviewNote: values.reviewNote || "",
        },
      );
    },
    onSuccess: () => {
      setSelected(undefined);
      setFailure(undefined);
      refresh();
      void message.success("Review saved as a new scope revision.");
    },
    onError: (e) => setFailure(e.message),
  });
  const entries =
    scope?.requirements.filter(
      (r) =>
        (filter === "all" ||
          (filter === "unreviewed"
            ? !r.ownerReviewed
            : r.implementationState === filter)) &&
        `${r.id} ${r.description} ${r.sources.map((s) => s.originalLabel || "").join(" ")}`
          .toLowerCase()
          .includes(search.toLowerCase()),
    ) || [];
  function open(row: ScopedRequirement) {
    setFailure(undefined);
    setReviewRevision(scope?.revision);
    setSelected(row);
    form.setFieldsValue({
      ...row,
      criteria: row.acceptanceCriteria.join("\n"),
      componentLines: row.components.join("\n"),
    });
  }
  return (
    <div className="requirement-intake">
      <Typography.Title level={4}>
        Requirements intake &amp; traceability
      </Typography.Title>
      <Alert
        type="info"
        showIcon
        title="Draft scope only"
        description="Imports do not authorize spending or implementation. No document is sent to a model. Owner review does not certify implementation. Estimates and financial approvals arrive in later M6 phases."
      />
      {failure && (
        <Alert
          type="error"
          role="alert"
          title="Requirements action failed"
          description={failure}
          closable
          onClose={() => setFailure(undefined)}
        />
      )}
      <Space wrap className="intake-controls">
        <label htmlFor="requirements-file">
          Document{" "}
          <input
            id="requirements-file"
            aria-label="Requirements document"
            type="file"
            accept=".md,.txt,.docx"
            onChange={(e) => setFile(e.target.files?.[0])}
          />
        </label>
        <Input
          aria-label="Stable document key"
          value={key}
          onChange={(e) => setKey(e.target.value)}
          maxLength={80}
          placeholder="Stable document key"
          style={{ width: 200 }}
        />
        <Button
          type="primary"
          disabled={
            !file ||
            query.isLoading ||
            !!query.error ||
            !/^[a-zA-Z0-9][a-zA-Z0-9_.-]{0,79}$/.test(key)
          }
          loading={upload.isPending}
          onClick={() => {
            setImportRevision(query.data?.revision);
            setConfirmation(true);
          }}
        >
          Import draft scope
        </Button>
      </Space>
      <Typography.Paragraph type="secondary">
        UTF-8 Markdown/TXT or macro-free DOCX, maximum 750000 bytes. PDF,
        scanned documents and legacy Word are unavailable. Reuse the same
        document key to preserve explicitly labeled requirement IDs across
        revisions.
      </Typography.Paragraph>
      <QueryState
        loading={query.isLoading || historical.isFetching}
        error={query.error || historical.error}
      >
        {!scope ? (
          <Empty description="No imported requirements. Upload a supported document to create a reviewable draft scope." />
        ) : (
          <>
            <Space wrap>
              <Tag>Scope v{scope.version}</Tag>
              <Tag>{scope.requirements.length} requirements</Tag>
              <Tag>
                {scope.requirements.filter((x) => x.ownerReviewed).length}{" "}
                reviewed
              </Tag>
              <Tag color={scope.readyForEstimation ? "green" : "gold"}>
                {scope.readyForEstimation
                  ? "Ready for estimation · not approved"
                  : "Owner review needed"}
              </Tag>
            </Space>
            <Typography.Paragraph copyable code>
              {scope.revision}
            </Typography.Paragraph>
            {historicalRevision && (
              <Alert
                type="warning"
                title="Read-only historical scope"
                action={
                  <Button
                    onClick={() => {
                      setHistoricalRevision(undefined);
                      setSelected(undefined);
                    }}
                  >
                    Return to current
                  </Button>
                }
              />
            )}
            <Space wrap className="intake-controls">
              <Input.Search
                aria-label="Search imported requirements"
                placeholder="Search ID, source label or description"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                style={{ width: 280, maxWidth: "100%" }}
              />
              <Select
                aria-label="Requirement state filter"
                value={filter}
                onChange={setFilter}
                options={["all", "unreviewed", ...states].map((value) => ({
                  value,
                  label: value,
                }))}
                style={{ width: 200 }}
              />
            </Space>
            <Table
              size="small"
              rowKey="id"
              dataSource={entries}
              pagination={{
                pageSize: 20,
                showSizeChanger: false,
                showTotal: (total) => `${total} requirements`,
              }}
              scroll={{ x: 760 }}
              columns={[
                {
                  title: "Source / requirement",
                  key: "source",
                  width: 185,
                  render: (_, r) => (
                    <>
                      <Typography.Text code>
                        {r.sources[0]?.originalLabel || r.id.slice(0, 16)}
                      </Typography.Text>
                      <br />
                      <Typography.Text type="secondary">
                        {r.sources[0]?.location}
                      </Typography.Text>
                    </>
                  ),
                },
                {
                  title: "Description",
                  dataIndex: "description",
                  ellipsis: true,
                },
                {
                  title: "State",
                  dataIndex: "implementationState",
                  width: 180,
                },
                {
                  title: "Review",
                  width: 130,
                  render: (_, r) => (
                    <Button size="small" onClick={() => open(r)}>
                      {historicalRevision
                        ? "View"
                        : r.ownerReviewed
                          ? "Edit review"
                          : "Review"}
                    </Button>
                  ),
                },
              ]}
            />
            <Collapse
              items={[
                {
                  key: "source",
                  label: "Source document & extraction notes",
                  children: (
                    <>
                      <Typography.Paragraph>
                        {scope.document.fileName} · {scope.document.mediaType}
                      </Typography.Paragraph>
                      <Typography.Paragraph copyable code>
                        {scope.document.sha256}
                      </Typography.Paragraph>
                      {scope.warnings.map((w, i) => (
                        <Typography.Paragraph key={i}>{w}</Typography.Paragraph>
                      ))}
                      <pre
                        style={{
                          whiteSpace: "pre-wrap",
                          maxHeight: 320,
                          overflow: "auto",
                        }}
                      >
                        {scope.document.extractedText.slice(
                          sourceOffset,
                          sourceOffset + 8000,
                        )}
                      </pre>
                      <Space>
                        <Button
                          disabled={sourceOffset === 0}
                          onClick={() =>
                            setSourceOffset(Math.max(0, sourceOffset - 8000))
                          }
                        >
                          Previous source section
                        </Button>
                        <Button
                          disabled={
                            sourceOffset + 8000 >=
                            scope.document.extractedText.length
                          }
                          onClick={() => setSourceOffset(sourceOffset + 8000)}
                        >
                          Next source section
                        </Button>
                      </Space>
                    </>
                  ),
                },
              ]}
            />
          </>
        )}
      </QueryState>
      <Collapse
        items={[
          {
            key: "history",
            label: "Immutable scope history",
            children: (
              <>
                <QueryState loading={history.isLoading} error={history.error}>
                  <Space direction="vertical" className="scope-history">
                    {history.data?.items.map((h) => (
                      <Button
                        key={h.revision}
                        type="link"
                        onClick={() => {
                          setHistoricalRevision(h.revision);
                          setSourceOffset(0);
                          setSelected(undefined);
                        }}
                      >
                        v{h.version} · {h.count} requirements · {h.reason}
                      </Button>
                    ))}
                    <Space>
                      <Button
                        disabled={!historyBefore}
                        onClick={() => setHistoryBefore(undefined)}
                      >
                        Latest revisions
                      </Button>
                      <Button
                        disabled={!history.data?.nextBefore}
                        onClick={() =>
                          setHistoryBefore(
                            history.data?.nextBefore || undefined,
                          )
                        }
                      >
                        Older revisions
                      </Button>
                    </Space>
                  </Space>
                </QueryState>
              </>
            ),
          },
        ]}
      />
      <Drawer
        title="Requirement review & source references"
        open={!!selected}
        onClose={() => setSelected(undefined)}
        size="large"
        styles={{ wrapper: { maxWidth: "100vw" } }}
      >
        {selected && (
          <>
            {failure && (
              <Alert
                type="error"
                role="alert"
                title="Review was not saved"
                description={failure}
                action={
                  <Button
                    onClick={() => {
                      setSelected(undefined);
                      refresh();
                    }}
                  >
                    Reload scope
                  </Button>
                }
              />
            )}
            <Typography.Paragraph copyable code>
              {selected.id}
            </Typography.Paragraph>
            {selected.warnings.map((w, i) => (
              <Alert key={i} type="warning" title={w} />
            ))}
            <Collapse
              items={[
                {
                  key: "references",
                  label: `${selected.sources.length} original source reference(s)`,
                  children: selected.sources.map((s, i) => (
                    <div key={i}>
                      <Typography.Paragraph copyable>
                        {s.documentSha256} · {s.location}
                      </Typography.Paragraph>
                      <pre style={{ whiteSpace: "pre-wrap" }}>{s.text}</pre>
                    </div>
                  )),
                },
              ]}
            />
            <Form
              form={form}
              layout="vertical"
              disabled={!!historicalRevision}
              onFinish={(v) => review.mutate(v)}
            >
              <Form.Item
                name="description"
                label="Requirement description"
                rules={[{ required: true }]}
              >
                <Input.TextArea rows={4} maxLength={8000} />
              </Form.Item>
              <Form.Item
                name="criteria"
                label="Acceptance criteria (one per line)"
              >
                <Input.TextArea rows={4} />
              </Form.Item>
              <Form.Item
                name="priority"
                label="Priority (1 highest, blank unknown)"
              >
                <InputNumber min={1} max={5} />
              </Form.Item>
              <Form.Item name="classification" label="Classification">
                <Select
                  options={["Unclassified", "Functional", "NonFunctional"].map(
                    (value) => ({ value }),
                  )}
                />
              </Form.Item>
              <Form.Item name="complexity" label="Owner complexity assessment">
                <Select
                  options={[
                    "Unknown",
                    "Small",
                    "Medium",
                    "Large",
                    "ExtraLarge",
                  ].map((value) => ({ value }))}
                />
              </Form.Item>
              <Form.Item
                name="implementationState"
                label="Implementation state (unverified owner assessment)"
              >
                <Select options={states.map((value) => ({ value }))} />
              </Form.Item>
              <Form.Item name="dependencies" label="Requirement dependencies">
                <Select
                  mode="multiple"
                  options={scope?.requirements
                    .filter((r) => r.id !== selected.id)
                    .map((r) => ({
                      value: r.id,
                      label: r.sources[0]?.originalLabel || r.id,
                    }))}
                />
              </Form.Item>
              <Form.Item
                name="componentLines"
                label="Architectural components (one per line)"
              >
                <Input.TextArea />
              </Form.Item>
              <Form.Item name="milestone" label="Proposed milestone">
                <Input maxLength={120} />
              </Form.Item>
              <Form.Item name="roles" label="Proposed engineering roles">
                <Select
                  mode="multiple"
                  options={roles.map((value) => ({ value }))}
                />
              </Form.Item>
              <Form.Item
                name="verificationStrategy"
                label="Verification strategy"
              >
                <Input.TextArea maxLength={4000} />
              </Form.Item>
              <Form.Item name="reviewNote" label="Review or exclusion reason">
                <Input.TextArea maxLength={2000} />
              </Form.Item>
              {!historicalRevision && (
                <Button
                  type="primary"
                  htmlType="submit"
                  loading={review.isPending}
                >
                  Save owner review
                </Button>
              )}
            </Form>
          </>
        )}
      </Drawer>
      <Modal
        title="Import a new draft scope revision?"
        open={confirmation}
        onCancel={() => setConfirmation(false)}
        confirmLoading={upload.isPending}
        onOk={() => upload.mutate()}
        okText="Import draft"
      >
        <p>
          All extracted candidates need owner review. Previous revisions and
          original references remain available. Requirements absent from this
          document remain visible as blocked until reconciled or explicitly
          excluded. No model call, execution or financial authorization will
          occur.
        </p>
      </Modal>
    </div>
  );
}
