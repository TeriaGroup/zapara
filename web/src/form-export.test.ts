import assert from "node:assert/strict";
import { test } from "node:test";
import { formResponsesCsv } from "./form-export.ts";
test("form export preserves anonymous answers, quotes newlines and neutralizes spreadsheet formula cells",()=>{const csv=formResponsesCsv([{questionId:"q",title:"Вопрос",kind:"shortText",required:true,options:[]}],[{respondentId:null,updatedAt:"2026-09-26T12:00:00Z",answers:[{questionId:"q",text:'=SUM(A1:A2)\n"текст"',choices:[]}]}]);assert.match(csv,/Анонимный ответ/);assert.match(csv,/'=SUM/);assert.match(csv,/""текст""/);assert.equal(csv.startsWith("\ufeff"),true);});
test("formula prefixes hidden by whitespace and controls are exported as text",()=>{
    const questions=[{questionId:"q",title:"Вопрос",kind:"shortText" as const,required:true,options:[]}];
    for(const prefix of [" ","\t","\r","\n","\r\n \t","\ufeff","\u00a0","\u0000"]){for(const operator of ["=","+","-","@"]){const text=prefix+operator+"SUM(A1:A2)";const csv=formResponsesCsv(questions,[{respondentId:null,updatedAt:"time",answers:[{questionId:"q",text,choices:[]}]}]);assert.ok(csv.includes('"\''+text+'"'),JSON.stringify(text));}}
    const ordinary=formResponsesCsv(questions,[{respondentId:null,updatedAt:"time",answers:[{questionId:"q",text:" Обычный ответ",choices:[]}]}]);assert.ok(ordinary.includes('" Обычный ответ"'));assert.equal(ordinary.includes("' Обычный ответ"),false);
});
