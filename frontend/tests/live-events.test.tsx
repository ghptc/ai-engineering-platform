import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { act, cleanup, render } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { useLiveEvents } from "../src/hooks/useLiveEvents";
const hub = vi.hoisted(() => ({
  on: vi.fn(),
  onreconnected: vi.fn(),
  onreconnecting: vi.fn(),
  onclose: vi.fn(),
  start: vi.fn(),
  stop: vi.fn(),
  invoke: vi.fn(),
}));
vi.mock("@microsoft/signalr", () => ({
  LogLevel: { None: 0 },
  HubConnectionBuilder: class {
    withUrl() {
      return this;
    }
    withAutomaticReconnect() {
      return this;
    }
    configureLogging() {
      return this;
    }
    build() {
      return hub;
    }
  },
}));
function Listener() {
  useLiveEvents("p");
  return null;
}
beforeEach(() => {
  vi.useFakeTimers();
  vi.clearAllMocks();
  hub.start.mockResolvedValue(undefined);
  hub.stop.mockResolvedValue(undefined);
  hub.invoke.mockResolvedValue(123);
});
afterEach(() => {
  cleanup();
  vi.useRealTimers();
});
it("shares subscriptions, batches replay, deduplicates out-of-order entries and cleans up", async () => {
  const client = new QueryClient();
  const key = ["/runs/r/stream?limit=50"];
  client.setQueryData(key, { items: [], hasMore: false });
  const view = render(
    <QueryClientProvider client={client}>
      <Listener />
      <Listener />
    </QueryClientProvider>,
  );
  await act(async () => {});
  expect(hub.start).toHaveBeenCalledTimes(1);
  const receive = hub.on.mock.calls.find(
    (c) => c[0] === "EngineeringEvent",
  )![1];
  await act(async () => {
    for (let i = 0; i < 1000; i++)
      receive({
        id: "e" + i,
        kind: "event",
        runId: "r",
        projectId: "p",
        sequence: 1000 - i,
      });
    receive({ id: "e0", kind: "event", runId: "r", sequence: 1000 });
    vi.advanceTimersByTime(500);
  });
  const items = (
    client.getQueryData(key) as { items: { id: string; sequence: number }[] }
  ).items;
  expect(items).toHaveLength(50);
  expect(items[0].sequence).toBe(1000);
  expect(new Set(items.map((e) => e.id)).size).toBe(50);
  view.unmount();
  expect(hub.stop).toHaveBeenCalledTimes(1);
});
it("reconnects from completed persisted replay rather than a possibly out-of-order live cursor", async () => {
  const client = new QueryClient();
  render(
    <QueryClientProvider client={client}>
      <Listener />
    </QueryClientProvider>,
  );
  await act(async () => {});
  const receive = hub.on.mock.calls.find(
    (c) => c[0] === "EngineeringEvent",
  )![1];
  receive({ id: "live", kind: "event", runId: "r", sequence: 999 });
  await act(async () => {
    hub.onreconnected.mock.calls[0][0]();
  });
  expect(hub.invoke).toHaveBeenLastCalledWith("SubscribeProject", "p", 123);
});

it("retries an initially unavailable hub and cancels retries on unmount", async () => {
  hub.start.mockRejectedValueOnce(new Error("Unavailable"));
  const client = new QueryClient();
  const view = render(
    <QueryClientProvider client={client}>
      <Listener />
    </QueryClientProvider>,
  );
  await act(async () => {});
  expect(hub.start).toHaveBeenCalledTimes(1);
  await act(async () => {
    vi.advanceTimersByTime(5000);
  });
  expect(hub.start).toHaveBeenCalledTimes(2);
  view.unmount();
  await act(async () => {
    vi.advanceTimersByTime(10000);
  });
  expect(hub.start).toHaveBeenCalledTimes(2);
});
