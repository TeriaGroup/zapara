import React from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./App";
import "./styles.css";

if ("serviceWorker" in navigator) {
  void navigator.serviceWorker.register("/app/react-worker.js", { scope: "/app/" }).catch(() => undefined);
}

const saved = localStorage.getItem("zapara.theme");
const theme = saved === "light" || saved === "dark" ? saved : (matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark");
document.documentElement.dataset.theme = theme;

createRoot(document.getElementById("root")!).render(
  <React.StrictMode>
    <BrowserRouter basename="/app">
      <App />
    </BrowserRouter>
  </React.StrictMode>
);
