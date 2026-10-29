import { useRef, useState } from "react";
import { Button, Card, Empty, Space, Typography } from "antd";
import { QueryState, Status, useApi } from "../../components/Common";
import { EvidenceDrawer } from "../../components/EvidenceDrawer";
import { summary } from "../../lib/engineering";
import type { Entry } from "../../types";
import type { RecordWindow } from "../../hooks/useLiveEvents";
export function ActivityFeed({ runId }: { runId: string }) {
  const viewport = useRef<HTMLDivElement>(null);
  const [pages, setPages] = useState<number[]>([]);
  const before = pages.at(-1);
  const [follow, setFollow] = useState(true);
  const [frozen, setFrozen] = useState<Entry[]>();
  const [selected, setSelected] = useState<Entry>();
  const feed = useApi<RecordWindow>(
    `/runs/${runId}/stream?limit=50${before != null ? `&before=${before}` : ""}`,
    !!runId,
    before == null ? 10000 : false,
  );
  const latest = () => {
    setPages([]);
    setFrozen(undefined);
    setFollow(true);
    if (viewport.current) viewport.current.scrollTop = 0;
  };
  return (
    <Card
      title="Execution activity"
      extra={
        <Space wrap>
          <Button
            aria-pressed={follow && before == null}
            onClick={() => {
              setFrozen(follow ? feed.data?.items : undefined);
              setFollow(!follow);
            }}
          >
            {follow ? "Pause auto-follow" : "Follow latest"}
          </Button>
          <Button onClick={latest}>Jump to Latest</Button>
        </Space>
      }
    >
      <Typography.Paragraph type="secondary">
        Newest first · 50 events per page ·{" "}
        {before != null ? "Inspecting older activity" : "Latest activity"}
      </Typography.Paragraph>
      <QueryState loading={feed.isLoading} error={feed.error}>
        <div
          ref={viewport}
          onScroll={(event) => {
            if (
              follow &&
              before == null &&
              event.currentTarget.scrollTop > 24
            ) {
              setFrozen(feed.data?.items);
              setFollow(false);
            }
          }}
          className="activity-window"
          data-testid="activity-window"
          role="region"
          aria-label="Execution activity"
          aria-live={follow && before == null ? "polite" : "off"}
        >
          {!feed.data?.items.length && (
            <Empty description="No persisted activity yet." />
          )}
          {(before != null ? feed.data?.items : (frozen ?? feed.data?.items))
            ?.slice(0, 50)
            .map((event) => (
              <button
                key={event.id}
                className="activity-row"
                onClick={() => {
                  setFrozen(feed.data?.items);
                  setFollow(false);
                  setSelected(event);
                }}
              >
                <time>{new Date(event.createdAt).toLocaleTimeString()}</time>
                <span className="activity-summary">
                  <strong>
                    {event.name.replace(/([a-z])([A-Z])/g, "$1 $2")}
                  </strong>
                  <span>{summary(event)}</span>
                </span>
                <Status
                  value={
                    event.status ||
                    (/Failed/.test(event.name) ? "Failed" : "Recorded")
                  }
                />
                <span aria-hidden>↗</span>
              </button>
            ))}
        </div>
      </QueryState>
      <Space className="history-controls">
        <Button
          disabled={!pages.length}
          onClick={() => setPages((p) => p.slice(0, -1))}
        >
          Newer activity
        </Button>
        <Button
          disabled={!feed.data?.hasMore}
          onClick={() => {
            setFrozen(undefined);
            setFollow(false);
            if (feed.data?.nextBefore != null)
              setPages((p) => [...p, feed.data!.nextBefore!]);
          }}
        >
          Older activity
        </Button>
      </Space>
      <EvidenceDrawer
        key={selected?.id ?? "none"}
        entry={selected}
        onClose={() => setSelected(undefined)}
      />
    </Card>
  );
}
