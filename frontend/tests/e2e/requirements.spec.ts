import { test, expect } from "@playwright/test";
test("owner imports 125 requirements, reviews a version and reconnects without starting execution", async ({
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
  const project = await page.request.post("/api/v1/projects", {
    headers: { "X-Requested-With": "AIEngineeringPlatform" },
    data: {
      name: "M6 intake browser fixture",
      description: "125 real persisted requirements; no execution",
      originalContext: "",
      technologyStack: [".NET 10", "React"],
    },
  });
  expect(project.ok()).toBeTruthy();
  const { id } = await project.json();
  await page.goto(`/projects/${id}`);
  await page
    .getByRole("tab", { name: "Requirements intake", exact: true })
    .click();
  await expect(page.getByText(/No imported requirements/)).toBeVisible();
  const source = Array.from(
    { length: 125 },
    (_, i) => `REQ-${i + 1}: Browser requirement ${i + 1}`,
  ).join("\n");
  await page.getByLabel("Requirements document").setInputFiles({
    name: "requirements.md",
    mimeType: "text/markdown",
    buffer: Buffer.from(source),
  });
  await page.getByRole("button", { name: "Import draft scope" }).click();
  await page.getByRole("button", { name: "Import draft", exact: true }).click();
  await expect(
    page.getByText("125 requirements", { exact: true }).first(),
  ).toBeVisible();
  expect(
    await page
      .locator(".requirement-intake .ant-table-tbody tr[data-row-key]")
      .count(),
  ).toBe(20);
  await page.getByLabel("Search imported requirements").fill("REQ-125:");
  await expect(
    page.getByText("REQ-125: Browser requirement 125", { exact: true }),
  ).toBeVisible();
  await page.getByRole("button", { name: "Review", exact: true }).click();
  await page
    .getByLabel("Acceptance criteria (one per line)")
    .fill(
      "Owner checks the delivered endpoint with an executed HTTP integration test.",
    );
  await page.getByRole("button", { name: "Save owner review" }).click();
  await expect(page.getByText("Scope v2", { exact: true })).toBeVisible();
  await page.reload();
  await page
    .getByRole("tab", { name: "Requirements intake", exact: true })
    .click();
  await expect(page.getByText("Scope v2", { exact: true })).toBeVisible();
  await expect(page.getByText("1 reviewed", { exact: true })).toBeVisible();
  await page.getByText("Immutable scope history", { exact: true }).click();
  await page.getByRole("button", { name: /^v1 ·/ }).click();
  await expect(
    page.getByText("Read-only historical scope", { exact: true }),
  ).toBeVisible();
  await expect(page.getByText("0 reviewed", { exact: true })).toBeVisible();
  await page
    .getByRole("button", { name: "Return to current", exact: true })
    .click();
  await expect(page.getByText("1 reviewed", { exact: true })).toBeVisible();
  const runs = await page.request.get(`/api/v1/projects/${id}/runs`);
  expect((await runs.json()).items).toHaveLength(0);
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(
    page.getByRole("heading", { name: "Requirements intake & traceability" }),
  ).toBeVisible();
  await expect(page.getByLabel("Search imported requirements")).toBeVisible();
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= window.innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: "../backend/docs/ux-evidence/m6-intake-mobile.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.screenshot({
    path: "../backend/docs/ux-evidence/m6-intake-desktop.png",
    fullPage: true,
  });
});
