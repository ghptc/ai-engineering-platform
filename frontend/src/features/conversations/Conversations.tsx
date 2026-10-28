import { useState } from "react";
import {
  App,
  Avatar,
  Button,
  Card,
  Input,
  Select,
  Space,
  Typography,
  Empty,
} from "antd";
import { RobotOutlined, SendOutlined, UserOutlined } from "@ant-design/icons";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, useApi, jsonText } from "../../components/Common";
import { useLiveEvents } from "../../hooks/useLiveEvents";
import type { Entry, Page, Project } from "../../types";
export function Conversations({ agents = false }: { agents?: boolean }) {
  const projects = useApi<Page<Project>>("/projects");
  const [project, setProject] = useState<string>();
  return (
    <>
      <Heading
        eyebrow={agents ? "ENGINEERING TEAM" : "CLIENT COMMUNICATION"}
        title={agents ? "Agent collaboration" : "Conversations"}
        description={
          agents
            ? "Persisted agent messages, tool evidence, and engineering decisions."
            : "Share context and feedback with your engineering team."
        }
      />
      <Select
        aria-label="Select project"
        placeholder="Select project"
        style={{ minWidth: 260, marginBottom: 24 }}
        options={projects.data?.items.map((p) => ({
          label: p.name,
          value: p.id,
        }))}
        value={project}
        onChange={setProject}
      />
      {project ? (
        <ConversationPanel
          projectId={project}
          type={agents ? "AgentConversation" : "ClientConversation"}
        />
      ) : (
        <Empty description="Select a project to view saved conversations." />
      )}
    </>
  );
}
export function ConversationPanel({
  projectId,
  type,
}: {
  projectId: string;
  type: string;
}) {
  useLiveEvents(projectId);
  const conversations = useApi<Page<Entry>>(
    "/projects/" + projectId + "/conversations",
  );
  const [selected, setSelected] = useState<string>();
  const options =
    conversations.data?.items.filter((x) => x.name === type) || [];
  const id = options.some((x) => x.id === selected) ? selected : options[0]?.id;
  const messages = useApi<Page<Entry>>(
    "/conversations/" + id + "/messages",
    !!id,
  );
  const [text, setText] = useState("");
  const { message } = App.useApp();
  const client = useQueryClient();
  const create = useMutation({
    mutationFn: () =>
      api<Entry>("/projects/" + projectId + "/conversations", "POST", {}),
    onSuccess: (entry) => {
      setSelected(entry.id);
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  const send = useMutation({
    mutationFn: () =>
      api("/conversations/" + id + "/messages", "POST", { content: text }),
    onSuccess: () => {
      setText("");
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  const reply = useMutation({
    mutationFn: () => api("/conversations/" + id + "/reply", "POST", {}),
    onSuccess: () => {
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  const submit = useMutation({
    mutationFn: () =>
      api("/projects/" + projectId + "/contexts", "POST", { content: text }),
    onSuccess: () => {
      setText("");
      message.success("Context saved. Start an execution to process it.");
      void client.invalidateQueries();
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card className="conversation">
      <div className="conversation-toolbar">
        <Select
          aria-label="Conversation"
          placeholder="Saved conversations"
          value={id}
          onChange={setSelected}
          style={{ minWidth: 260 }}
          options={options.map((c) => ({
            value: c.id,
            label:
              new Date(c.createdAt).toLocaleString() +
              (c.runId ? " · " + c.runId.slice(0, 8) : ""),
          }))}
        />
        {type === "ClientConversation" && (
          <Button onClick={() => create.mutate()} loading={create.isPending}>
            New conversation
          </Button>
        )}
      </div>
      <div className="message-list">
        <QueryState
          loading={messages.isLoading && !!id}
          error={messages.error}
          empty={!messages.data?.items.length}
        >
          {messages.data?.items.map((entry) => (
            <article
              className={
                "message " + (entry.name === "Client" ? "client-message" : "")
              }
              key={entry.id}
            >
              <Avatar
                icon={
                  entry.name === "Client" ? <UserOutlined /> : <RobotOutlined />
                }
              />
              <div>
                <div className="message-meta">
                  <strong>
                    {entry.name.replace(/([a-z])([A-Z])/g, "$1 $2")}
                  </strong>
                  <time>{new Date(entry.createdAt).toLocaleString()}</time>
                </div>
                <pre className="message-content">{jsonText(entry.content)}</pre>
                {entry.runId && (
                  <Typography.Text type="secondary">
                    Execution {entry.runId.slice(0, 12)}
                  </Typography.Text>
                )}
              </div>
            </article>
          ))}
        </QueryState>
      </div>
      {type === "ClientConversation" && (
        <div className="composer">
          <Input.TextArea
            aria-label="Message"
            placeholder="Describe your requirements, request a change, or reference an artifact…"
            value={text}
            onChange={(e) => setText(e.target.value)}
            rows={3}
          />
          <Space wrap>
            <Button
              type="primary"
              icon={<SendOutlined />}
              disabled={!id || !text.trim()}
              loading={send.isPending}
              onClick={() => send.mutate()}
            >
              Send message
            </Button>
            <Button
              disabled={!id}
              loading={reply.isPending}
              onClick={() => reply.mutate()}
            >
              Ask analyst
            </Button>
            <Button
              disabled={!text.trim()}
              loading={submit.isPending}
              onClick={() => submit.mutate()}
            >
              Save as project context
            </Button>
            <Typography.Text type="secondary">
              Saved context is processed when an execution starts.
            </Typography.Text>
          </Space>
        </div>
      )}
    </Card>
  );
}
