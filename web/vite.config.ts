import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  base: "/app/",
  publicDir: false,
  build: {
    outDir: "../src/Zapara.Web/wwwroot",
    emptyOutDir: false,
    assetsDir: "assets"
  },
  server: {
    port: 5173,
    proxy: {
      "/api": "http://127.0.0.1:5187",
      "/web-api": "http://127.0.0.1:5187"
    }
  }
});
