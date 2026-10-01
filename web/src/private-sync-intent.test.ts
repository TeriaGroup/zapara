import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";

const source = ts.transpileModule(await readFile(new URL("./private-sync.ts", import.meta.url), "utf8"), {
  compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022 },
}).outputText;
const serverSettings = { selectedGroupId: "group-a", parityInvert: true, notifyTime1: "19:00", notifyTime2: "07:00", strictness: 80, alwaysShow: true };
const record = { entityType: "settings", entityId: "00000000-0000-0000-0000-000000000001", revision: 4, tombstone: false,
  changedAt: "2026-09-29T12:00:00Z", value: serverSettings };
const settle = () => new Promise(resolve => setImmediate(resolve));

test("deleting queued task while another mutation waits prevents copied queue from sending it",async()=>{
  const value={subjectRaw:"Предмет",subjectKey:"предмет",text:"Задание",targetNthOccurrence:1,createdAtUtc:"2026-10-01T00:00:00Z",legacyCreatedLocalDate:null};
  const items=["other","target"].map(id=>({id,subject:"Предмет",text:"Задание",created:value.createdAtUtc,done:false}));
  const queue=[{opId:"other-op",id:"other",type:"homework",revision:0,value},{opId:"target-op",id:"target",type:"homework",revision:0,value},{opId:"target-completion",id:"target",type:"completion",revision:0,value:{done:false}}];
  const memory=new Map([["zapara.private-homework.owner",JSON.stringify({owner:"owner",items,pending:queue,records:[]})]]);const effects:any[]=[];const calls:string[]=[];let release:(value:unknown)=>void=()=>{};
  const react={useState:(initial:any)=>[typeof initial==="function"?initial():initial,()=>{}],useRef:(value:any)=>({current:value}),useEffect:(effect:any)=>effects.push(effect)};
  const api={beginSyncSnapshot:async()=>({syncEpoch:"epoch",manifestId:"manifest",highWater:0}),syncSnapshotPage:async()=>({items:[],hasMore:false,nextAfterOrdinal:0}),mutatePrivate:async(...args:any[])=>{calls.push(args[3]);return new Promise(resolve=>{release=resolve;});}};
  const context={exports:{} as any,require:(id:string)=>id==="react"?react:id==="./api.ts"?api:{canonicalUtc:String,syncSubjectKey:String,normalizeIntersectionStrictness:(n:number)=>n},localStorage:{getItem:(key:string)=>memory.get(key)||null,setItem:(key:string,value:string)=>memory.set(key,value)},window:{setInterval:()=>1,clearInterval:()=>{},addEventListener:()=>{},removeEventListener:()=>{}},document:{hidden:false,addEventListener:()=>{},removeEventListener:()=>{}},crypto:{randomUUID:()=>"new-op"}};
  runInNewContext(source,context);const hook=context.exports.usePrivateHomework("owner");effects[0]();effects[1]();await settle();assert.deepEqual(calls,["other"]);hook.remove("target");release({status:200,serverRecord:{entityId:"other",entityType:"homework",revision:1,tombstone:false,value}});await settle();assert.deepEqual(calls,["other"]);assert.equal(JSON.parse(memory.get("zapara.private-homework.owner")!).items.some((item:any)=>item.id==="target"),false);
});

test("pre-snapshot settings change persists as a field patch and rebases onto server settings", async () => {
  for (const cached of [false, true]) {
  const memory = new Map<string, string>(cached ? [["zapara.private-homework.owner", JSON.stringify({ owner: "owner", items: [], pending: [], records: [{ ...record, revision: 1, value: { ...serverSettings, notifyTime1: "17:00" } }] })]] : []);
  const effects: (() => void | (() => void))[] = [];
  const mutations: any[] = [];
  const react = { useState: (initial: any) => [typeof initial === "function" ? initial() : initial, () => {}],
    useRef: (value: any) => ({ current: value }), useEffect: (effect: any) => effects.push(effect) };
  const api = { beginSyncSnapshot: async () => ({ syncEpoch: "epoch", manifestId: "manifest", highWater: 2 }),
    syncSnapshotPage: async () => ({ items: [{ ordinal: 1, record }], hasMore: false, nextAfterOrdinal: 1 }),
    mutatePrivate: async (...args: any[]) => { mutations.push(args); return { status: 200, serverRecord: { ...record, revision: 5, value: args[5] } }; } };
  const context = { exports: {} as any, require: (id: string) => id === "react" ? react : id === "./api.ts" ? api : { canonicalUtc: String, syncSubjectKey: String, normalizeIntersectionStrictness: (n: number) => n },
    localStorage: { getItem: (key: string) => memory.get(key) || null, setItem: (key: string, value: string) => memory.set(key, value) },
    window: { setInterval: () => 1, clearInterval: () => {}, addEventListener: () => {}, removeEventListener: () => {} },
    document: { hidden: false, addEventListener: () => {}, removeEventListener: () => {} }, crypto: { randomUUID: () => "op" } };
  runInNewContext(source, context);
  const hook = context.exports.usePrivateHomework("owner");
  assert.equal(hook.saveSettings({ selectedGroupId: "group-b" }), true);
  const before = JSON.parse(memory.get("zapara.private-homework.owner")!);
  assert.deepEqual(JSON.parse(JSON.stringify(before.settingsIntent)), { selectedGroupId: "group-b" });
  assert.deepEqual(before.pending, []);
  effects[0]();
  effects[1]();
  await settle();
  assert.equal(mutations.length, 1);
  assert.equal(mutations[0][4], 4);
  assert.deepEqual(JSON.parse(JSON.stringify(mutations[0][5])), { ...serverSettings, selectedGroupId: "group-b" });
  }
});

test("settings from the previous account are hidden before the next owner profile loads", () => {
  const memory = new Map<string, string>([["zapara.private-homework.first", JSON.stringify({ owner: "first", items: [], pending: [], records: [record] })]]);
  const state: any[] = [];
  let cursor = 0;
  const react = { useState: (initial: any) => { const at = cursor++; if (!(at in state)) state[at] = typeof initial === "function" ? initial() : initial; return [state[at], (value: any) => { state[at] = value; }]; },
    useRef: (value: any) => { const at = cursor++; if (!(at in state)) state[at] = { current: value }; return state[at]; }, useEffect: () => {} };
  const context = { exports: {} as any, require: (id: string) => id === "react" ? react : { canonicalUtc: String, syncSubjectKey: String, normalizeIntersectionStrictness: (n: number) => n },
    localStorage: { getItem: (key: string) => memory.get(key) || null, setItem: () => {} } };
  runInNewContext(source, context);
  cursor = 0;
  assert.equal(context.exports.usePrivateHomework("first").settings.selectedGroupId, "group-a");
  cursor = 0;
  assert.equal(context.exports.usePrivateHomework("second").settings, undefined);
});
