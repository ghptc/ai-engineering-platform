import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      "/api": {
        target: process.env.E2E_API_TARGET || "http://localhost:8080",
        changeOrigin: false, // Preserve the browser authority for same-origin CSRF validation.
      },
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
