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
    .getByRole("link", { name: /Open workspace/ })
    .first()
    .click();
  await expect(
    page.getByRole("heading", { name: "Browser verified product" }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Connect repository" }).click();
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
  await page.getByRole("button", { name: "Start execution" }).click();
  await expect(
    page.getByText("Awaiting Human Approval", { exact: true }),
  ).toBeVisible({ timeout: 45000 });
  const runUrl = page.url();
  await page.reload();
  await expect(
    page.getByText("Awaiting Human Approval", { exact: true }),
  ).toBeVisible();
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
  await page.getByRole("combobox", { name: "Select project" }).click();
  await page
    .getByText("Browser verified product", { exact: true })
    .last()
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
  await expect(page.getByText("Run Completed", { exact: true })).toBeVisible();
});
