import { useState } from "react";
import {
  App,
  Button,
  Card,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Typography,
  InputNumber,
  Switch,
} from "antd";
import { PlusOutlined, ArrowRightOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, Status, useApi } from "../../components/Common";
import type { Project, Page } from "../../types";
export function ProjectForm({
  project,
  onSaved,
  onCancel,
}: {
  project?: Project;
  onSaved: () => void;
  onCancel: () => void;
}) {
  const { message } = App.useApp();
  const client = useQueryClient();
  const mutation = useMutation({
    mutationFn: (values: unknown) =>
      api(
        project ? "/projects/" + project.id : "/projects",
        project ? "PUT" : "POST",
        values,
      ),
    onSuccess: () => {
      void client.invalidateQueries();
      message.success("Project saved");
      onSaved();
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Form
      layout="vertical"
      initialValues={
        project || {
          technologyStack: ["C#", "React"],
          executionPolicy: {
            maxIterations: 5,
            maxToolCalls: 30,
            maxDurationMinutes: 60,
            maxBudgetUsd: 5,
            maxRetries: 2,
            requireHumanApprovalForMerge: true,
            reviewBeforeCommit: true,
          },
        }
      }
      onFinish={(values) =>
        mutation.mutate({
          ...values,
          version: project?.version ?? 0,
          description: values.description || "",
          originalContext: values.originalContext || "",
        })
      }
    >
      <Form.Item
        label="Project name"
        name="name"
        rules={[{ required: true, max: 120 }]}
      >
        <Input placeholder="Customer portal" />
      </Form.Item>
      <Form.Item label="Description" name="description">
        <Input.TextArea rows={2} />
      </Form.Item>
      <Form.Item
        label="Initial development context"
        name="originalContext"
        rules={[{ required: true, max: 100000 }]}
      >
        <Input.TextArea
          rows={6}
          placeholder="Describe the product, users, constraints and acceptance criteria."
        />
      </Form.Item>
      <Form.Item label="Technology stack" name="technologyStack">
        <Select
          mode="tags"
          options={["C#", ".NET 10", "React", "TypeScript", "MongoDB"].map(
            (value) => ({ value }),
          )}
        />
      </Form.Item>
      <div className="form-grid">
        {[
          ["Max iterations", "maxIterations", 20],
          ["Max tool calls", "maxToolCalls", 200],
          ["Duration (minutes)", "maxDurationMinutes", 240],
          ["Budget (USD)", "maxBudgetUsd", 1000],
          ["Stage retries", "maxRetries", 5],
        ].map(([label, key, max]) => (
          <Form.Item
            key={key}
            label={label}
            name={["executionPolicy", String(key)]}
          >
            <InputNumber
              min={key === "maxRetries" ? 0 : 1}
              max={Number(max)}
              style={{ width: "100%" }}
            />
          </Form.Item>
        ))}
      </div>
      <Form.Item
        name={["executionPolicy", "requireHumanApprovalForMerge"]}
        valuePropName="checked"
        hidden
      >
        <Switch />
      </Form.Item>
      <Form.Item
        label="Review local changes before creating a commit (off: review after draft PR creation)"
        name={["executionPolicy", "reviewBeforeCommit"]}
        valuePropName="checked"
      >
        <Switch />
      </Form.Item>
      <Space>
        <Button type="primary" htmlType="submit" loading={mutation.isPending}>
          Save project
        </Button>
        <Button onClick={onCancel}>Cancel</Button>
      </Space>
    </Form>
  );
}
export function Projects() {
  const query = useApi<Page<Project>>("/projects");
  const [open, setOpen] = useState(false);
  return (
    <>
      <Heading
        eyebrow="PRODUCT PORTFOLIO"
        title="Projects"
        description="Give your engineering team a purpose, a repository, and clear boundaries."
        actions={
          <Button
            type="primary"
            icon={<PlusOutlined />}
            onClick={() => setOpen(true)}
          >
            New project
          </Button>
        }
      />
      <QueryState
        loading={query.isLoading}
        error={query.error}
        empty={!query.data?.items.length}
      >
        <div className="project-grid">
          {query.data?.items.map((project) => (
            <Card key={project.id} className="project-card">
              <Space>
                <div className="project-avatar">
                  {project.name.slice(0, 2).toUpperCase()}
                </div>
                <Status value={project.status} />
              </Space>
              <Typography.Title level={4}>{project.name}</Typography.Title>
              <Typography.Paragraph type="secondary" ellipsis={{ rows: 2 }}>
                {project.description || "No description provided."}
              </Typography.Paragraph>
              <Space wrap>
                {project.technologyStack.map((stack) => (
                  <span className="stack-chip" key={stack}>
                    {stack}
                  </span>
                ))}
              </Space>
              <div className="card-footer">
                <span>{new Date(project.updatedAt).toLocaleDateString()}</span>
                <Link to={"/projects/" + project.id}>
                  Open workspace <ArrowRightOutlined />
                </Link>
              </div>
            </Card>
          ))}
        </div>
      </QueryState>
      <Modal
        title="Create project"
        open={open}
        onCancel={() => setOpen(false)}
        footer={null}
        width={720}
        destroyOnHidden
      >
        <ProjectForm
          onSaved={() => setOpen(false)}
          onCancel={() => setOpen(false)}
        />
      </Modal>
    </>
  );
}
