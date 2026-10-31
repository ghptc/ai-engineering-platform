import { test, expect } from "@playwright/test";
test("owner creates a project, runs engineering cycle, reviews evidence and approves delivery", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByLabel("Owner credential")
    .fill("testing-owner-credential-2026");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Your engineering workspace" }),
  ).toBeVisible();
  await page.getByRole("link", { name: "Projects", exact: true }).click();
  await page.getByRole("button", { name: "New project" }).click();
  await page.getByLabel("Project name").fill("Browser verified product");
  await page
    .getByLabel("Description", { exact: true })
    .fill("Deterministic test fixture");
  await page
    .getByLabel("Initial development context")
    .fill(
      "Create a feature, write tests, validate and review it with traceable evidence.",
    );
  await page.getByRole("button", { name: "Save project" }).click();
  await page
    .locator(".ant-card")
    .filter({
      has: page.getByRole("heading", {
        name: "Browser verified product",
        exact: true,
      }),
    })
    .getByRole("link", { name: /Open workspace/ })
    .click();
  await expect(
    page.getByRole("heading", { name: "Browser verified product" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Project actions ⌄" }).click();
  await page.getByRole("menuitem", { name: "Connect repository" }).click();
  await page.getByLabel("owner", { exact: true }).fill("test-owner");
  await page.getByLabel("repository", { exact: true }).fill("test-repo");
  await page.getByLabel("GitHub App installation ID").fill("1");
  await page.getByRole("button", { name: "Verify and connect" }).click();
  await expect(page.getByText("Repository access verified")).toBeVisible();
  await page.getByRole("tab", { name: "Client conversation" }).click();
  await page.getByRole("button", { name: "New conversation" }).click();
  await page
    .getByRole("textbox", { name: "Message", exact: true })
    .fill("Acceptance criterion: all changes need verified test evidence.");
  await page.getByRole("button", { name: /Send message/ }).click();
  await expect(
    page.getByText(
      "Acceptance criterion: all changes need verified test evidence.",
    ),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Project instructions", exact: true })
    .click();
  await page
    .getByLabel("Standing instructions")
    .fill(
      "All changes require verified test evidence. Never modify the protected default branch.",
    );
  await page.getByRole("button", { name: "Save instructions" }).click();
  await expect(page.getByText("Project instructions saved")).toBeVisible();
  await page.getByRole("button", { name: "Start focused task" }).click();
  await page
    .getByLabel("Task title", { exact: true })
    .fill("Browser focused feature");
  await page
    .getByLabel("Change to implement")
    .fill("Implement the existing feature using project conventions.");
  await page
    .getByLabel("Acceptance criteria (one per line)")
    .fill("Tests pass against the changed source");
  await page.getByRole("button", { name: "Run tasks", exact: true }).click();
  await expect(
    page.getByText("Awaiting Human Approval", { exact: true }),
  ).toBeVisible({ timeout: 45000 });
  const runUrl = page.url();
  await expect(
    page.getByRole("button", { name: "Approve", exact: true }),
  ).toBeVisible();
  await page.waitForTimeout(2000); // Let transient save notifications leave the visual capture.
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-dark.png",
    fullPage: true,
  });
  await expect(
    page
      .getByLabel("Live directed engineering workflow")
      .locator("g[role=button]"),
  ).toHaveCount(12);
  await page
    .getByRole("button", { name: /Backend Engineer · Completed/ })
    .click();
  await expect(
    page.getByRole("region", { name: "Execution history", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: /Write File/ })
    .first()
    .click();
  await expect(page.locator(".execution-terminal")).toContainText([
    "written backend/Feature.cs",
  ]);
  await expect(
    page.getByText("Copy output page", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("tab", { name: "Execution timeline", exact: true })
    .click();
  await page.reload();
  await expect(
    page.getByText("Awaiting Human Approval", { exact: true }),
  ).toBeVisible();
  await page
    .getByRole("tab", { name: "Execution timeline", exact: true })
    .click();
  await expect(
    page.getByText("Agent Started", { exact: true }).first(),
  ).toBeVisible();
  await page.getByRole("tab", { name: /Artifacts/ }).click();
  await expect(
    page.getByRole("cell", { name: "TestReport", exact: true }).first(),
  ).toBeVisible();
  await expect(
    page.getByRole("cell", { name: "CodeReview", exact: true }),
  ).toBeVisible();
  await page.getByRole("link", { name: "Code reviews", exact: true }).click();
  await expect(
    page.getByRole("cell", { name: "Local code review", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("link", { name: "Agent collaboration", exact: true })
    .click();
  await page
    .locator(".ant-select")
    .filter({ has: page.getByRole("combobox", { name: "Select project" }) })
    .click();
  await page
    .locator(".ant-select-item-option-content")
    .filter({ hasText: /^Browser verified product$/ })
    .click();
  await expect(
    page.getByRole("button", { name: /Backend Engineer ·/ }).first(),
  ).toBeVisible();
  await page.getByRole("link", { name: "Approvals", exact: true }).click();
  await page
    .getByRole("button", { name: "Approve", exact: true })
    .first()
    .click();
  await page
    .getByRole("textbox", { name: "Decision feedback" })
    .fill("Reviewed deterministic test evidence.");
  await page
    .getByRole("dialog")
    .getByRole("button", { name: "Approve", exact: true })
    .click();
  await expect(page.getByText("Approved", { exact: true })).toBeVisible();
  await page.goto(runUrl);
  await expect(
    page.getByText("Completed", { exact: true }).first(),
  ).toBeVisible({ timeout: 30000 });
  await page.reload();
  await expect(
    page.getByText("Completed", { exact: true }).first(),
  ).toBeVisible();
  await page
    .getByRole("tab", { name: "Execution timeline", exact: true })
    .click();
  await expect(page.getByText("Run Completed", { exact: true })).toBeVisible();
});

test("large histories, safe details, cost separation and mobile navigation", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByLabel("Owner credential")
    .fill("testing-owner-credential-2026");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByText("Active engineering", { exact: true }),
  ).toBeVisible();
  await expect(page.getByText("Tokens used", { exact: true })).toHaveCount(0);
  await expect(
    page.getByText("Estimated model cost", { exact: true }),
  ).toHaveCount(0);
  await page.screenshot({
    path: "../backend/docs/ux-evidence/dashboard-dark.png",
    fullPage: true,
  });
  await page.getByRole("link", { name: "Usage & Costs", exact: true }).click();
  await expect(
    page.getByText("Costs are estimates, not provider invoices"),
  ).toBeVisible();
  await page.screenshot({
    path: "../backend/docs/ux-evidence/usage-dark.png",
    fullPage: true,
  });
  let requests = 0;
  page.on("request", (r) => {
    if (r.url().includes("/api/v1/")) requests++;
  });
  await page.goto("/executions/ux-history-run?tab=timeline");
  const feed = page.getByTestId("activity-window");
  await expect(feed.locator(".activity-row")).toHaveCount(50);
  expect(
    await feed.evaluate((el) => el.getBoundingClientRect().height),
  ).toBeLessThanOrEqual(560);
  await expect(
    page.getByText("COMPLETE_CONTEXT_PAYLOAD", { exact: false }),
  ).toHaveCount(0);
  await page
    .getByRole("button", { name: /Inspect evidence ux-history-event-999/ })
    .click();
  await expect(page.getByRole("dialog")).toBeVisible();
  await page.getByRole("button", { name: "Raw payload", exact: true }).click();
  await page.getByRole("button", { name: /Payload · 3 fields/ }).click();
  await expect(
    page.locator(".json-value").filter({ hasText: "COMPLETE_CONTEXT_PAYLOAD" }),
  ).toBeVisible();
  await expect(
    page.getByText("Show complete value", { exact: true }),
  ).toBeVisible();
  await page.screenshot({
    path: "../backend/docs/ux-evidence/evidence-drawer-dark.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page
    .getByRole("button", { name: "Older activity", exact: true })
    .click();
  await expect(
    page.getByText("Inspecting older activity", { exact: false }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Jump to latest", exact: true })
    .click();
  await expect(
    page.getByText("Context sources measured", { exact: true }),
  ).toBeVisible();
  expect(requests).toBeLessThan(50);
  await page.goto("/projects/ux-history-project");
  await expect(
    page.getByText("Product objective:", { exact: false }).first(),
  ).toBeVisible();
  await expect(
    page.getByText("Historical context stays behind disclosure.", {
      exact: false,
    }),
  ).toHaveCount(0);
  await page.getByText("Full product context", { exact: true }).click();
  await expect(page.locator(".context-window")).toBeVisible();
  await expect(page.locator(".context-window h1")).toHaveText(
    "Product knowledge",
  );
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("switch", { name: "Dark mode" }).click();
  await page.getByRole("button", { name: "Open navigation" }).click();
  await page
    .getByRole("dialog")
    .getByRole("link", { name: "Agent collaboration", exact: true })
    .click();
  await expect(page.getByRole("dialog")).toBeHidden();
  await expect(
    page.getByRole("heading", { name: "Agent collaboration", exact: true }),
  ).toBeVisible();
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(390);
  await page
    .locator(".ant-select")
    .filter({ has: page.getByRole("combobox", { name: "Select project" }) })
    .click();
  await page
    .locator(".ant-select-item-option-content")
    .filter({ hasText: /^Engineering history fixture$/ })
    .click();
  await expect(
    page
      .getByLabel("Live directed engineering workflow")
      .locator("g[role=button]"),
  ).toHaveCount(12);
  await page.keyboard.press("Escape");
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(390);
  await page.screenshot({
    path: "../backend/docs/ux-evidence/mobile-light.png",
    fullPage: true,
  });
});

test("persisted DevPulse incident has an honest pipeline, bounded terminal and responsive controls", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByLabel("Owner credential")
    .fill("testing-owner-credential-2026");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Your engineering workspace" }),
  ).toBeVisible();
  await page.goto("/executions/b09188be6e3743c797092d1e73ededd6");
  await expect(
    page.getByRole("button", { name: "Implementation · Blocked", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Build · Not observed", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Tests · Not observed", exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Fit to view", exact: true }).click();
  await page.getByRole("button", { name: /Inspect path records/ }).click();
  await expect(
    page.getByText("Business Analyst → Architect", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: /Hide path records/ }).click();
  await page.getByLabel("Pipeline canvas height").fill("400");
  await page.getByLabel("Pipeline canvas height").press("Enter");
  await page
    .getByRole("button", { name: /Write File/ })
    .first()
    .click();
  await expect(
    page.getByText("Safe recorded arguments", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText(/Arguments not recorded for this operation/),
  ).toBeVisible();
  await expect(
    page.getByText("Recorded evidence", { exact: true }),
  ).toBeVisible();
  await page.locator(".execution-history-window").evaluate((el) => {
    el.scrollTop = 0;
  });
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    window.scrollTo(0, 0);
  });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-observability-tool.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: /Collapse pipeline/ }).click();
  await expect(
    page.getByRole("region", { name: "Live directed engineering workflow" }),
  ).toHaveCount(0);
  await page.getByRole("button", { name: /Expand pipeline/ }).click();
  await page
    .getByRole("button", { name: /Write File/ })
    .first()
    .click();
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    window.scrollTo(0, 0);
  });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-observability-dark.png",
    fullPage: true,
  });
  await page.getByText("Timeline", { exact: true }).click();
  await expect(page.locator(".timeline-track").first()).toBeVisible();
  await page.getByText("Detailed", { exact: true }).click();
  await expect(page.locator(".record-identity").first()).toContainText("#");
  await page
    .getByLabel("Search execution history")
    .fill("no matching operation");
  await expect(
    page.getByText(/No matching records in this page/),
  ).toBeVisible();
  await page.getByLabel("Search execution history").clear();
  await page
    .getByRole("button", { name: "Jump to latest", exact: true })
    .click();
  await expect(
    page.getByRole("button", { name: "Pause-follow", exact: true }),
  ).toHaveAttribute("aria-pressed", "true");
  await page.getByRole("button", { name: "Pause-follow", exact: true }).click();
  await expect(
    page.getByRole("button", { name: "Follow latest", exact: true }),
  ).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Fit to view", exact: true }).click();
  await expect(
    page.getByRole("button", { name: /Backend Engineer ·/ }),
  ).toBeAttached();
  await page.getByRole("button", { name: /Collapse pipeline/ }).focus();
  await page.keyboard.press("Enter");
  await expect(
    page.getByRole("button", { name: /Expand pipeline/ }),
  ).toBeVisible();
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(390);
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    window.scrollTo(0, 0);
  });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-observability-mobile.png",
    fullPage: true,
  });
});

test("persisted contract fixture exposes responsible rework, retries and measured waiting", async ({
  page,
}) => {
  await page.goto("/");
  await page
    .getByLabel("Owner credential")
    .fill("testing-owner-credential-2026");
  await page.getByRole("button", { name: "Sign in", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Your engineering workspace" }),
  ).toBeVisible();
  await page.goto("/executions/ux-rework-run");
  await expect(
    page.getByRole("button", { name: /Implementation · Blocked/ }),
  ).toBeVisible();
  await page.getByRole("button", { name: /Inspect path records/ }).click();
  await expect(
    page.getByText(
      "QA Engineer → Frontend Engineer: Deterministic fixture: inaccessible control",
      { exact: true },
    ),
  ).toBeVisible();
  await expect(page.locator(".pipeline-edge.flow-rework")).toHaveCount(2);
  await page.getByRole("combobox", { name: "Workflow iteration" }).click();
  await page
    .locator(".ant-select-item-option-content")
    .filter({ hasText: /^Iteration 2$/ })
    .click();
  await expect(page.locator(".pipeline-edge.flow-rework")).toHaveCount(1);
  await page.getByRole("button", { name: /Hide path records/ }).click();
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    window.scrollTo(0, 0);
  });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-observability-rework.png",
    fullPage: true,
  });
  await page
    .getByRole("tab", { name: "Execution timeline", exact: true })
    .click();
  await expect(page.getByText("Retry", { exact: true })).toBeVisible();
  await expect(page.getByText("Resumed", { exact: true })).toBeVisible();
  await expect(
    page.getByText(/Waiting period: Paused · 1.0 min/),
  ).toBeVisible();
  await page.getByText("Detailed", { exact: true }).click();
  await expect(page.getByText(/ux-retry-event-2/)).toBeVisible();
  await page.evaluate(() => {
    (document.activeElement as HTMLElement)?.blur();
    window.scrollTo(0, 0);
  });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/execution-observability-history.png",
    fullPage: true,
  });
});
