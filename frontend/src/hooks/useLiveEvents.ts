import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useQueryClient } from "@tanstack/react-query";
import type { Entry } from "../types";

export function useLiveEvents(projectId?: string) {
  const client = useQueryClient();
  useEffect(() => {
    if (!projectId) return;
    let stopped = false;
    let refresh: ReturnType<typeof setTimeout> | undefined;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/engineering")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    let cursor = Number(sessionStorage.getItem("sequence:" + projectId) || 0);
    const subscribe = () =>
      connection.invoke("SubscribeProject", projectId, cursor);
    connection.on("EngineeringEvent", (event: Entry) => {
      if (event.sequence <= cursor) return;
      cursor = event.sequence;
      sessionStorage.setItem("sequence:" + projectId, String(cursor));
      // Replay and busy agents can emit many events in a single turn. Coalesce
      // them to avoid a request storm, while REST polling remains authoritative.
      if (!refresh)
        refresh = setTimeout(() => {
          refresh = undefined;
          if (!stopped)
            void client.invalidateQueries({
              predicate: (query) => {
                const path = String(query.queryKey[0]);
                return (
                  !path.startsWith("/auth/") &&
                  !path.startsWith("/settings") &&
                  !path.startsWith("/agents")
                );
              },
            });
        }, 500);
    });
    connection.onreconnected(() => {
      void subscribe();
    });
    const timer = setTimeout(async () => {
      if (stopped) return;
      try {
        await connection.start();
        if (!stopped) await subscribe();
      } catch {
        /* REST polling remains authoritative during a disconnected hub. */
      }
    }, 0);
    return () => {
      stopped = true;
      clearTimeout(timer);
      if (refresh) clearTimeout(refresh);
      void connection.stop();
    };
  }, [projectId, client]);
}
