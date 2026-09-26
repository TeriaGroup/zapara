import assert from "node:assert/strict";
import { test } from "node:test";
import { validateFormAnswers, validateFormQuestions } from "./forms.ts";
import type { FormQuestion } from "./types";
const question:FormQuestion={questionId:"a",title:"Выберите",kind:"singleChoice",required:true,options:["А","Б"]};
test("form publication rejects duplicate choices and invalid future deadlines",()=>{assert.equal(validateFormQuestions("Анкета",[question],""),null);assert.ok(validateFormQuestions("Анкета",[{...question,options:["А","А"]}],""));assert.ok(validateFormQuestions("Анкета",[question],"2000-01-01T10:00"));assert.ok(validateFormQuestions("Анкета",[question,question],""));});
test("responses enforce required, known, distinct and single-choice answers",()=>{assert.ok(validateFormAnswers([question],[]));assert.ok(validateFormAnswers([question],[{questionId:"a",text:null,choices:["А","Б"]}]));assert.ok(validateFormAnswers([question],[{questionId:"a",text:null,choices:["В"]}]));assert.equal(validateFormAnswers([question],[{questionId:"a",text:null,choices:["А"]}]),null);assert.ok(validateFormAnswers([question],[{questionId:"unknown",text:null,choices:["А"]}]));});
