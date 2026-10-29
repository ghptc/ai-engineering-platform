import { useState } from "react";
import {
  App,
  Alert,
  Button,
  Card,
  Form,
  InputNumber,
  Space,
  Statistic,
  Table,
  Tabs,
  Typography,
} from "antd";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Heading, QueryState, useApi } from "../../components/Common";
import { api } from "../../services/api";
import { human } from "../../lib/engineering";
import type { Agent, Page, Project, Run } from "../../types";
interface Group {
  key: string;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
  calls: number;
}
interface UsageItem {
  id: string;
  createdAt: string;
  projectId: string;
  runId?: string;
  agent: string;
  model: string;
  responseId: string;
  inputTokens: number;
  outputTokens: number;
  estimatedCostUsd: number;
}
interface Ledger {
  total: Group;
  projects: Group[];
  executions: Group[];
  agents: Group[];
  models: Group[];
  days: Group[];
  items: UsageItem[];
  offset: number;
  hasMore: boolean;
  costBasis: string;
}
const money = (value: number) => `$${value.toFixed(4)}`;
function Pricing({ agent }: { agent: Agent }) {
  const { message } = App.useApp();
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (values: unknown) =>
      api(`/agents/${agent.id}/pricing`, "PATCH", values),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ["/agents"] });
      message.success(
        "Model pricing saved; historical cost estimates are retained",
      );
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card
      size="small"
      title={`${agent.name} · ${agent.model || "Model not selected"}`}
    >
      <Form
        layout="inline"
        className="pricing-form"
        key={agent.instructionVersion}
        initialValues={agent}
        onFinish={(v) => mutation.mutate(v)}
      >
        <Form.Item
          label="Input USD / million tokens"
          name="inputCostPerMillion"
          rules={[{ required: true }]}
        >
          <InputNumber min={0.001} step={0.1} />
        </Form.Item>
        <Form.Item
          label="Output USD / million tokens"
          name="outputCostPerMillion"
          rules={[{ required: true }]}
        >
          <InputNumber min={0.001} step={0.1} />
        </Form.Item>
        <Button htmlType="submit" type="primary" loading={mutation.isPending}>
          Save prices
        </Button>
      </Form>
    </Card>
  );
}
export function Usage() {
  const [offset, setOffset] = useState(0);
  const ledger = useApi<Ledger>(
    `/usage/ledger?offset=${offset}&limit=25`,
    true,
    30000,
  );
  const projects = useApi<Page<Project>>("/projects?limit=500");
  const runs = useApi<Page<Run>>("/projects/all/runs?limit=50");
  const agents = useApi<Agent[]>("/agents");
  const name = (id: string) =>
    projects.data?.items.find((p) => p.id === id)?.name ?? id;
  const columns = [
    { title: "Group", dataIndex: "key", render: (v: string) => human(name(v)) },
    { title: "Input tokens", dataIndex: "inputTokens" },
    { title: "Output tokens", dataIndex: "outputTokens" },
    { title: "Calls", dataIndex: "calls" },
    {
      title: "Estimated API cost",
      dataIndex: "estimatedCostUsd",
      render: money,
    },
  ];
  return (
    <>
      <Heading
        eyebrow="API USAGE & LIMITS"
        title="Usage & Costs"
        description="Persisted provider usage, estimated model costs and enforced execution limits."
      />
      <QueryState loading={ledger.isLoading} error={ledger.error}>
        <Alert
          type="info"
          showIcon
          title="Costs are estimates, not provider invoices"
          description={ledger.data?.costBasis}
          className="section-gap"
        />
        <div className="metrics">
          <Card>
            <Statistic
              title="Total API cost · estimated"
              value={ledger.data?.total.estimatedCostUsd ?? 0}
              precision={4}
              prefix="$"
            />
          </Card>
          <Card>
            <Statistic
              title="Input tokens · recorded"
              value={ledger.data?.total.inputTokens ?? 0}
            />
          </Card>
          <Card>
            <Statistic
              title="Output tokens · recorded"
              value={ledger.data?.total.outputTokens ?? 0}
            />
          </Card>
          <Card title="Confirmed billing">
            <Typography.Text>
              Unavailable — no billing reconciliation source configured.
            </Typography.Text>
          </Card>
        </div>
        <Tabs
          items={[
            ...["Projects", "Executions", "Agents", "Models"].map((key) => ({
              key,
              label: `By ${key.toLowerCase()}`,
              children: (
                <Table
                  rowKey="key"
                  size="small"
                  scroll={{ x: 650 }}
                  dataSource={
                    ledger.data?.[
                      key.toLowerCase() as
                        "projects" | "executions" | "agents" | "models"
                    ]
                  }
                  columns={columns}
                />
              ),
            })),
            {
              key: "trends",
              label: "Usage trends",
              children: (
                <Table
                  size="small"
                  rowKey="key"
                  dataSource={ledger.data?.days}
                  columns={[
                    { title: "UTC day", dataIndex: "key" },
                    ...columns.slice(1),
                  ]}
                />
              ),
            },
            {
              key: "pricing",
              label: "Model pricing",
              children: (
                <Space orientation="vertical" style={{ width: "100%" }}>
                  <Typography.Paragraph>
                    Prices affect future request reservations and estimates.
                    Model selection and behavioral settings remain in{" "}
                    <Link to="/settings">Settings & Agents</Link>.
                  </Typography.Paragraph>
                  {agents.data?.map((a) => (
                    <Pricing key={a.id} agent={a} />
                  ))}
                </Space>
              ),
            },
          ]}
        />
        <Card title="Execution budgets & alerts" className="section-gap">
          <Typography.Paragraph type="secondary">
            Latest 50 executions. Limits are enforced by the backend; edit
            policy in the project configuration.
          </Typography.Paragraph>
          <Table
            rowKey="id"
            size="small"
            scroll={{ x: 650 }}
            dataSource={runs.data?.items}
            columns={[
              {
                title: "Execution",
                render: (_, r: Run) => (
                  <Link to={`/executions/${r.id}`}>
                    {r.tasks?.[r.taskIndex]?.title ?? r.id.slice(0, 12)}
                  </Link>
                ),
              },
              {
                title: "Project",
                dataIndex: "projectId",
                render: (v: string) => (
                  <Link to={`/projects/${v}`}>{name(v)}</Link>
                ),
              },
              { title: "Estimated spend", dataIndex: "cost", render: money },
              {
                title: "Run limit",
                render: (_, r: Run) => money(r.policy?.maxBudgetUsd ?? 0),
              },
              {
                title: "Budget alert",
                render: (_, r: Run) =>
                  r.cost >= (r.policy?.maxBudgetUsd ?? Infinity)
                    ? "Limit reached"
                    : r.cost >= 0.8 * (r.policy?.maxBudgetUsd ?? Infinity)
                      ? "Approaching limit"
                      : "Within configured limit",
              },
            ]}
          />
        </Card>
        <Card title="Detailed usage history" className="section-gap">
          <Table
            rowKey="id"
            pagination={false}
            size="small"
            scroll={{ x: 900 }}
            dataSource={ledger.data?.items}
            columns={[
              {
                title: "Recorded",
                dataIndex: "createdAt",
                render: (v: string) => new Date(v).toLocaleString(),
              },
              { title: "Agent", dataIndex: "agent", render: human },
              { title: "Model", dataIndex: "model" },
              {
                title: "Execution",
                dataIndex: "runId",
                render: (v?: string) =>
                  v ? (
                    <Link to={`/executions/${v}`}>{v.slice(0, 10)}</Link>
                  ) : (
                    "Client conversation"
                  ),
              },
              { title: "Input", dataIndex: "inputTokens" },
              { title: "Output", dataIndex: "outputTokens" },
              {
                title: "Estimated USD",
                dataIndex: "estimatedCostUsd",
                render: money,
              },
              {
                title: "Provider receipt",
                dataIndex: "responseId",
                render: (v: string) => (
                  <Typography.Text copyable={{ text: v }}>
                    {v.slice(0, 14)}…
                  </Typography.Text>
                ),
              },
            ]}
          />
          <Space className="history-controls">
            <Button
              disabled={!offset}
              onClick={() => setOffset(Math.max(0, offset - 25))}
            >
              Newer usage
            </Button>
            <Button
              disabled={!ledger.data?.hasMore}
              onClick={() => setOffset(offset + 25)}
            >
              Older usage
            </Button>
          </Space>
        </Card>
      </QueryState>
    </>
  );
}
