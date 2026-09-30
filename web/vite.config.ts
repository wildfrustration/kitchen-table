import path from "node:path";
import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { defineConfig } from "vite";

// Dev: Vite on :5173 proxies /api to the .NET API on :5080.
// Build: output goes to the API's wwwroot so one container serves both.
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { "@": path.resolve(__dirname, "./src") } },
  server: {
    port: 5173,
    proxy: { "/api": "http://localhost:5080" },
  },
  build: {
    outDir: "../src/PlanD.Api/wwwroot",
    emptyOutDir: true,
  },
});
