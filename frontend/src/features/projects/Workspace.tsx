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
import { Requirements } from "./Requirements";
import { Markdown, ReadableEntry } from "../../components/Readable";
import { EvidenceDrawer } from "../../components/EvidenceDrawer";
import { TeamWorkspace } from "../collaboration/Collaboration";
import { summary } from "../../lib/engineering";
import type { RecordWindow } from "../../hooks/useLiveEvents";
import { Collapse, Dropdown } from "antd";
import { TaskLauncher } from "./TaskLauncher";
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
  const currentRun = runs.data?.items[0];
  const requirements = useApi<Page<Entry>>(
    `/projects/${id}/requirements?limit=50`,
  );
  const events = useApi<RecordWindow>(
    `/runs/${currentRun?.id}/stream?limit=50`,
    !!currentRun,
  );
  const [selectedArtifact, setSelectedArtifact] = useState<Entry>();
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
                <TaskLauncher project={project.data} />
                <Dropdown
                  menu={{
                    items: [
                      {
                        key: "edit",
                        label: "Edit project",
                        onClick: () => setEdit(true),
                      },
                      {
                        key: "repository",
                        label: "Connect repository",
                        onClick: () => setConnect(true),
                      },
                      {
                        key: "plan",
                        label: "Plan complete project",
                        onClick: () => start.mutate(),
                      },
                    ],
                  }}
                  trigger={["click"]}
                >
                  <Button loading={start.isPending}>Project actions ⌄</Button>
                </Dropdown>
              </Space>
            }
          />
          <Tabs
            items={[
              {
                key: "overview",
                label: "Overview",
                children: (
                  <Space orientation="vertical" style={{ width: "100%" }}>
                    <div className="operations-grid">
                      <Card title="Product objective">
                        <Markdown
                          content={
                            project.data.description ||
                            project.data.originalContext
                              .split("\n")
                              .find((line) => line.trim())
                              ?.slice(0, 300) ||
                            "Define your product objective."
                          }
                        />
                        <Space wrap>
                          {project.data.technologyStack.map((x) => (
                            <span className="stack-chip" key={x}>
                              {x}
                            </span>
                          ))}
                        </Space>
                        <Collapse
                          className="section-gap"
                          items={[
                            {
                              key: "context",
                              label: "Full product context",
                              children: (
                                <div className="context-window">
                                  <Markdown
                                    content={project.data.originalContext}
                                  />
                                </div>
                              ),
                            },
                          ]}
                        />
                      </Card>
                      <Card title="Current milestone & task">
                        {currentRun ? (
                          <>
                            <Status value={currentRun.currentStage} />
                            <Typography.Title level={4}>
                              {currentRun.tasks?.[currentRun.taskIndex]
                                ?.title ?? "Engineering execution"}
                            </Typography.Title>
                            <Typography.Paragraph>
                              {currentRun.tasks?.[
                                currentRun.taskIndex
                              ]?.description?.slice(0, 300)}
                            </Typography.Paragraph>
                            <Typography.Paragraph>
                              Iteration {currentRun.iteration} /{" "}
                              {currentRun.policy.maxIterations} ·{" "}
                              {requirements.data?.items.filter(
                                (r) => r.status === "Verified",
                              ).length ?? 0}{" "}
                              verified requirements in current page
                            </Typography.Paragraph>
                            {currentRun.failureReason && (
                              <Alert
                                type="warning"
                                title={currentRun.failureReason}
                              />
                            )}
                            <Link to={`/executions/${currentRun.id}`}>
                              Open live engineering workspace ↗
                            </Link>
                          </>
                        ) : (
                          <Alert
                            type="info"
                            title="Ready for your first focused task"
                            description="Describe a bounded change and choose only the specialists it needs."
                          />
                        )}
                      </Card>
                    </div>
                    <Card title="Latest decisions & deliveries">
                      {memory.data
                        ?.filter((e) =>
                          [
                            "ArchitectureDecision",
                            "PullRequest",
                            "Commit",
                            "ValidationReport",
                            "CodeReview",
                          ].includes(e.name),
                        )
                        .slice(-5)
                        .reverse()
                        .map((e) => (
                          <div className="compact-row" key={e.id}>
                            <div>
                              <strong>{e.name}</strong>
                              <p>{summary(e)}</p>
                            </div>
                            <Button onClick={() => setSelectedArtifact(e)}>
                              Inspect
                            </Button>
                          </div>
                        ))}
                      {!memory.data?.length && (
                        <Typography.Text type="secondary">
                          No decisions or deliveries recorded yet.
                        </Typography.Text>
                      )}
                    </Card>
                    <Card title="Execution boundaries">
                      <Descriptions
                        column={{ xs: 1, sm: 2, md: 3 }}
                        items={[
                          {
                            key: "status",
                            label: "Project",
                            children: <Status value={project.data.status} />,
                          },
                          {
                            key: "iterations",
                            label: "Iteration limit",
                            children:
                              project.data.executionPolicy.maxIterations,
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
                            label: "Protected delivery",
                            children: "Human approval; no automatic merge",
                          },
                        ]}
                      />
                    </Card>
                  </Space>
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
                            <Button onClick={() => setSelectedArtifact(entry)}>
                              Inspect artifact
                            </Button>
                          }
                        >
                          <ReadableEntry entry={entry} compact />
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
                  <ConversationPanel
                    projectId={id}
                    type="ClientConversation"
                    taskActions={false}
                  />
                ),
              },
              {
                key: "collaboration",
                label: "Agent collaboration",
                children: currentRun ? (
                  <TeamWorkspace
                    run={currentRun}
                    events={events.data?.items ?? []}
                  />
                ) : (
                  <Alert
                    type="info"
                    title="Start an execution to see the selected engineering team."
                  />
                ),
              },
            ]}
          />
          <EvidenceDrawer
            key={selectedArtifact?.id ?? "none"}
            entry={selectedArtifact}
            onClose={() => setSelectedArtifact(undefined)}
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
