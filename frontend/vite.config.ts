import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": process.env.E2E_API_TARGET || "http://localhost:8080",
      "/hubs": {
        target: process.env.E2E_API_TARGET || "http://localhost:8080",
        ws: true,
      },
    },
  },
  test: {
    environment: "jsdom",
    setupFiles: ["./tests/setup.ts"],
    exclude: ["**/node_modules/**", "tests/e2e/**"],
  },
});
