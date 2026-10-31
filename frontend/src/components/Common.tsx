import { Alert, Empty, Spin, Tag, Typography } from "antd";
import { useQuery } from "@tanstack/react-query";
import { api } from "../services/api";
import type { ReactNode } from "react";
export function useApi<T>(
  path: string,
  enabled = true,
  interval: number | false = false,
) {
  return useQuery({
    queryKey: [path],
    queryFn: () => api<T>(path),
    enabled,
    refetchInterval: interval,
    staleTime: 2000,
    retry: (count, error) =>
      !("status" in error && [401, 403, 404].includes(Number(error.status))) &&
      count < 2,
  });
}
export function Status({ value }: { value: string }) {
  const color = /Completed|Approved|APPROVE|Succeeded|Healthy/.test(value)
    ? "green"
    : /Failed|Rejected|REQUEST_CHANGES|Cancelled|Blocked|OutcomeUnknown|Expired/.test(value)
      ? "red"
      : /Paused|Approval|Pending/.test(value)
        ? "gold"
        : "blue";
  return <Tag color={color}>{value.replace(/([a-z])([A-Z])/g, "$1 $2")}</Tag>;
}
export function QueryState({
  loading,
  error,
  empty,
  children,
}: {
  loading: boolean;
  error: Error | null;
  empty?: boolean;
  children: ReactNode;
}) {
  if (loading)
    return (
      <div className="loading" role="status" aria-label="Loading content">
        <Spin size="large" />
      </div>
    );
  if (error)
    return (
      <Alert
        type="error"
        showIcon
        title="Unable to load data"
        description={error.message}
      />
    );
  if (empty)
    return (
      <Empty description="Nothing here yet. Create a project or start an execution to begin." />
    );
  return <>{children}</>;
}
export function Heading({
  eyebrow,
  title,
  description,
  actions,
}: {
  eyebrow: string;
  title: string;
  description?: string;
  actions?: ReactNode;
}) {
  return (
    <div className="page-heading">
      <div>
        <span className="eyebrow">{eyebrow}</span>
        <Typography.Title level={2}>{title}</Typography.Title>
        {description && (
          <Typography.Paragraph type="secondary">
            {description}
          </Typography.Paragraph>
        )}
      </div>
      {actions}
    </div>
  );
}
export function jsonText(content: string) {
  try {
    return JSON.stringify(JSON.parse(content), null, 2);
  } catch {
    return content;
  }
}
