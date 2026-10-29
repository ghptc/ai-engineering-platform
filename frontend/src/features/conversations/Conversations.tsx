import { useState } from "react";
import {
  App,
  Avatar,
  Button,
  Card,
  Empty,
  Input,
  Select,
  Space,
  Typography,
} from "antd";
import { RobotOutlined, UserOutlined, SendOutlined } from "@ant-design/icons";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { api } from "../../services/api";
import { Heading, QueryState, useApi } from "../../components/Common";
import {
  LongMarkdown,
  ReadableEntry,
  JsonDetails,
} from "../../components/Readable";
import { human, list, parse } from "../../lib/engineering";
import { useLiveEvents, type RecordWindow } from "../../hooks/useLiveEvents";
import { TaskLauncher } from "../projects/TaskLauncher";
import { Collapse } from "antd";
import type { Entry, Page, Project } from "../../types";
export function Conversations() {
  const projects = useApi<Page<Project>>("/projects");
  const [project, setProject] = useState<string>();
  return (
    <>
      <Heading
        eyebrow="CLIENT COMMUNICATION"
        title="Conversations"
        description="Saved client context and analyst replies. Sending a message does not start an execution."
      />
      <Select
        aria-label="Select project"
        placeholder="Select project"
        className="view-toolbar"
        style={{ width: 280 }}
        value={project}
        onChange={setProject}
        options={projects.data?.items.map((p) => ({
          label: p.name,
          value: p.id,
        }))}
      />
      {project ? (
        <ConversationPanel projectId={project} type="ClientConversation" />
      ) : (
        <Empty description="Select a project to view saved conversations." />
      )}
    </>
  );
}
export function ConversationPanel({
  projectId,
  type,
  taskActions = true,
}: {
  projectId: string;
  type: string;
  taskActions?: boolean;
}) {
  useLiveEvents(projectId);
  const project = useApi<Project>(`/projects/${projectId}`);
  const conversations = useApi<Page<Entry>>(
    `/projects/${projectId}/conversations`,
  );
  const [selected, setSelected] = useState<string>();
  const [before, setBefore] = useState<number>();
  const options =
    conversations.data?.items.filter((c) => c.name === type) ?? [];
  const id = options.some((c) => c.id === selected)
    ? selected
    : options.at(-1)?.id;
  const messages = useApi<RecordWindow>(
    `/conversations/${id}/feed?limit=30${before != null ? `&before=${before}` : ""}`,
    !!id,
    before == null ? 10000 : false,
  );
  const [content, setContent] = useState("");
  const { message } = App.useApp();
  const client = useQueryClient();
  const refresh = () =>
    void client.invalidateQueries({
      predicate: (q) =>
        String(q.queryKey[0]).includes(
          `/projects/${projectId}/conversations`,
        ) || String(q.queryKey[0]).includes(`/conversations/${id}/`),
    });
  const create = useMutation({
    mutationFn: () =>
      api<Entry>(`/projects/${projectId}/conversations`, "POST", {}),
    onSuccess: (e) => {
      setSelected(e.id);
      setBefore(undefined);
      refresh();
    },
    onError: (e) => message.error(e.message),
  });
  const send = useMutation({
    mutationFn: () => api(`/conversations/${id}/messages`, "POST", { content }),
    onSuccess: () => {
      setContent("");
      setBefore(undefined);
      refresh();
      message.success("Message saved. No execution started.");
    },
    onError: (e) => message.error(e.message),
  });
  const reply = useMutation({
    mutationFn: () => api(`/conversations/${id}/reply`, "POST", {}),
    onSuccess: refresh,
    onError: (e) => message.error(e.message),
  });
  const context = useMutation({
    mutationFn: () =>
      api(`/projects/${projectId}/contexts`, "POST", { content }),
    onSuccess: () => {
      setContent("");
      message.success("Project knowledge saved. No execution started.");
    },
    onError: (e) => message.error(e.message),
  });
  return (
    <Card
      title={
        type === "ClientConversation"
          ? "Client conversation"
          : "Saved agent conversation"
      }
    >
      <div className="conversation-toolbar">
        <Select
          aria-label="Conversation"
          placeholder="Saved conversations"
          style={{ minWidth: 220, maxWidth: "100%" }}
          value={id}
          onChange={(v) => {
            setSelected(v);
            setBefore(undefined);
          }}
          options={options.map((c) => ({
            label: new Date(c.createdAt).toLocaleString(),
            value: c.id,
          }))}
        />
        <Space wrap>
          {type === "ClientConversation" && (
            <Button onClick={() => create.mutate()} loading={create.isPending}>
              New conversation
            </Button>
          )}
          {project.data && taskActions && (
            <TaskLauncher
              project={project.data}
              showInstructions={false}
              initialTask={{
                description: messages.data?.items
                  .find((e) => e.name === "Client")
                  ?.content.slice(0, 12000),
                criteria: list(
                  parse(
                    messages.data?.items.find((e) => e.name !== "Client")
                      ?.content ?? "",
                  ),
                  "AcceptanceCriteria",
                )
                  .map(String)
                  .join("\n"),
              }}
            />
          )}
        </Space>
      </div>
      <div className="message-list" role="region" aria-label="Saved messages">
        <QueryState
          loading={messages.isLoading && !!id}
          error={messages.error}
          empty={!messages.data?.items.length}
        >
          {[...(messages.data?.items ?? [])].reverse().map((e) => (
            <article
              className={`message ${e.name === "Client" ? "client-message" : ""}`}
              key={e.id}
            >
              <Avatar
                icon={
                  e.name === "Client" ? <UserOutlined /> : <RobotOutlined />
                }
              />
              <div>
                <div className="message-meta">
                  <strong>{human(e.name)}</strong>
                  <time>{new Date(e.createdAt).toLocaleString()}</time>
                </div>
                {e.name === "Client" ? (
                  <LongMarkdown content={e.content} />
                ) : (
                  <ReadableEntry entry={e} />
                )}
                <Collapse
                  size="small"
                  ghost
                  items={[
                    {
                      key: "raw",
                      label: "Original message",
                      children: <JsonDetails content={e.content} />,
                    },
                  ]}
                />
                {e.runId && (
                  <Link to={`/executions/${e.runId}`}>Related execution ↗</Link>
                )}
              </div>
            </article>
          ))}
        </QueryState>
      </div>
      <Space className="history-controls">
        <Button
          disabled={!messages.data?.hasMore}
          onClick={() => setBefore(messages.data?.nextBefore)}
        >
          Older messages
        </Button>
        <Button disabled={before == null} onClick={() => setBefore(undefined)}>
          Latest messages
        </Button>
      </Space>
      {type === "ClientConversation" && (
        <div className="composer">
          <Input.TextArea
            aria-label="Message"
            placeholder="Describe work or provide feedback. Sending saves a message only."
            rows={3}
            maxLength={100000}
            value={content}
            onChange={(e) => setContent(e.target.value)}
          />
          <Space wrap>
            <Button
              type="primary"
              icon={<SendOutlined />}
              disabled={!id || !content.trim()}
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
              disabled={!content.trim()}
              loading={context.isPending}
              onClick={() => context.mutate()}
            >
              Save as project context
            </Button>
          </Space>
          <Typography.Paragraph type="secondary">
            Messages and analyst replies stay here. Use Start focused task to
            authorize implementation.
          </Typography.Paragraph>
        </div>
      )}
    </Card>
  );
}
