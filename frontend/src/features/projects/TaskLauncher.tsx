import { useState } from "react";
import {
  App,
  Button,
  Checkbox,
  Form,
  Input,
  Modal,
  Select,
  Space,
  Typography,
} from "antd";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { api } from "../../services/api";
import type { Project, Run } from "../../types";

type TaskFields = {
  title: string;
  description: string;
  criteria: string;
  requiresBusinessAnalysis?: boolean;
  requiresArchitecture?: boolean;
  developers?: string[];
};
export function TaskLauncher({
  project,
  showInstructions = true,
  initialTask,
}: {
  project: Project;
  showInstructions?: boolean;
  initialTask?: { title?: string; description?: string; criteria?: string };
}) {
  const [open, setOpen] = useState(false);
  const [instructionsOpen, setInstructionsOpen] = useState(false);
  const navigate = useNavigate();
  const client = useQueryClient();
  const { message } = App.useApp();
  const start = useMutation({
    mutationFn: (values: { tasks: TaskFields[] }) =>
      api<Run>(
        "/projects/" + project.id + "/tasks",
        "POST",
        {
          tasks: values.tasks.map(({ criteria, ...task }) => ({
            ...task,
            acceptanceCriteria: criteria
              .split("\n")
              .map((x) => x.trim())
              .filter(Boolean),
          })),
        },
        { "Idempotency-Key": crypto.randomUUID() },
      ),
    onSuccess: (run) => {
      setOpen(false);
      void client.invalidateQueries();
      navigate("/executions/" + run.id);
    },
    onError: (e) => message.error(e.message),
  });
  const saveInstructions = useMutation({
    mutationFn: (values: { content: string }) =>
      api("/projects/" + project.id + "/instructions", "PATCH", values),
    onSuccess: () => {
      setInstructionsOpen(false);
      void client.invalidateQueries();
      message.success("Project instructions saved");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <>
      <Button type="primary" onClick={() => setOpen(true)}>
        Start focused task
      </Button>
      {showInstructions && (
        <Button onClick={() => setInstructionsOpen(true)}>
          Project instructions
        </Button>
      )}
      <Modal
        title="Focused engineering tasks"
        open={open}
        onCancel={() => setOpen(false)}
        footer={null}
        width={720}
        destroyOnHidden
      >
        <Typography.Paragraph>
          Describe the next change and its acceptance criteria. Saved project
          instructions apply to every task. Earlier conversations and project
          knowledge remain available on demand. Queued tasks share this
          execution's budget and require approval before each delivery.
        </Typography.Paragraph>
        <Form
          layout="vertical"
          initialValues={{
            tasks: [{ developers: ["BackendEngineer"], ...initialTask }],
          }}
          onFinish={(values) => start.mutate(values)}
        >
          <Form.List name="tasks">
            {(fields, { add, remove }) => (
              <>
                {fields.map((field) => (
                  <div key={field.key}>
                    <Typography.Title level={5}>
                      Task {field.name + 1}
                    </Typography.Title>
                    <Form.Item
                      name={[field.name, "title"]}
                      label="Task title"
                      rules={[{ required: true }]}
                    >
                      <Input maxLength={120} />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, "description"]}
                      label="Change to implement"
                      rules={[{ required: true }]}
                    >
                      <Input.TextArea rows={3} maxLength={12000} />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, "criteria"]}
                      label="Acceptance criteria (one per line)"
                      rules={[{ required: true }]}
                    >
                      <Input.TextArea rows={3} />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, "developers"]}
                      label="Implementation specialists"
                      rules={[{ required: true }]}
                    >
                      <Select
                        mode="multiple"
                        options={[
                          { value: "BackendEngineer", label: "Developer" },
                          {
                            value: "FrontendEngineer",
                            label: "Frontend specialist",
                          },
                        ]}
                      />
                    </Form.Item>
                    <Form.Item
                      name={[field.name, "requiresBusinessAnalysis"]}
                      valuePropName="checked"
                    >
                      <Checkbox>
                        Requirements need Business Analyst input
                      </Checkbox>
                    </Form.Item>
                    <Form.Item
                      name={[field.name, "requiresArchitecture"]}
                      valuePropName="checked"
                    >
                      <Checkbox>Architecture needs specialist input</Checkbox>
                    </Form.Item>
                    {fields.length > 1 && (
                      <Button onClick={() => remove(field.name)}>
                        Remove task
                      </Button>
                    )}
                  </div>
                ))}
                <Space>
                  <Button
                    disabled={
                      fields.length >= project.executionPolicy.maxIterations
                    }
                    onClick={() => add({ developers: ["BackendEngineer"] })}
                  >
                    Queue next task
                  </Button>
                  <Button
                    type="primary"
                    htmlType="submit"
                    loading={start.isPending}
                  >
                    Run tasks
                  </Button>
                </Space>
              </>
            )}
          </Form.List>
        </Form>
      </Modal>
      <Modal
        title="Project instructions"
        open={instructionsOpen}
        onCancel={() => setInstructionsOpen(false)}
        footer={null}
        destroyOnHidden
      >
        <Typography.Paragraph>
          Save lasting human constraints here so they apply to every focused
          task. Project conversations remain available as knowledge.
        </Typography.Paragraph>
        <Form
          key={project.version}
          layout="vertical"
          initialValues={{ content: project.standingInstructions ?? "" }}
          onFinish={(values) => saveInstructions.mutate(values)}
        >
          <Form.Item name="content" label="Standing instructions">
            <Input.TextArea rows={8} maxLength={12000} />
          </Form.Item>
          <Button
            type="primary"
            htmlType="submit"
            loading={saveInstructions.isPending}
          >
            Save instructions
          </Button>
        </Form>
      </Modal>
    </>
  );
}
