import { useState } from "react";
import {
  App,
  Button,
  Card,
  Descriptions,
  Form,
  Input,
  InputNumber,
  Modal,
  Space,
  Table,
  Tabs,
  Typography,
  Alert,
} from "antd";
import {
  PlayCircleOutlined,
  EditOutlined,
  LinkOutlined,
} from "@ant-design/icons";
import { Link, useNavigate, useParams } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import {
  Heading,
  QueryState,
  Status,
  useApi,
  jsonText,
} from "../../components/Common";
import { ProjectForm } from "./Projects";
import { ConversationPanel } from "../conversations/Conversations";
import { useLiveEvents } from "../../hooks/useLiveEvents";
import type { Entry, Page, Project, Run } from "../../types";
export function Workspace() {
  const { id = "" } = useParams();
  useLiveEvents(id);
  const project = useApi<Project>("/projects/" + id);
  const runs = useApi<Page<Run>>("/projects/" + id + "/runs");
  const memory = useApi<Entry[]>("/projects/" + id + "/memory");
  const [edit, setEdit] = useState(false);
  const [connect, setConnect] = useState(false);
  const { message } = App.useApp();
  const client = useQueryClient();
  const navigate = useNavigate();
  const start = useMutation({
    mutationFn: () =>
      api<Run>(
        "/projects/" + id + "/runs",
        "POST",
        {},
        { "Idempotency-Key": crypto.randomUUID() },
      ),
    onSuccess: (run) => {
      void client.invalidateQueries();
      navigate("/executions/" + run.id);
    },
    onError: (e) => message.error(e.message),
  });
  const repository = useMutation({
    mutationFn: (values: unknown) =>
      api("/integrations/github/repositories", "POST", {
        projectId: id,
        connection: values,
      }),
    onSuccess: () => {
      setConnect(false);
      void client.invalidateQueries();
      message.success("Repository access verified");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <QueryState loading={project.isLoading} error={project.error}>
      {project.data && (
        <>
          <Heading
            eyebrow="PROJECT WORKSPACE"
            title={project.data.name}
            description={project.data.description}
            actions={
              <Space wrap>
                <Button icon={<EditOutlined />} onClick={() => setEdit(true)}>
                  Edit
                </Button>
                <Button
                  icon={<LinkOutlined />}
                  onClick={() => setConnect(true)}
                >
                  Connect repository
                </Button>
                <Button
                  type="primary"
                  icon={<PlayCircleOutlined />}
                  loading={start.isPending}
                  onClick={() => start.mutate()}
                >
                  Start execution
                </Button>
              </Space>
            }
          />
          <Tabs
            items={[
              {
                key: "overview",
                label: "Overview",
                children: (
                  <div className="workspace-grid">
                    <Card title="Product context">
                      <Typography.Paragraph className="preserve">
                        {project.data.originalContext}
                      </Typography.Paragraph>
                      <Space wrap>
                        {project.data.technologyStack.map((x) => (
                          <span className="stack-chip" key={x}>
                            {x}
                          </span>
                        ))}
                      </Space>
                    </Card>
                    <Card title="Execution boundaries">
                      <Descriptions
                        column={1}
                        items={[
                          {
                            key: "status",
                            label: "Status",
                            children: <Status value={project.data.status} />,
                          },
                          {
                            key: "iterations",
                            label: "Iterations",
                            children:
                              project.data.executionPolicy.maxIterations,
                          },
                          {
                            key: "budget",
                            label: "Budget per run",
                            children:
                              "$" + project.data.executionPolicy.maxBudgetUsd,
                          },
                          {
                            key: "duration",
                            label: "Time limit",
                            children:
                              project.data.executionPolicy.maxDurationMinutes +
                              " min",
                          },
                          {
                            key: "repository",
                            label: "Repository",
                            children: project.data.repositoryConnection
                              ? project.data.repositoryConnection.owner +
                                "/" +
                                project.data.repositoryConnection.repository
                              : "Not connected",
                          },
                          {
                            key: "merge",
                            label: "Merge policy",
                            children: "Human approval required",
                          },
                        ]}
                      />
                    </Card>
                  </div>
                ),
              },
              {
                key: "requirements",
                label: "Requirements",
                children: <Requirements projectId={id} />,
              },
              {
                key: "runs",
                label: "Execution history",
                children: (
                  <Table
                    rowKey="id"
                    dataSource={runs.data?.items}
                    loading={runs.isLoading}
                    columns={[
                      {
                        title: "Execution",
                        dataIndex: "id",
                        render: (value: string) => (
                          <Link to={"/executions/" + value}>
                            {value.slice(0, 12)}
                          </Link>
                        ),
                      },
                      {
                        title: "Stage",
                        dataIndex: "currentStage",
                        render: (value: string) => <Status value={value} />,
                      },
                      { title: "Iteration", dataIndex: "iteration" },
                      {
                        title: "Cost",
                        dataIndex: "cost",
                        render: (value: number) => "$" + value.toFixed(4),
                      },
                    ]}
                  />
                ),
              },
              {
                key: "architecture",
                label: "Architecture & artifacts",
                children: (
                  <Space orientation="vertical" style={{ width: "100%" }}>
                    {memory.data?.length ? (
                      memory.data.map((entry) => (
                        <Card
                          key={entry.id}
                          title={entry.name}
                          extra={
                            <Typography.Text type="secondary">
                              {new Date(entry.createdAt).toLocaleString()}
                            </Typography.Text>
                          }
                        >
                          <pre className="artifact-preview">
                            {jsonText(entry.content)}
                          </pre>
                        </Card>
                      ))
                    ) : (
                      <Alert
                        title="Architecture decisions and artifacts appear after execution."
                        type="info"
                      />
                    )}
                  </Space>
                ),
              },
              {
                key: "conversation",
                label: "Client conversation",
                children: (
                  <ConversationPanel projectId={id} type="ClientConversation" />
                ),
              },
              {
                key: "collaboration",
                label: "Agent collaboration",
                children: (
                  <ConversationPanel projectId={id} type="AgentConversation" />
                ),
              },
            ]}
          />
          <Modal
            title="Edit project"
            open={edit}
            onCancel={() => setEdit(false)}
            footer={null}
            width={720}
            destroyOnHidden
          >
            <ProjectForm
              project={project.data}
              onSaved={() => setEdit(false)}
              onCancel={() => setEdit(false)}
            />
          </Modal>
          <Modal
            title="Connect GitHub repository"
            open={connect}
            onCancel={() => setConnect(false)}
            footer={null}
            destroyOnHidden
          >
            <Form
              layout="vertical"
              initialValues={{ defaultBranch: "main" }}
              onFinish={(values) => repository.mutate(values)}
            >
              {["owner", "repository", "defaultBranch"].map((name) => (
                <Form.Item
                  label={name}
                  name={name}
                  key={name}
                  rules={[{ required: true }]}
                >
                  <Input />
                </Form.Item>
              ))}
              <Form.Item
                label="GitHub App installation ID"
                name="installationId"
                rules={[{ required: true }]}
              >
                <InputNumber min={1} style={{ width: "100%" }} />
              </Form.Item>
              <Button
                type="primary"
                htmlType="submit"
                loading={repository.isPending}
              >
                Verify and connect
              </Button>
            </Form>
          </Modal>
        </>
      )}
    </QueryState>
  );
}
function Requirements({ projectId }: { projectId: string }) {
  const query = useApi<Page<Entry>>("/projects/" + projectId + "/requirements");
  const [open, setOpen] = useState(false);
  const { message } = App.useApp();
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (values: {
      title: string;
      description: string;
      criteria: string;
    }) =>
      api("/projects/" + projectId + "/requirements", "POST", {
        ...values,
        acceptanceCriteria: values.criteria.split("\n").filter(Boolean),
        priority: 1,
      }),
    onSuccess: () => {
      setOpen(false);
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <>
      <Button onClick={() => setOpen(true)} style={{ marginBottom: 16 }}>
        Add requirement
      </Button>
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <Table
          rowKey="id"
          dataSource={query.data?.items}
          columns={[
            { title: "Requirement", dataIndex: "name" },
            {
              title: "Status",
              dataIndex: "status",
              render: (value: string) => <Status value={value} />,
            },
            {
              title: "Details",
              dataIndex: "content",
              render: (value: string) => (
                <pre className="artifact-preview">{jsonText(value)}</pre>
              ),
            },
          ]}
        />
      </QueryState>
      <Modal
        title="Add requirement"
        open={open}
        onCancel={() => setOpen(false)}
        footer={null}
        destroyOnHidden
      >
        <Form layout="vertical" onFinish={(values) => mutation.mutate(values)}>
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
            <Input.TextArea rows={5} />
          </Form.Item>
          <Button type="primary" htmlType="submit" loading={mutation.isPending}>
            Save requirement
          </Button>
        </Form>
      </Modal>
    </>
  );
}
