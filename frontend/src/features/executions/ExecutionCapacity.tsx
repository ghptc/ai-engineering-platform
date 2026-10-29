import { useState } from "react";
import {
  Alert,
  App,
  Button,
  Card,
  Form,
  Input,
  InputNumber,
  Modal,
  Select,
  Table,
  Typography,
} from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { human } from "../../lib/engineering";
import type { Policy, Run } from "../../types";
export const presets: Record<string, Partial<Policy>> = {
  "Quick Fix": {
    maxToolCalls: 30,
    maxIterations: 3,
    maxRetries: 1,
    maxDurationMinutes: 30,
    maxBudgetUsd: 2,
  },
  "Standard Development": {
    maxToolCalls: 100,
    maxIterations: 5,
    maxRetries: 2,
    maxDurationMinutes: 90,
    maxBudgetUsd: 5,
  },
  "Extended Autonomous Development": {
    maxToolCalls: 300,
    maxIterations: 10,
    maxRetries: 3,
    maxDurationMinutes: 180,
    maxBudgetUsd: 10,
  },
};
export function ExecutionCapacity({ run }: { run: Run }) {
  const [open, setOpen] = useState(false);
  const [form] = Form.useForm();
  const client = useQueryClient();
  const { message } = App.useApp();
  const extend = useMutation({
    mutationFn: (values: { reason: string; policy: Policy }) =>
      api(`/runs/${run.id}/limits`, "POST", {
        ...values,
        policy: { ...run.policy, ...values.policy },
        version: run.version,
      }),
    onSuccess: () => {
      setOpen(false);
      void client.invalidateQueries();
      message.success(
        "Owner-approved limits saved. Resume when ready; checkpoints are retained.",
      );
    },
    onError: (error) => message.error(error.message),
  });
  const ratio = run.toolCalls / run.policy.maxToolCalls;
  return (
    <Card
      size="small"
      title={`Execution capacity · ${run.toolCalls} / ${run.policy.maxToolCalls} tools`}
      className="section-gap"
      extra={
        !["Completed", "Cancelled"].includes(run.currentStage) && (
          <Button onClick={() => setOpen(true)}>Extend limits</Button>
        )
      }
    >
      {ratio >= 0.8 && (
        <Alert
          showIcon
          type={ratio >= 0.95 ? "error" : "warning"}
          title={
            ratio >= 0.95
              ? "95% tool allowance reached — checkpoint or extend"
              : "80% tool allowance reached"
          }
          description="Completed checkpoints remain saved. Extensions require an explicit owner decision and do not automatically resume execution."
        />
      )}
      <Table
        size="small"
        pagination={{ pageSize: 7 }}
        rowKey={(row) => `${row.agent}-${row.taskIndex}`}
        dataSource={run.usage ?? []}
        columns={[
          { title: "Agent", render: (_, row) => human(row.agent) },
          { title: "Task", render: (_, row) => row.taskIndex + 1 },
          { title: "Model requests", dataIndex: "modelRequests" },
          { title: "Tools", dataIndex: "toolInvocations" },
          { title: "Failed tools", dataIndex: "failedToolInvocations" },
          { title: "Retried tools", dataIndex: "retriedToolInvocations" },
        ]}
      />
      <Modal
        title="Owner-approved execution extension"
        open={open}
        onCancel={() => setOpen(false)}
        footer={null}
        destroyOnHidden
      >
        <Typography.Paragraph>
          Review every new limit. Increasing estimated API budget explicitly
          permits additional spending. Owner-configured ceilings still apply.
          Usage, source revision and completed operations remain intact.
        </Typography.Paragraph>
        <Form
          form={form}
          layout="vertical"
          initialValues={{ policy: run.policy }}
          onFinish={(values) => extend.mutate(values)}
        >
          <Select
            aria-label="Extension preset"
            placeholder="Fill from preset"
            options={Object.keys(presets).map((value) => ({ value }))}
            onChange={(value) =>
              form.setFieldsValue({
                policy: Object.fromEntries(
                  Object.entries(presets[value]).map(([key, amount]) => [
                    key,
                    Math.max(
                      Number(run.policy[key as keyof Policy]),
                      Number(amount),
                    ),
                  ]),
                ),
              })
            }
          />
          {(
            [
              ["maxToolCalls", "Tool calls"],
              ["maxIterations", "Development iterations"],
              ["maxRetries", "Stage retries"],
              [
                "maxDurationMinutes",
                "Total duration (minutes from original start)",
              ],
              ["maxBudgetUsd", "Estimated API budget (USD)"],
            ] as const
          ).map(([key, label]) => (
            <Form.Item
              key={key}
              name={["policy", key]}
              label={label}
              rules={[{ required: true }]}
            >
              <InputNumber min={run.policy[key]} />
            </Form.Item>
          ))}
          <Form.Item
            name="reason"
            label="Reason for extension"
            rules={[{ required: true }]}
          >
            <Input.TextArea maxLength={2000} />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={extend.isPending}>
            Approve limits
          </Button>
        </Form>
      </Modal>
    </Card>
  );
}
