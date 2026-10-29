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
    page.getByLabel("Selected engineering team").locator(".agent-tile"),
  ).toHaveCount(4);
  await expect(
    page.getByText("Recorded handoffs", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Backend Engineer → Test Engineer", { exact: true }),
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
    page.getByText("Backend Engineer", { exact: true }).first(),
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
  await page.getByText("Context sources measured", { exact: true }).click();
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
    .getByRole("button", { name: "Jump to Latest", exact: true })
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
    page.getByLabel("Selected engineering team").locator(".agent-tile"),
  ).toHaveCount(4);
  await page.keyboard.press("Escape");
  expect(
    await page.evaluate(() => document.documentElement.scrollWidth),
  ).toBeLessThanOrEqual(390);
  await page.screenshot({
    path: "../backend/docs/ux-evidence/mobile-light.png",
    fullPage: true,
  });
});
