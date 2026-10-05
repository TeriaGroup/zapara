import fs from "node:fs";
import { createHash } from "node:crypto";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { normalizeHtmlLf } from "./src/html-normalization.ts";

const webRoot = path.dirname(fileURLToPath(import.meta.url));
const sharedRoot = path.resolve(webRoot, "../src/Zapara.Web/wwwroot");

function serveSharedFiles() {
  return {
    name: "serve-shared-files",
    configureServer(server) {
      server.middlewares.use((req, res, next) => {
        const raw = req.url?.split("?")[0] ?? "";
        const allowed = raw.startsWith("/app/fonts/") || raw.startsWith("/app/icons/") || raw === "/app/manifest.webmanifest";
        if (!allowed) return next();
        const file = path.resolve(sharedRoot, raw.slice("/app/".length));
        if (!file.startsWith(sharedRoot) || !fs.existsSync(file) || !fs.statSync(file).isFile()) return next();
        const type = file.endsWith(".ttf") ? "font/ttf"
          : file.endsWith(".webmanifest") ? "application/manifest+json"
          : file.endsWith(".svg") ? "image/svg+xml"
          : file.endsWith(".png") ? "image/png"
          : "application/octet-stream";
        res.setHeader("Content-Type", type);
        fs.createReadStream(file).pipe(res);
      });
    }
  };
}

export default defineConfig({
  plugins: [react(), serveSharedFiles(), {name:"public-html-lf", transformIndexHtml:{order:"post",handler:normalizeHtmlLf}}, { name: "react-offline-worker", generateBundle(_options, bundle) { const source=fs.readFileSync(path.join(webRoot, "react-worker.js"), "utf8"); const buildId=createHash("sha256").update(source + Object.keys(bundle).sort().join("|")).digest("hex").slice(0,20); this.emitFile({ type: "asset", fileName: "react-worker.js", source: source.replace("__ZAPARA_REACT_BUILD__",buildId) }); } }],
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
      "/api": { target: process.env.ZAPARA_API_PROXY || "http://127.0.0.1:5187", changeOrigin: false },
      "/web-api": { target: process.env.ZAPARA_API_PROXY || "http://127.0.0.1:5187", changeOrigin: false }
    }
  }
});
