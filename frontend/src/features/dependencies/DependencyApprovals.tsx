import { useState } from "react";
import {
  Alert,
  App,
  Button,
  Checkbox,
  Collapse,
  Descriptions,
  Empty,
  Input,
  Modal,
  Space,
  Table,
  Tag,
  Typography,
} from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { api } from "../../services/api";
import { QueryState, useApi } from "../../components/Common";
import type { Run, Project } from "../../types";
interface Package {
  name: string;
  version: string;
  development: boolean;
  reason?: string;
  integrity?: string;
  source?: string;
  direct?: boolean;
  license?: string;
  securityStatus?: string;
  hasInstallScript?: boolean;
  installPath?: string;
  alias?: string;
  cacheRequired?: boolean;
  runtimeCached?: boolean;
}
export interface DependencyRequest {
  id: string;
  projectId: string;
  runId: string;
  stage: string;
  agent: string;
  originatingTaskId: string;
  originatingOperationId: string;
  taskIndex: number;
  proposal: {
    version: number;
    ecosystem: string;
    packageManager: string;
    registry: string;
    packages: Package[];
    capabilities: string[];
    securityImplications: string;
    alternatives: string[];
    independentTasks: string[];
    blockedTasks: string[];
    evidenceReferences: string[];
  };
  status: string;
  version: number;
  createdAt: string;
  diagnostic?: string;
  resolution?: {
    planSha256: string;
    packages: Package[];
    manifest: string;
    lock: string;
    cacheSha256: string;
    resolverImage?: string;
  };
  audit: {
    action: string;
    owner: string;
    at: string;
    packageIds: string[];
    registryAccess: boolean;
    reason: string;
  }[];
}
export function DependencyApprovalPanel({
  entry,
}: {
  entry: DependencyRequest;
}) {
  const [action, setAction] = useState<string>();
  const [decisionVersion, setDecisionVersion] = useState(entry.version);
  const begin = (next: string) => {
    setDecisionVersion(entry.version);
    setAction(next);
    setReason("");
    setFailure(undefined);
  };
  const [reason, setReason] = useState("");
  const [network, setNetwork] = useState(false);
  const [selected, setSelected] = useState<React.Key[]>([]);
  const [failure, setFailure] = useState<string>();
  const { message } = App.useApp();
  const cache = useQueryClient();
  const run = useApi<Run>(`/runs/${entry.runId}`, true, 5000);
  const project = useApi<Project>(`/projects/${entry.projectId}`);
  const decided = ["Approved", "Rejected", "ChangesRequested"].includes(
    entry.status,
  );
  const packages = [
    ...new Map(
      (entry.resolution?.packages || entry.proposal.packages).map((p) => [
        p.name + "@" + p.version,
        p,
      ]),
    ).values(),
  ];
  const approved = new Set(
    entry.audit
      .filter((a) => a.action === "Approve")
      .flatMap((a) => a.packageIds),
  );
  const mutate = useMutation({
    mutationFn: () =>
      action === "Resume"
        ? api(`/dependency-approvals/${entry.id}/resume`, "POST", {})
        : api(`/dependency-approvals/${entry.id}/decision`, "POST", {
            version: decisionVersion,
            action,
            packageIds: selected,
            registryAccess:
              ["Resolve", "RetryResolution"].includes(action || "") && network,
            reason,
          }),
    onSuccess: () => {
      setAction(undefined);
      setFailure(undefined);
      setSelected([]);
      void cache.invalidateQueries();
      void message.success(
        "Owner action persisted. Limits and security boundaries remain unchanged.",
      );
    },
    onError: (error) => {
      setFailure(error.message);
      void cache.invalidateQueries();
    },
  });
  const remainingTime = run.data
    ? Math.max(
        0,
        new Date(run.data.startedAt).getTime() +
          run.data.policy.maxDurationMinutes * 60000 -
          Date.now(),
      )
    : null;
  return (
    <section
      role="region"
      className="dependency-approval"
      aria-label={`Dependency approval ${entry.id}`}
    >
      <Space wrap>
        <Tag color={decided ? "blue" : "gold"}>
          {entry.status === "Pending"
            ? "Awaiting Owner Approval"
            : entry.status}
        </Tag>
        <Link to={`/projects/${entry.projectId}`}>
          {project.data?.name || `Project ${entry.projectId}`}
        </Link>
        <Link to={`/executions/${entry.runId}`}>Execution {entry.runId}</Link>
      </Space>
      <Typography.Paragraph>
        {entry.stage} · {entry.agent} · task {entry.taskIndex + 1}
      </Typography.Paragraph>
      {failure && (
        <Alert
          type="error"
          role="alert"
          title="Dependency action failed"
          description={failure}
        />
      )}
      {entry.diagnostic && (
        <Alert
          type="warning"
          title={
            entry.status === "ResolutionFailed"
              ? "Acquisition failed; administrator remediation required"
              : "Reconciliation required"
          }
          description={entry.diagnostic}
        />
      )}
      <Alert
        type="info"
        title="Approval is scoped, not a general installation permission"
        description="Registry resolution needs separate explicit authorization. Agents remain offline. Install scripts, arbitrary sources, shell commands and repository permissions are never enabled. Source permissions apply only after every package in the resolved graph is approved."
      />
      <Descriptions
        size="small"
        column={{ xs: 1, md: 3 }}
        items={[
          {
            key: "registry",
            label: "Registry",
            children: entry.proposal.registry,
          },
          {
            key: "ecosystem",
            label: "Ecosystem / manager",
            children: `${entry.proposal.ecosystem} / ${entry.proposal.packageManager}`,
          },
          {
            key: "cost",
            label: "Recorded run cost / ceiling",
            children: run.data
              ? `$${run.data.cost} / $${run.data.policy.maxBudgetUsd}`
              : "Loading actual run policy",
          },
          {
            key: "tools",
            label: "Tools consumed / maximum",
            children: run.data
              ? `${run.data.toolCalls} / ${run.data.policy.maxToolCalls}`
              : "Unknown",
          },
          {
            key: "remaining",
            label: "Remaining original deadline",
            children:
              remainingTime === null
                ? "Unknown"
                : `${Math.floor(remainingTime / 60000)} minutes (server rechecks)`,
          },
          {
            key: "financial",
            label: "Approval budget effect",
            children:
              "No ceiling increase. Registry operations have no configured package purchase charge; infrastructure costs are unpriced, not certified free.",
          },
        ]}
      />
      <Table
        size="small"
        rowKey={(p) => p.name + "@" + p.version}
        dataSource={packages}
        pagination={{ pageSize: 15, showSizeChanger: false }}
        scroll={{ x: 880 }}
        rowSelection={
          entry.status === "ReadyForReview"
            ? {
                selectedRowKeys: selected,
                onChange: setSelected,
                preserveSelectedRowKeys: true,
                getCheckboxProps: (p) => ({
                  disabled: approved.has(p.name + "@" + p.version),
                }),
              }
            : undefined
        }
        columns={[
          {
            title: "Exact package",
            render: (_, p) => (
              <Typography.Text code>
                {p.name}@{p.version}
              </Typography.Text>
            ),
            width: 240,
          },
          {
            title: "Use",
            render: (_, p) =>
              `${p.development ? "Development" : "Production"} · ${p.direct === false ? "Transitive" : "Direct"}`,
            width: 190,
          },
          {
            title: "Model justification / registry license",
            render: (_, p) => (
              <>
                <Typography.Paragraph>
                  {p.reason ||
                    entry.proposal.packages.find(
                      (r) => r.name === p.name && r.version === p.version,
                    )?.reason ||
                    (p.direct
                      ? "Existing direct dependency retained"
                      : "Required by the resolved dependency graph")}
                </Typography.Paragraph>
                <Typography.Text type="secondary">
                  {p.license || "License unknown pending resolution"}
                </Typography.Text>
              </>
            ),
            width: 220,
          },
          {
            title: "Security",
            render: (_, p) => (
              <>
                {p.securityStatus || "Unknown — not scanned"}
                {p.cacheRequired === false && (
                  <Tag>Optional package for another runtime</Tag>
                )}
                {p.runtimeCached && <Tag>Verified in pinned runtime</Tag>}
                {p.hasInstallScript && (
                  <Tag color="orange">Install script present; disabled</Tag>
                )}
              </>
            ),
          },
          {
            title: "Decision",
            render: (_, p) =>
              approved.has(p.name + "@" + p.version)
                ? "Owner approved"
                : "Not approved",
            width: 130,
          },
        ]}
        expandable={{
          expandedRowRender: (p) => (
            <Typography.Paragraph copyable>
              {p.source || entry.proposal.registry} ·{" "}
              {p.integrity || "Integrity unknown until controlled resolution"}{" "}
              {p.installPath ? ` · ${p.installPath}` : ""}{" "}
              {p.alias ? ` · alias ${p.alias}` : ""}
            </Typography.Paragraph>
          ),
        }}
      />
      <Collapse
        items={[
          {
            key: "risks",
            label: "Justification, alternatives & security implications",
            children: (
              <>
                <Typography.Paragraph>
                  {entry.proposal.securityImplications ||
                    "Unknown security implications; owner review required."}
                </Typography.Paragraph>
                <p>
                  Capabilities:{" "}
                  {entry.proposal.capabilities.join(", ") || "None declared"}
                </p>
                <p>
                  Alternatives:{" "}
                  {entry.proposal.alternatives.join("; ") || "Unknown"}
                </p>
                <p>
                  Independent tasks:{" "}
                  {entry.proposal.independentTasks.join("; ") ||
                    "None established"}
                </p>
                <p>
                  Blocked tasks:{" "}
                  {entry.proposal.blockedTasks.join("; ") || "Affected stage"}
                </p>
                <p>Task: {entry.originatingTaskId}</p>
                <p>Operation: {entry.originatingOperationId}</p>
                <p>
                  Evidence:{" "}
                  {entry.proposal.evidenceReferences.join("; ") ||
                    "No evidence references supplied"}
                </p>
              </>
            ),
          },
          ...(entry.resolution
            ? [
                {
                  key: "plan",
                  label: "Exact source changes & immutable resolution",
                  children: (
                    <>
                      <Typography.Paragraph copyable>
                        Plan {entry.resolution.planSha256}
                      </Typography.Paragraph>
                      <p>
                        Changes: frontend/package.json and
                        frontend/package-lock.json; offline cache attached only
                        to isolated execution workloads.
                      </p>
                      <pre>{entry.resolution.manifest}</pre>
                      <Typography.Paragraph copyable>
                        Cache SHA-256 {entry.resolution.cacheSha256}
                      </Typography.Paragraph>
                      {entry.resolution.resolverImage && (
                        <Typography.Paragraph copyable>
                          Runtime image {entry.resolution.resolverImage}
                        </Typography.Paragraph>
                      )}
                      <p>Lock preview (bounded):</p>
                      <pre>{entry.resolution.lock.slice(0, 16000)}</pre>
                    </>
                  ),
                },
              ]
            : []),
          {
            key: "audit",
            label: `Owner audit history (${entry.audit.length})`,
            children: entry.audit.length ? (
              entry.audit.map((a, i) => (
                <Typography.Paragraph key={i}>
                  {a.at} · {a.owner} · {a.action} · {a.packageIds.length}{" "}
                  packages · registry access{" "}
                  {a.registryAccess ? "authorized" : "not authorized"} ·{" "}
                  {a.reason}
                </Typography.Paragraph>
              ))
            ) : (
              <p>No owner decision exists.</p>
            ),
          },
        ]}
      />
      <Space wrap className="history-controls">
        {entry.status === "Pending" && (
          <Button
            onClick={() => {
              setNetwork(false);
              begin("Resolve");
            }}
          >
            Authorize bounded registry resolution
          </Button>
        )}
        {entry.status === "Resolving" && (
          <>
            <Button onClick={() => begin("Reconcile")}>
              Reconcile existing resolution
            </Button>
          </>
        )}
        {entry.status === "ReadyForReview" && (
          <Button
            type="primary"
            disabled={selected.length === 0}
            onClick={() => begin("Approve")}
          >
            Approve selected packages
          </Button>
        )}
        {!decided && (
          <>
            <Button danger onClick={() => begin("Reject")}>
              Reject request
            </Button>
            <Button onClick={() => begin("RequestChanges")}>
              Request changes
            </Button>
          </>
        )}
        {decided && (
          <Button
            type="primary"
            disabled={run.data?.currentStage !== "AwaitingDependencyApproval"}
            onClick={() => begin("Resume")}
          >
            Resume same execution
          </Button>
        )}
      </Space>
      <Modal
        title={`Owner dependency action: ${action}`}
        open={!!action}
        onCancel={() => setAction(undefined)}
        confirmLoading={mutate.isPending}
        onOk={() => mutate.mutate()}
        okButtonProps={{
          disabled: ["Resolve", "RetryResolution"].includes(action || "")
            ? !network || !reason.trim()
            : action !== "Resume" && !reason.trim(),
        }}
      >
        {["Resolve", "RetryResolution"].includes(action || "") && (
          <Checkbox
            checked={network}
            onChange={(e) => setNetwork(e.target.checked)}
          >
            I authorize the controlled executor to read public metadata and
            tarballs only from registry.npmjs.org, up to 64 MB and 120 seconds,
            for this project resolution. No install scripts, credentials or
            agent network access.
          </Checkbox>
        )}
        {action === "Resume" ? (
          <p>
            Resume the existing stage/task using its preserved checkpoints.
            Original consumed and uncertain usage, deadline, budgets, tool and
            iteration limits remain in effect. Rejection resumes only to seek a
            permitted alternative.
          </p>
        ) : (
          <Input.TextArea
            aria-label="Dependency decision reason"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            maxLength={2000}
            rows={4}
          />
        )}
        {failure && <Alert type="error" title={failure} />}
      </Modal>
    </section>
  );
}
export function DependencyApprovals({
  projectId,
  runId,
}: {
  projectId?: string;
  runId?: string;
}) {
  const params = new URLSearchParams();
  if (projectId) params.set("projectId", projectId);
  if (runId) params.set("runId", runId);
  type Summary = Pick<
    DependencyRequest,
    | "id"
    | "projectId"
    | "runId"
    | "stage"
    | "agent"
    | "status"
    | "version"
    | "createdAt"
  > & { packages: Package[] };
  const query = useApi<Summary[]>(
    "/dependency-approvals?" + params,
    true,
    5000,
  );
  const [selectedId, setSelectedId] = useState<string>();
  const items = Array.isArray(query.data) ? query.data : [];
  const activeId = selectedId || (items.length === 1 ? items[0].id : undefined);
  const detail = useApi<DependencyRequest>(
    "/dependency-approvals/" + activeId,
    !!activeId,
    5000,
  );
  return (
    <>
      <Typography.Title level={4}>Dependency authorizations</Typography.Title>
      <QueryState loading={query.isLoading} error={query.error}>
        {items.length === 0 ? (
          <Empty description="No dependency authorization requests. No package permissions have been granted through this workflow." />
        ) : (
          <>
            {items.length > 1 && (
              <Table
                size="small"
                rowKey="id"
                dataSource={items}
                pagination={{ pageSize: 10, showSizeChanger: false }}
                scroll={{ x: 760 }}
                columns={[
                  { title: "Status", dataIndex: "status", width: 180 },
                  {
                    title: "Stage / agent",
                    render: (_, r) => `${r.stage} · ${r.agent}`,
                  },
                  {
                    title: "Requested packages",
                    ellipsis: true,
                    render: (_, r) =>
                      r.packages
                        .map((p) => p.name + "@" + p.version)
                        .join(", "),
                  },
                  {
                    title: "Review",
                    render: (_, r) => (
                      <Button size="small" onClick={() => setSelectedId(r.id)}>
                        Review request
                      </Button>
                    ),
                    width: 130,
                  },
                ]}
              />
            )}
            {items.length === 100 && (
              <Alert
                type="info"
                title="Showing up to 100 requests, pending first. Open a project to narrow the history."
              />
            )}
            {activeId && (
              <QueryState loading={detail.isLoading} error={detail.error}>
                {detail.data && (
                  <DependencyApprovalPanel
                    key={detail.data.id}
                    entry={detail.data}
                  />
                )}
              </QueryState>
            )}
          </>
        )}
      </QueryState>
    </>
  );
}
