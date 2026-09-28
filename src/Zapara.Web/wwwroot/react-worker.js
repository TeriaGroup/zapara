// Replaced during bundling so each public build triggers a worker update.
const buildId = "b80b64641566eddc8414";
const commitCacheName = "zapara-react-commits-v1";
const stagePrefix = "zapara-react-stage-";
const legacyCacheName = "zapara-react-static-v1";
const commitKey = "/app/__react_static_commit__";
const shell = "/app/index.html";
async function committedCache() {
  const metadata = await (await caches.open(commitCacheName)).match(commitKey);
  if (metadata) {
    const reference = await metadata.json();
    if (typeof reference.cacheName === "string" && reference.cacheName.startsWith(stagePrefix)) return caches.open(reference.cacheName);
  }
  return caches.open(legacyCacheName);
}
async function savedShell() {
  const saved = await (await committedCache()).match(shell);
  return saved || await (await caches.open(legacyCacheName)).match(shell);
}
self.addEventListener("install", event => event.waitUntil((async () => {
  const stageName = stagePrefix + buildId + "-" + crypto.randomUUID();
  try {
    const response = await fetch(shell, { cache: "reload" });
    if (!response.ok) throw new Error("shell unavailable");
    const html = await response.clone().text();
    const assets = [...html.matchAll(/(?:src|href)="(\/app\/assets\/[^"<>]+)"/g)].map(match => match[1]);
    if (!assets.some(asset => asset.endsWith(".js"))) throw new Error("shell assets missing");
    const staged = await caches.open(stageName);
    await staged.addAll([...new Set([...assets, "/app/fonts/inter_regular.ttf", "/app/fonts/inter_medium.ttf", "/app/icons/zapara.svg"])]);
    await staged.put(shell, response);
    // One final Cache.put publishes a complete shell/resource set. Any preceding failure leaves last-good intact.
    await (await caches.open(commitCacheName)).put(commitKey, new Response(JSON.stringify({ cacheName: stageName }), { headers: { "Content-Type": "application/json" } }));
  } catch (error) {
    await caches.delete(stageName);
    throw error;
  }
})()));
self.addEventListener("activate", event => event.waitUntil(self.clients.claim()));
self.addEventListener("message", event => { if (event.data?.type === "APPLY_UPDATE") self.skipWaiting(); });
self.addEventListener("fetch", event => {
  const request = event.request, url = new URL(request.url);
  if (request.method !== "GET" || url.origin !== self.location.origin || !url.pathname.startsWith("/app/")) return;
  if (request.mode === "navigate") {
    event.respondWith((async () => {
      let response;
      try { response = await fetch(request); if (response.ok) return response; } catch { /* Use the committed public shell. */ }
      return await savedShell() || response || new Response("Приложение пока не сохранено для работы без сети.", { status: 503, headers: { "Content-Type": "text/plain;charset=utf-8" } });
    })());
    return;
  }
  if (!/^\/app\/(assets|fonts|icons)\//.test(url.pathname)) return;
  event.respondWith((async () => { const cache = await committedCache(); const saved = await cache.match(request); if (saved) return saved; const legacy = await (await caches.open(legacyCacheName)).match(request); if (legacy) return legacy; const response = await fetch(request); if (response.ok) await cache.put(request,response.clone()); return response; })());
});
self.addEventListener("push", event => event.waitUntil(self.registration.showNotification("Расписание военмех", { body: "Откройте приложение, чтобы проверить расписание и задания.", icon: "/app/icons/icon-512.png", tag: "zapara-reminder", data: { url: "/app/" } })));
self.addEventListener("notificationclick", event => { event.notification.close(); event.waitUntil((async () => { const windows=await self.clients.matchAll({type:"window",includeUncontrolled:true}); for(const client of windows){const url=new URL(client.url);if(url.origin===self.location.origin&&url.pathname.startsWith("/app/")){await client.focus();return;}}await self.clients.openWindow("/app/"); })()); });
