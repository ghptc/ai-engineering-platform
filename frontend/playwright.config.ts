import { defineConfig } from "@playwright/test";
const apiPort = process.env.E2E_API_PORT ?? "15088";
const frontendPort = process.env.E2E_FRONTEND_PORT ?? "15173";
export default defineConfig({
  testDir: "./tests/e2e",
  workers: 1,
  timeout: 90000,
  use: {
    baseURL: `http://127.0.0.1:${frontendPort}`,
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
  },
  webServer: [
    {
      command:
        "dotnet run --project ../backend/tests/AIEngineeringPlatform.E2EHost",
      url: `http://localhost:${apiPort}/health/live`,
      reuseExistingServer: false,
      timeout: 90000,
      env: { E2E_API_PORT: apiPort },
    },
    {
      command: `npm run dev -- --strictPort --port ${frontendPort}`,
      url: `http://127.0.0.1:${frontendPort}`,
      reuseExistingServer: false,
      env: { E2E_API_TARGET: `http://localhost:${apiPort}` },
    },
  ],
});
