import { defineConfig } from "@playwright/test";
export default defineConfig({
  testDir: "./tests/e2e",
  workers: 1,
  timeout: 90000,
  use: {
    baseURL: "http://127.0.0.1:5173",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --project ../backend/tests/AIEngineeringPlatform.E2EHost",
      url: "http://localhost:5088/health/live",
      reuseExistingServer: !process.env.CI,
      timeout: 90000,
    },
    {
      command: "npm run dev -- --port 5173",
      url: "http://127.0.0.1:5173",
      reuseExistingServer: !process.env.CI,
      env: { E2E_API_TARGET: "http://localhost:5088" },
    },
  ],
});
