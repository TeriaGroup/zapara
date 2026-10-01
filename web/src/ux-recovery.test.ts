import test from "node:test";
import assert from "node:assert/strict";
import { sendSupportDraft } from "./support.ts";
import { requiredFormProgress } from "./forms.ts";
import { friendGroupChoice } from "./friend-group-choice.ts";
import { reminderTimePatch } from "./reminder-settings.ts";
test("support preserves text/files after error and clears only acknowledged submission", async () => {
  let draft = { text: "Описание", files: ["a.png"] };
  const clear = () => { draft = {text:"",files:[]}; };
  assert.equal(await sendSupportDraft(async () => { throw Error("offline"); },clear),false);
  assert.deepEqual(draft,{text:"Описание",files:["a.png"]});
  assert.equal(await sendSupportDraft(async () => false,clear),false);
  assert.equal(await sendSupportDraft(async () => true,clear),true);
  assert.deepEqual(draft,{text:"",files:[]});
});
test("form progress targets missing required answers and ignores optional questions", () => {
  const questions = [{questionId:"a",title:"Имя",kind:"shortText" as const,required:true,options:[]},{questionId:"b",title:"Выбор",kind:"singleChoice" as const,required:true,options:["Да"]},{questionId:"c",title:"Прочее",kind:"longText" as const,required:false,options:[]}];
  const state = requiredFormProgress(questions,[{questionId:"a",text:"  ",choices:[]},{questionId:"b",text:null,choices:["Да"]}]);
  assert.equal(state.answered,1); assert.equal(state.total,2); assert.equal(state.firstMissing?.questionId,"a");
});
test("known friend catalog rejects typos; empty catalog keeps explicit manual path", () => {
  assert.ok(friendGroupChoice("oops",[{id:"1",name:"А123"}]).error);
  assert.deepEqual(friendGroupChoice("а123",[{id:"1",name:"А123"}]),{name:"А123",manual:false});
  assert.deepEqual(friendGroupChoice(" А123 ",[]),{name:"А123",manual:true});
});
test("partial reminder time cannot produce a settings patch and disabled slots stay disabled", () => {
  const saved = {enabled:true,morning:false,evening:true,morningAt:"07:00",eveningAt:"20:00"};
  assert.equal(reminderTimePatch(saved,{morningAt:"07:00",eveningAt:"20:"}),null);
  assert.deepEqual(reminderTimePatch(saved,{morningAt:"08:00",eveningAt:"21:30"}),{notifyTime1:"21:30",notifyTime2:null});
  assert.equal(saved.eveningAt,"20:00");
});
