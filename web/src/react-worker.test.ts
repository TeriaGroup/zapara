import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
const source = await readFile(new URL("../react-worker.js",import.meta.url),"utf8");
function harness() {
    const storage = new Map<string,Map<string,Response>>();
    const deleted:string[]=[];
    let sequence=0;
    let fetcher:(path:string)=>Promise<Response>=async()=>new Response("unavailable",{status:503});
    let notification:any;
    const path=(input:any)=> typeof input==="string" ? input.replace("https://example.test","") : input.url.replace("https://example.test","");
    const fetch=async(input:any)=>fetcher(path(input));
    const caches={ async open(name:string){if(!storage.has(name))storage.set(name,new Map());const cache=storage.get(name)!;return {async match(input:any){return cache.get(path(input))?.clone();},async put(input:any,response:Response){cache.set(path(input),response.clone());},async addAll(urls:string[]){for(const url of urls){const response=await fetch(url);if(!response.ok)throw new Error("asset failed");cache.set(url,response.clone());}}};},async delete(name:string){deleted.push(name);return storage.delete(name);} };
    function worker(build:string){
        const handlers:Record<string,(event:any)=>void>={};
        runInNewContext(source.replace("__ZAPARA_REACT_BUILD__",build),{URL,Response,crypto:{randomUUID:()=>`attempt-${++sequence}`},self:{location:{origin:"https://example.test"},addEventListener:(name:string,handler:any)=>{handlers[name]=handler;},registration:{showNotification:async(title:string,options:any)=>{notification={title,options};}},clients:{claim:async()=>undefined}},caches,fetch});
        return {handlers,install(){let promise:Promise<void>|undefined;handlers.install({waitUntil:(value:Promise<void>)=>{promise=value;}});return promise!;},navigate(url="https://example.test/app/schedule"){let promise:Promise<Response>|undefined;handlers.fetch({request:{url,method:"GET",mode:"navigate"},respondWith:(value:Promise<Response>)=>{promise=value;}});return promise!;}};
    }
    return {storage,deleted,caches,worker,setNetwork(handler:(path:string)=>Promise<Response>){fetcher=handler;},get notification(){return notification;}};
}
const html=(version:string)=>`<html><script src="/app/assets/${version}.js"></script><link href="/app/assets/${version}.css"></html>`;
function completeNetwork(version:string){return async(path:string)=>new Response(path==="/app/index.html"?html(version):`resource ${path}`);}
test("React worker never intercepts API/private requests and never reads a push payload",async()=>{
    const app=harness(), worker=app.worker("one");
    for(const url of ["https://example.test/web-api/sync/changes","https://example.test/api/v1/groups","https://other.test/app/assets/a.js","https://example.test/app/private-profile.json"]){let intercepted=false;worker.handlers.fetch({request:{url,method:"GET",mode:"cors"},respondWith:()=>{intercepted=true;}});assert.equal(intercepted,false,url);}
    let promise:Promise<void>|undefined;worker.handlers.push({data:{json:()=>{throw new Error("private payload must not be read");}},waitUntil:(value:Promise<void>)=>{promise=value;}});await promise;
    assert.equal(app.notification.title,"Расписание военмех");assert.equal(app.notification.options.body,"Откройте приложение, чтобы проверить расписание и задания.");assert.equal(app.notification.options.data.url,"/app/");
});
test("failed asset staging preserves the previous committed shell and all unrelated caches",async()=>{
    const app=harness();await (await app.caches.open("unrelated-project")).put("/private",new Response("keep"));
    app.setNetwork(completeNetwork("old"));const active=app.worker("old-build");await active.install();
    const before=await (await app.caches.open("zapara-react-commits-v1")).match("/app/__react_static_commit__");const reference=await before!.json();
    app.setNetwork(async path=>path==="/app/index.html"?new Response(html("new")):path.endsWith("new.css")?new Response("asset missing",{status:500}):new Response("staged partial asset"));
    await assert.rejects(app.worker("new-build").install(),/asset failed/);
    const after=await (await app.caches.open("zapara-react-commits-v1")).match("/app/__react_static_commit__");assert.deepEqual(await after!.json(),reference);
    app.setNetwork(async()=>new Response("server error",{status:503}));assert.equal(await (await active.navigate()).text(),html("old"));
    assert.equal(await (await (await app.caches.open("unrelated-project")).match("/private"))!.text(),"keep");
    assert.equal(app.deleted.length,1);assert.match(app.deleted[0],/^zapara-react-stage-new-build-/);assert.ok(app.storage.has(reference.cacheName));
});
test("successful commit exposes one complete new resource set and offline navigation uses its shell",async()=>{
    const app=harness();app.setNetwork(completeNetwork("old"));const active=app.worker("old-build");await active.install();
    app.setNetwork(completeNetwork("new"));await app.worker("new-build").install();
    const metadata=await (await app.caches.open("zapara-react-commits-v1")).match("/app/__react_static_commit__");const reference=await metadata!.json();const committed=await app.caches.open(reference.cacheName);
    assert.equal(await (await committed.match("/app/index.html"))!.text(),html("new"));assert.ok(await committed.match("/app/assets/new.js"));assert.ok(await committed.match("/app/assets/new.css"));
    app.setNetwork(async()=>{throw new Error("offline");});assert.equal(await (await active.navigate()).text(),html("new"));assert.equal(app.deleted.length,0);
});
test("first failed upgrade can fall back to an existing legacy public shell",async()=>{
    const app=harness();await (await app.caches.open("zapara-react-static-v1")).put("/app/index.html",new Response(html("legacy")));
    app.setNetwork(async()=>new Response("unavailable",{status:500}));const worker=app.worker("new-build");await assert.rejects(worker.install(),/shell unavailable/);
    assert.equal(await (await worker.navigate()).text(),html("legacy"));assert.equal(app.storage.has("zapara-react-static-v1"),true);
});
