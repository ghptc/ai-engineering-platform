import {
  App,
  Alert,
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Space,
  Switch,
  Tabs,
  Tag,
  Typography,
} from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, useApi } from "../../components/Common";
import type { Agent, Settings as SettingsType } from "../../types";
export function Settings() {
  const status = useApi<SettingsType>("/settings");
  const agents = useApi<Agent[]>("/agents");
  return (
    <>
      <Heading
        eyebrow="PLATFORM CONFIGURATION"
        title="Settings & agents"
        description="Configure model behavior, pricing, and integration prerequisites. Secrets stay on the server."
      />
      <div className="metrics">
        {[
          { label: "OpenAI", configured: status.data?.openAIConfigured },
          { label: "GitHub App", configured: status.data?.githubConfigured },
          {
            label: `Sandbox (${status.data?.sandboxProvider ?? "Remote"})`,
            configured: status.data?.sandboxConfigured,
            ready: status.data?.sandboxReady,
          },
        ].map(({ label, configured, ready }) => (
          <Card key={label} title={label}>
            <Tag color={configured && ready !== false ? "green" : "gold"}>
              {ready === true
                ? "Ready"
                : ready === false && configured
                  ? "Configured; executor unavailable"
                  : configured
                    ? "Configured"
                    : "Configuration required"}
            </Tag>
          </Card>
        ))}
      </div>
      <Alert
        title="Set integration secrets in the deployment environment. Each agent needs an available model and current input/output token prices before execution."
        type="info"
        showIcon
        style={{ marginBottom: 24 }}
      />
      <QueryState loading={agents.isLoading} error={agents.error}>
        <Tabs
          tabPosition="left"
          items={agents.data?.map((agent) => ({
            key: agent.id,
            label: agent.name,
            children: <AgentForm agent={agent} />,
          }))}
        />
      </QueryState>
    </>
  );
}
function AgentForm({ agent }: { agent: Agent }) {
  const { message } = App.useApp();
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (values: unknown) =>
      api("/agents/" + agent.id + "/configuration", "PATCH", values),
    onSuccess: () => {
      void client.invalidateQueries();
      message.success("Agent configuration saved");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card title={agent.name}>
      <Typography.Paragraph type="secondary">
        {agent.description}
      </Typography.Paragraph>
      <Form
        key={agent.id + agent.instructionVersion}
        initialValues={agent}
        layout="vertical"
        onFinish={(values) => mutation.mutate(values)}
      >
        <div className="form-grid">
          <Form.Item label="Model" name="model" rules={[{ required: true }]}>
            <Input placeholder="Available model identifier" />
          </Form.Item>
          <Form.Item
            label="Maximum output tokens"
            name="maxOutputTokens"
            rules={[{ required: true }]}
          >
            <InputNumber min={100} max={16000} />
          </Form.Item>
          <Form.Item
            label="Input price / million tokens (USD)"
            name="inputCostPerMillion"
            rules={[{ required: true }]}
          >
            <InputNumber min={0.001} step={0.1} />
          </Form.Item>
          <Form.Item
            label="Output price / million tokens (USD)"
            name="outputCostPerMillion"
            rules={[{ required: true }]}
          >
            <InputNumber min={0.001} step={0.1} />
          </Form.Item>
        </div>
        <Form.Item label="Enabled" name="enabled" valuePropName="checked">
          <Switch />
        </Form.Item>
        <Form.Item
          label={"System instructions · version " + agent.instructionVersion}
          name="systemInstructions"
        >
          <Input.TextArea rows={8} />
        </Form.Item>
        <Typography.Paragraph>
          Authorized tools: {agent.allowedTools.join(", ")}
        </Typography.Paragraph>
        <Space>
          <Button type="primary" htmlType="submit" loading={mutation.isPending}>
            Save configuration
          </Button>
        </Space>
      </Form>
    </Card>
  );
}
