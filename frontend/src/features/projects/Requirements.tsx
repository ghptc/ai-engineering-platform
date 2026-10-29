import { useState } from "react";
import {
  App,
  Button,
  Drawer,
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
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { QueryState, Status, useApi } from "../../components/Common";
import { JsonDetails, Markdown } from "../../components/Readable";
import { list, parse, text } from "../../lib/engineering";
import type { Entry, Page } from "../../types";
export function Requirements({ projectId }: { projectId: string }) {
  const [offset, setOffset] = useState(0);
  const query = useApi<Page<Entry>>(
    `/projects/${projectId}/requirements?offset=${offset}&limit=25`,
  );
  const [open, setOpen] = useState(false);
  const [selected, setSelected] = useState<Entry>();
  const { message } = App.useApp();
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (v: {
      title: string;
      description: string;
      criteria: string;
      priority: number;
      dependencies: string[];
    }) =>
      api(`/projects/${projectId}/requirements`, "POST", {
        ...v,
        acceptanceCriteria: v.criteria
          .split("\n")
          .map((x) => x.trim())
          .filter(Boolean),
      }),
    onSuccess: () => {
      setOpen(false);
      void client.invalidateQueries({
        predicate: (q) =>
          String(q.queryKey[0]).startsWith(
            `/projects/${projectId}/requirements`,
          ),
      });
    },
    onError: (e) => message.error(e.message),
  });
  const d = parse(selected?.content ?? "");
  return (
    <>
      <Button onClick={() => setOpen(true)} className="view-toolbar">
        Add requirement
      </Button>
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <Table
          rowKey="id"
          size="small"
          pagination={false}
          scroll={{ x: 640 }}
          dataSource={query.data?.items}
          columns={[
            {
              title: "Requirement",
              dataIndex: "name",
              render: (v: string, e: Entry) => (
                <Button type="link" onClick={() => setSelected(e)}>
                  {v}
                </Button>
              ),
            },
            {
              title: "Priority",
              render: (_, e: Entry) =>
                `P${text(parse(e.content), "Priority", "—")}`,
            },
            {
              title: "Status",
              dataIndex: "status",
              render: (v: string) => <Status value={v} />,
            },
            {
              title: "Acceptance criteria",
              render: (_, e: Entry) =>
                list(parse(e.content), "AcceptanceCriteria").length,
            },
            {
              title: "Dependencies",
              render: (_, e: Entry) =>
                list(parse(e.content), "Dependencies").length,
            },
            {
              title: "Execution",
              dataIndex: "runId",
              render: (v?: string) =>
                v ? (
                  <Link to={`/executions/${v}`}>Inspect evidence ↗</Link>
                ) : (
                  "Not assigned"
                ),
            },
          ]}
        />
      </QueryState>
      <Space className="history-controls">
        <Button
          disabled={!offset}
          onClick={() => setOffset(Math.max(0, offset - 25))}
        >
          Previous requirements
        </Button>
        <Button
          disabled={(query.data?.items.length ?? 0) < 25}
          onClick={() => setOffset(offset + 25)}
        >
          Next requirements
        </Button>
      </Space>
      <Drawer
        title={selected?.name ?? "Requirement"}
        open={!!selected}
        onClose={() => setSelected(undefined)}
        size="large"
        destroyOnHidden
      >
        {selected && (
          <>
            <Space wrap>
              <Status value={selected.status} />
              <Tag>Priority {text(d, "Priority", "unavailable")}</Tag>
              {selected.runId && <Tag>Agent-generated / execution-linked</Tag>}
            </Space>
            <Markdown content={text(d, "Description")} />
            <Typography.Title level={5}>Acceptance criteria</Typography.Title>
            <ul>
              {list(d, "AcceptanceCriteria").map((v, i) => (
                <li key={i}>
                  <Markdown content={String(v)} />
                </li>
              ))}
            </ul>
            <Typography.Title level={5}>Dependencies</Typography.Title>
            {list(d, "Dependencies").length ? (
              list(d, "Dependencies").map((v, i) => (
                <Typography.Paragraph key={i} copyable>
                  {String(v)}
                </Typography.Paragraph>
              ))
            ) : (
              <p>No dependencies recorded.</p>
            )}
            {selected.runId && (
              <Link to={`/executions/${selected.runId}?tab=artifacts`}>
                Related execution, artifacts and validation ↗
              </Link>
            )}
            {selected.metadata?.testEvidenceId && (
              <Typography.Paragraph copyable code>
                {selected.metadata.testEvidenceId}
              </Typography.Paragraph>
            )}
            <Typography.Paragraph type="secondary">
              Generated requirements are presented for review. This view never
              approves or verifies them; backend policy and actual validation
              determine completion.
            </Typography.Paragraph>
            <JsonDetails content={selected.content} />
          </>
        )}
      </Drawer>
      <Modal
        title="Add requirement"
        open={open}
        onCancel={() => setOpen(false)}
        footer={null}
        destroyOnHidden
      >
        <Form
          layout="vertical"
          initialValues={{ priority: 1, dependencies: [] }}
          onFinish={(v) => mutation.mutate(v)}
        >
          <Form.Item name="title" label="Title" rules={[{ required: true }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="description"
            label="Description"
            rules={[{ required: true }]}
          >
            <Input.TextArea />
          </Form.Item>
          <Form.Item
            name="criteria"
            label="Acceptance criteria (one per line)"
            rules={[{ required: true }]}
          >
            <Input.TextArea rows={4} />
          </Form.Item>
          <Form.Item
            name="priority"
            label="Priority (1 highest)"
            rules={[{ required: true }]}
          >
            <InputNumber min={1} max={5} />
          </Form.Item>
          <Form.Item name="dependencies" label="Dependency requirement IDs">
            <Select
              mode="tags"
              options={query.data?.items.map((e) => ({
                value: e.id,
                label: e.name,
              }))}
            />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={mutation.isPending}>
            Save requirement
          </Button>
        </Form>
      </Modal>
    </>
  );
}
