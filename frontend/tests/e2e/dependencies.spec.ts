import { test, expect } from "@playwright/test";
test("owner reviews a scoped dependency graph and reloads its persisted approval without automatic resume", async ({
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
  await page.goto("/projects/ux-dependency-project");
  await page.getByRole("tab", { name: "Dependencies", exact: true }).click();
  await expect(
    page.getByText("is-number@7.0.0", { exact: true }),
  ).toBeVisible();
  await expect(page.getByText("ReadyForReview", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Approve selected packages" }),
  ).toBeDisabled();
  await page
    .getByRole("checkbox", { name: "Select row 1", exact: true })
    .check();
  await page.getByRole("button", { name: "Approve selected packages" }).click();
  await page
    .getByLabel("Dependency decision reason")
    .fill(
      "Review fixture exact graph, origin, integrity and unknown security findings.",
    );
  await page.getByRole("button", { name: "OK", exact: true }).click();
  await expect(page.getByText("Approved", { exact: true })).toBeVisible();
  await page.reload();
  await page.getByRole("tab", { name: "Dependencies", exact: true }).click();
  await expect(page.getByText("Approved", { exact: true })).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Resume same execution" }),
  ).toBeEnabled();
  const response = await page.request.get("/api/v1/runs/ux-dependency-run");
  const run = await response.json();
  expect(run.currentStage).toBe("AwaitingDependencyApproval");
  expect(run.toolCalls).toBe(26);
  expect(run.cost).toBe(0.2);
  expect(run.policy.maxToolCalls).toBe(100);
  await page.getByText("Owner audit history (2)", { exact: true }).click();
  await expect(page.getByText(/Review fixture exact graph/)).toBeVisible();
  await page.setViewportSize({ width: 390, height: 844 });
  await expect
    .poll(() =>
      page.evaluate(
        () => document.documentElement.scrollWidth <= window.innerWidth,
      ),
    )
    .toBe(true);
  await page.screenshot({
    path: "../backend/docs/ux-evidence/dependency-approval-mobile.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/dependency-approval-desktop.png",
    fullPage: true,
  });
});
