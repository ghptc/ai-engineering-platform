import { useEffect, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient, type QueryClient } from "@tanstack/react-query";
import type { Entry } from "../types";
import { mergeEntries } from "../lib/engineering";
export interface RecordWindow {
  items: Entry[];
  nextBefore?: number;
  hasMore: boolean;
}
type LiveState = "Connecting" | "Live" | "Reconnecting" | "Polling";
const registries = new WeakMap<
  QueryClient,
  Map<
    string,
    {
      listeners: Set<(s: LiveState) => void>;
      stop: () => void;
      state: LiveState;
    }
  >
>();
export function useLiveEvents(projectId?: string) {
  const client = useQueryClient();
  const [state, setState] = useState<LiveState>("Connecting");
  useEffect(() => {
    if (!projectId) return;
    let registry = registries.get(client);
    if (!registry) {
      registry = new Map();
      registries.set(client, registry);
    }
    let shared = registry.get(projectId);
    if (!shared) {
      const listeners = new Set<(s: LiveState) => void>();
      const connection = new HubConnectionBuilder()
        .withUrl("/hubs/engineering")
        .withAutomaticReconnect()
        .configureLogging(LogLevel.None)
        .build();
      let stopped = false;
      let retry: ReturnType<typeof setTimeout> | undefined;
      let acknowledged = -1; // Only a completed persisted replay advances this watermark.
      const seen = new Set<string>();
      const pending = new Map<string, Entry>();
      let timer: ReturnType<typeof setTimeout> | undefined;
      const notify = (next: LiveState) => {
        if (shared) shared.state = next;
        listeners.forEach((fn) => fn(next));
      };
      const flush = () => {
        timer = undefined;
        if (stopped) return;
        const batch = [...pending.values()];
        pending.clear();
        client
          .getQueryCache()
          .findAll()
          .forEach((query) => {
            const path = String(query.queryKey[0]);
            if (!path.includes("/stream") || path.includes("before=")) return;
            const relevant = batch.filter(
              (e) =>
                path.startsWith(`/runs/${e.runId}/stream`) &&
                e.kind ===
                  (new URLSearchParams(path.split("?")[1]).get("kind") ??
                    "event"),
            );
            if (!relevant.length) return;
            client.setQueryData<RecordWindow>(query.queryKey, (old) => {
              if (!old) return old;
              const items = mergeEntries(old.items, relevant, 50);
              return {
                ...old,
                items,
                nextBefore: items.at(-1)?.sequence,
                hasMore: old.hasMore || old.items.length + relevant.length > 50,
              };
            });
          });
        const runIds = new Set(batch.map((e) => e.runId));
        void client.invalidateQueries({
          predicate: (q) => {
            const path = String(q.queryKey[0]);
            if (path.includes("/stream?kind=tool"))
              return (
                !path.includes("before=") &&
                batch.some(
                  (e) =>
                    path.startsWith(`/runs/${e.runId}/stream`) &&
                    [
                      "ToolInvocationCompleted",
                      "ExecutorOperationBlocked",
                    ].includes(e.name),
                )
              );
            if (
              path.includes("/stream") &&
              !path.includes("kind=artifact") &&
              !path.includes("kind=handoff") &&
              !path.includes("kind=message")
            )
              return false;
            return (
              path === "/activity" ||
              path === "/approvals" ||
              path.includes("/reviews") ||
              path.startsWith(`/projects/${projectId}`) ||
              path.startsWith("/projects/all/runs") ||
              path.startsWith("/conversations/") ||
              [...runIds].some(
                (id) =>
                  id &&
                  (path === `/runs/${id}` ||
                    path.startsWith(`/runs/${id}/stream?kind=`)),
              )
            );
          },
        });
      };
      connection.on("EngineeringEvent", (event: Entry) => {
        if (seen.has(event.id)) return;
        seen.add(event.id);
        if (seen.size > 2000) seen.delete(seen.values().next().value!);
        pending.set(event.id, event);
        if (!timer) timer = setTimeout(flush, 500);
      });
      const subscribe = async () => {
        try {
          const watermark = await connection.invoke<number>(
            "SubscribeProject",
            projectId,
            acknowledged,
          );
          if (stopped) return;
          if (typeof watermark === "number") acknowledged = watermark;
          notify("Live");
          void client.invalidateQueries({
            predicate: (q) =>
              String(q.queryKey[0]).startsWith(`/runs/`) ||
              String(q.queryKey[0]).startsWith("/conversations/"),
          });
        } catch {
          if (!stopped) notify("Polling");
        }
      };
      connection.onreconnecting(() => notify("Reconnecting"));
      connection.onreconnected(() => {
        void subscribe();
      });
      connection.onclose(() => {
        if (!stopped) {
          notify("Polling");
          retry = setTimeout(connect, 5000);
        }
      });
      shared = {
        listeners,
        state: "Connecting",
        stop: () => {
          stopped = true;
          if (timer) clearTimeout(timer);
          if (retry) clearTimeout(retry);
          pending.clear();
          void connection.stop();
        },
      };
      registry.set(projectId, shared);
      function connect() {
        if (stopped) return;
        void connection
          .start()
          .then(() => {
            if (!stopped) void subscribe();
          })
          .catch(() => {
            if (!stopped) {
              notify("Polling");
              retry = setTimeout(connect, 5000);
            }
          });
      }
      connect();
    }
    shared.listeners.add(setState);
    setState(shared.state);
    return () => {
      shared!.listeners.delete(setState);
      if (shared!.listeners.size === 0) {
        shared!.stop();
        registry!.delete(projectId);
      }
    };
  }, [projectId, client]);
  return state;
}
