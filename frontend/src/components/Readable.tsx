import { useMemo, useState } from "react";
import { Button, Collapse, Space, Tag, Typography } from "antd";
import { marked } from "marked";
import DOMPurify from "dompurify";
import {
  field,
  githubUrl,
  human,
  list,
  parse,
  summary,
  text,
  type Data,
} from "../lib/engineering";
import type { Entry } from "../types";
export function Markdown({ content }: { content: string }) {
  const html = useMemo(
    () =>
      DOMPurify.sanitize(marked.parse(content, { async: false }) as string, {
        FORBID_TAGS: ["img", "iframe", "style", "form", "input", "svg"],
        FORBID_ATTR: ["style"],
      }),
    [content],
  );
  return (
    <div className="markdown" dangerouslySetInnerHTML={{ __html: html }} />
  );
}
export function LongMarkdown({ content }: { content: string }) {
  const [expanded, setExpanded] = useState(false);
  return (
    <>
      <Markdown content={expanded ? content : content.slice(0, 4000)} />
      {content.length > 4000 && (
        <Button size="small" onClick={() => setExpanded(!expanded)}>
          {expanded ? "Collapse message" : "Read complete message"}
        </Button>
      )}
    </>
  );
}
function JsonNode({ value, name }: { value: unknown; name: string }) {
  const [open, setOpen] = useState(false);
  const [count, setCount] = useState(30);
  if (value && typeof value === "object") {
    const entries = Object.entries(value);
    return (
      <div className="json-node">
        <Button
          type="text"
          size="small"
          onClick={() => setOpen(!open)}
          aria-expanded={open}
        >
          {open ? "▾" : "▸"} {name} · {entries.length}{" "}
          {Array.isArray(value) ? "items" : "fields"}
        </Button>
        {open && (
          <div className="json-children">
            {entries.slice(0, count).map(([k, v]) => (
              <JsonNode key={k} name={k} value={v} />
            ))}
            {entries.length > count && (
              <Button onClick={() => setCount(count + 30)}>
                Show next 30 fields
              </Button>
            )}
          </div>
        )}
      </div>
    );
  }
  const raw = String(value ?? "null");
  return (
    <div className="json-value">
      <strong>{name}: </strong>
      {open ? raw : raw.slice(0, 2000)}
      {raw.length > 2000 && (
        <Button size="small" onClick={() => setOpen(!open)}>
          {open ? "Collapse value" : "Show complete value"}
        </Button>
      )}
    </div>
  );
}
export function JsonDetails({ content }: { content: string }) {
  let value: unknown;
  try {
    value = JSON.parse(content);
  } catch {
    value = content;
  }
  return (
    <div className="json-tree">
      <Typography.Paragraph copyable={{ text: content }}>
        Copy complete payload
      </Typography.Paragraph>
      <JsonNode name="Payload" value={value} />
    </div>
  );
}
export function ReadableEntry({
  entry,
  compact = false,
}: {
  entry: Entry;
  compact?: boolean;
}) {
  const d = parse(entry.content);
  const url = githubUrl(field(d, "html_url"));
  const sections = [
    "CompletedWork",
    "AcceptanceCriteria",
    "Requirements",
    "ArchitectureDecisions",
    "ValidationResults",
    "Checks",
    "Findings",
    "Blockers",
    "OpenQuestions",
    "RelevantArtifacts",
    "Evidence",
    "NextAction",
  ];
  return (
    <div className="readable-entry">
      <Markdown content={summary(entry)} />
      {text(d, "sha") && (
        <Typography.Paragraph copyable code>
          {text(d, "sha")}
        </Typography.Paragraph>
      )}
      {text(d, "branch") && (
        <Typography.Paragraph code>{text(d, "branch")}</Typography.Paragraph>
      )}
      {field(d, "Success") != null && (
        <Tag color={field(d, "Success") ? "green" : "red"}>
          {field(d, "Success") ? "Succeeded" : "Failed"}
        </Tag>
      )}
      {text(d, "EvidenceId") && (
        <Typography.Paragraph copyable code>
          {text(d, "EvidenceId")}
        </Typography.Paragraph>
      )}
      {text(d, "WorkspaceRevision") && (
        <Typography.Paragraph copyable code>
          {text(d, "WorkspaceRevision")}
        </Typography.Paragraph>
      )}
      {url && text(d, "state") && (
        <Tag>Published GitHub status · {text(d, "state")}</Tag>
      )}
      {url && (
        <a href={url} target="_blank" rel="noopener noreferrer">
          Open {text(d, "title", "GitHub delivery")} ↗
        </a>
      )}
      {!compact && (
        <>
          {text(d, "Decision") && <Tag>{human(text(d, "Decision"))}</Tag>}
          {sections
            .filter(
              (k) =>
                field(d, k) != null &&
                (!Array.isArray(field(d, k)) || list(d, k).length > 0),
            )
            .map((k) => (
              <Collapse
                key={k}
                size="small"
                ghost
                items={[
                  {
                    key: k,
                    label: human(k),
                    children:
                      typeof field(d, k) === "string" ? (
                        <Markdown content={String(field(d, k))} />
                      ) : (
                        <StructuredList items={list(d, k)} />
                      ),
                  },
                ]}
              />
            ))}
        </>
      )}
    </div>
  );
}
function StructuredList({ items }: { items: unknown[] }) {
  return (
    <ul className="structured-list">
      {items.map((item, i) => {
        const d = item && typeof item === "object" ? (item as Data) : {};
        return (
          <li key={i}>
            {typeof item === "string" ? (
              <Markdown content={item} />
            ) : (
              <Space orientation="vertical" size={2}>
                <strong>
                  {text(d, "Title", text(d, "Scenario", text(d, "Severity")))}
                </strong>
                {text(d, "File") && (
                  <code>
                    {text(d, "File")}
                    {text(d, "Line") && ":" + text(d, "Line")}
                  </code>
                )}
                <Markdown
                  content={text(
                    d,
                    "Description",
                    text(d, "Decision", text(d, "Details", text(d, "Context"))),
                  )}
                />
                {field(d, "Passed") != null && (
                  <Tag color={field(d, "Passed") ? "green" : "red"}>
                    {field(d, "Passed") ? "Passed" : "Failed"}
                  </Tag>
                )}
                {text(d, "EvidenceId") && (
                  <Typography.Text copyable code>
                    {text(d, "EvidenceId")}
                  </Typography.Text>
                )}
                {list(d, "AcceptanceCriteria").length > 0 && (
                  <StructuredList items={list(d, "AcceptanceCriteria")} />
                )}
                {text(d, "SuggestedCorrection") && (
                  <Markdown content={text(d, "SuggestedCorrection")} />
                )}
              </Space>
            )}
          </li>
        );
      })}
    </ul>
  );
}
