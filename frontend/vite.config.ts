import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    port: 5173,
  },
  test: {
    environment: "jsdom",
    globals: true,
    // The API client throws when VITE_API_BASE_URL is absent, by design. Supplying it
    // here keeps the test run self-sufficient on a machine with no frontend/.env — CI
    // included — without reintroducing a production fallback.
    env: { VITE_API_BASE_URL: "http://localhost:5197" },
  },
});
