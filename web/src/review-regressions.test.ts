import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import * as rules from "./ux300.ts";
import * as forms from "./forms.ts";
import * as revocation from "./draft-revocation.ts";
import { formResponsesCsv } from "./form-export.ts";
import { browseHomework } from "./homework-browse.ts";
import { personalHomeworkDue } from "./planner.ts";
import * as teachers from "./teachers.ts";
import * as chatUi from "./chat-ui.ts";
import * as navigation from "./ux-navigation.ts";
import * as subgroups from "./subgroups.ts";
import * as routing from "./campus-routing.ts";
import * as routeMemory from "./route-memory.ts";
import * as homeworkExport from "./homework-export.ts";
import * as studyPlanning from "./study-planning.ts";
import * as parity from "./parity.ts";
import * as planner from "./planner.ts";
import * as scheduleText from "./schedule-text.ts";
import * as personalBatch from "./personal-homework-batch.ts";
import * as publication from "./homework-publication-batch.ts";
import * as postpone from "./homework-postpone.ts";
import * as overviews from "./study-overviews.ts";
import * as summary from "./summary.ts";
import * as discovery from './study-discovery.ts';
import * as obligations from './group-obligations.ts';
import * as chatInbox from './chatInbox.ts';
import * as visibleRefresh from './visible-refresh.ts';

test("homework batch siblings retain distinct reconciliation identities across sync updates", async()=>{
  const source=await readFile(new URL("./pages.tsx",import.meta.url),"utf8");
  const keys=["PersonalHomeworkBatch","HomeworkPostpone","HomeworkPublication"].map(name=>{
    const expression=source.match(new RegExp(`<${name} key=\\{(JSON\\.stringify\\(\\[[^\\]]*\\]\\))\\}`))?.[1];
    assert.ok(expression,`${name} has a scoped identity`);
    return runInNewContext(expression,{copyOwnerKey:"owner",app:{session:{familyId:"family"},groupId:"group"},communityId:"community"});
  });
  assert.equal(new Set(keys).size,keys.length,"sibling keys must not collide and retain ghost batch buttons");
});

function hooks() {
  const slots:any[]=[]; let cursor=0, mounted=true, lateWrites=0;
  const pending:(()=>void)[]=[];
  const react={
    useState(initial:any){const i=cursor++;if(!(i in slots))slots[i]=typeof initial==="function"?initial():initial;return[slots[i],(next:any)=>{if(!mounted)lateWrites++;slots[i]=typeof next==="function"?next(slots[i]):next;}];},
    useRef(initial:any){const i=cursor++;return slots[i]??(slots[i]={current:initial});},
    useMemo(fn:any,deps:any[]){const i=cursor++;if(!slots[i]||deps.some((value,index)=>!Object.is(value,slots[i].deps[index])))slots[i]={deps,value:fn()};return slots[i].value;},
    useEffect(fn:any,deps?:any[]){const i=cursor++;if(!slots[i]||!deps||deps.some((value,index)=>!Object.is(value,slots[i].deps?.[index]))){const old=slots[i];slots[i]={deps,cleanup:old?.cleanup};pending.push(()=>{old?.cleanup?.();slots[i].cleanup=fn();});}},
    useLayoutEffect(fn:any,deps?:any[]){react.useEffect(fn,deps);},
  };
  return {react,render<T>(fn:()=>T){cursor=0;const tree=fn();while(pending.length)pending.shift()!();return tree;},unmount(){mounted=false;slots.forEach(slot=>slot?.cleanup?.());},lateWrites:()=>lateWrites};
}
const nodes=(tree:any):any[]=>Array.isArray(tree)?tree.flatMap(nodes):tree&&typeof tree==="object"?[tree,...nodes(tree.props?.children)]:[];
const text=(tree:any):string=>Array.isArray(tree)?tree.map(text).join(" "):tree&&typeof tree==="object"?text(tree.props?.children):tree==null?"":String(tree);
const button=(tree:any,label:string)=>nodes(tree).find(node=>node.type==="button"&&text(node)===label);
const settle=()=>new Promise(resolve=>setImmediate(resolve));
async function load(file:string, names:string[], modules:any, globals:any={}){
  const source=await readFile(new URL(file,import.meta.url),"utf8");
  const code=ts.transpileModule(source+`\nexport {${names.join(",")}};`,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText;
  const jsx=(type:any,props:any)=>({type,props});
  const context={exports:{} as any,URLSearchParams,require:(name:string)=>name==="react/jsx-runtime"?{jsx,jsxs:jsx,Fragment:"fragment"}:modules[name]??{},...globals};
  runInNewContext(code,context);return context.exports;
}

for(const pageToDelay of [0,1]) for(const change of ["unmount","owner","family","community","form","permission","auth","lease","none"]){
  test(`actual CSV ignores pending page ${pageToDelay} after ${change}`,async()=>{
    const h=hooks();let epoch=0,resolvePage!:(value:any)=>void,clicks=0,blobs=0,calls=0;
    const app={session:{user:{userId:"owner"},familyId:"family"}};
    const form:any={formId:"form",topicId:"topic",title:"Анкета",description:"",canViewResponses:true,canRespond:false,ownResponse:null,questions:[],responseCount:0};
    let communityId="community";
    const memory=new Map<string,string>();const storage={getItem:(key:string)=>memory.get(key)??null,setItem:(key:string,value:string)=>memory.set(key,value),removeItem:(key:string)=>memory.delete(key)};
    const modules:any={react:h.react,"./api":{authGeneration:()=>epoch,groupFormResponses:async()=>{const page=calls++;if(page===pageToDelay)return new Promise(resolve=>{resolvePage=resolve;});return{responses:[],nextCursor:"last"};}},"./store":{useApp:()=>app},"./draft-revocation":revocation,"./draft-store":{draftKey:()=>"draft",useStoredDraft:()=>[[],()=>{},()=>{},()=>{},()=>{}]},"./forms":forms,"./ux300":rules,"./form-export":{formResponsesCsv}};
    const out=await load("./group-panels.tsx",["FormCard"],modules,{sessionStorage:storage,Blob:class{constructor(){blobs++;}},URL:{createObjectURL:()=>"blob:test",revokeObjectURL:()=>{}},document:{createElement:()=>({click:()=>clicks++})},window:{setTimeout:()=>1}});
    const render=()=>h.render(()=>out.FormCard({communityId,form,writable:false,onUpdate:()=>{},onError:()=>{throw Error("late error");}}));
    button(render(),"Выгрузить ответы CSV").props.onClick();await settle();assert.equal(calls,pageToDelay+1);
    if(change==="unmount")h.unmount();
    else {if(change==="owner")app.session.user.userId="other";if(change==="family")app.session.familyId="other";if(change==="community")communityId="other";if(change==="form")form.formId="other";if(change==="permission")form.canViewResponses=false;if(change==="auth")epoch++;if(change==="lease")revocation.purgeGroupDrafts(storage,{owner:"owner",community:"community",topic:"topic"});render();}
    resolvePage({responses:[],nextCursor:null});await settle();assert.equal(clicks,change==="none"?1:0);assert.equal(blobs,change==="none"?1:0);assert.equal(h.lateWrites(),0);
  });
}

test("inferred personal calendar day remains today until the next midnight while shared deadlines stay exact",()=>{
  const lesson:any={dayOfWeek:5,timeStart:"09:00",timeEnd:"10:30",subjectRaw:"Математика",parity:0};
  const task:any={id:"today",subject:"Математика",text:"Решить",done:false,created:new Date(2026,9,1,12).toISOString()};
  const inferred=personalHomeworkDue(task,[lesson],{start:"2026-09-01",weekCount:2,title:"",timeZone:"Europe/Moscow"},false)!;
  assert.equal(inferred.getDate(),2);
  for(const hour of [0,12,23]){
    const now=new Date(2026,9,2,hour,1);
    assert.equal(rules.dueBucket(inferred.toISOString(),now,"date"),"today");
    assert.equal(rules.dueBucket(new Date(2026,9,1).toISOString(),now,"date"),"overdue");
    assert.equal(rules.dueBucket(new Date(2026,9,3).toISOString(),now,"date"),"soon");
    const rows=browseHomework([{...task,deadlineAt:inferred.toISOString(),deadlinePrecision:"date"}],[],{subject:null,query:"",status:"all",target:null,deadline:"today",now});assert.equal(rows.local.length,1);
  }
  const now=new Date(2026,9,2,12);assert.equal(rules.dueBucket(new Date(2026,9,2,11).toISOString(),now),"overdue");assert.equal(rules.dueBucket(new Date(2026,9,2,13).toISOString(),now),"today");
});

test("actual teacher detail follows Back and Forward and discards the previous delayed request",async()=>{
  const h=hooks();const location={pathname:"/teachers",search:""};let calls=0;
  const responses:((value:any)=>void)[]=[];
  const catalog=[{id:"teacher",name:"Иванов Иван",shortName:"Иванов И.",kafedra:"Кафедра"}];
  const app={groupId:"group",catalog:{groups:[]},lessons:[],subgroups:{},invert:false,session:{}};
  const modules:any={react:h.react,"react-router-dom":{useLocation:()=>location,useNavigate:()=>((next:any)=>{location.search=next.search?`?${next.search}`:"";}),Link:()=>null},"./store":{useApp:()=>app},"./api":{readCache:()=>({lessons:{}}),loadTeachers:async()=>({lecturers:catalog}),loadTeacher:()=>{calls++;return new Promise(resolve=>responses.push(resolve));}},"./teachers":teachers,"./subgroups":subgroups,"./ux-navigation":navigation,"./ux300":rules,"./ux300-controls":{useBrowseValue:(key:string,initial:any)=>h.react.useState(key==="teacher-mine"?false:initial)},"./next-workflows":{matchesWords:rules.noteSearch}};
  const out=await load("./pages.tsx",["TeachersContent"],modules);
  const render=()=>h.render(()=>out.TeachersContent());
  render();await settle();let tree=render();
  const card=nodes(tree).find(node=>node.type==="button"&&text(node).includes("Иванов Иван"));assert.ok(card);card.props.onClick();render();assert.equal(calls,1);
  location.search="";render();tree=render();assert.equal(button(tree,"Назад"),undefined);
  responses[0]({lessons:[{dayOfWeek:1,timeStart:"08:00",parity:0,subjectRaw:"Старый ответ"}]});await settle();tree=render();assert.equal(text(tree).includes("Старый ответ"),false);assert.equal(button(tree,"Назад"),undefined);
  location.search="?teacher=teacher";render();assert.equal(calls,2);responses[1]({lessons:[]});await settle();tree=render();assert.ok(button(tree,"Назад"));
  location.search="?teacher=unknown";render();tree=render();assert.equal(button(tree,"Назад"),undefined);assert.match(text(tree),/Преподаватель по ссылке не найден/);
});

test("actual updater protects support-only draft, preserves cancel and blocks reload during a send",async()=>{
  const h=hooks();let reloads=0,confirmations=0,accept=false;
  const modules={react:h.react,"./homework-publication-batch":publication,"./store":{useApp:()=>({privateHomework:{pending:[]}})},"./personal-composer-context":{usePersonalDrafts:()=>[]},"./homework-draft-context":{useHomeworkDraft:()=>({controller:{dirty:false,busy:false}})}};
  const out=await load("./update-settings-view.tsx",[],modules,{fetch:async()=>({ok:true}),Event:class{},window:{confirm:()=>{confirmations++;return accept;},dispatchEvent:()=>{},location:{reload:()=>reloads++}}});
  let supportState={dirty:true,busy:false};const render=()=>h.render(()=>out.UpdateSettings({supportState}));
  button(render(),"Проверить обновление").props.onClick();await settle();button(render(),"Перезагрузить приложение").props.onClick();assert.equal(confirmations,1);assert.equal(reloads,0);assert.equal(supportState.dirty,true);
  supportState={dirty:true,busy:true};let tree=render();assert.equal(button(tree,"Перезагрузить приложение").props.disabled,true);button(tree,"Перезагрузить приложение").props.onClick();assert.equal(reloads,0);
  supportState={dirty:true,busy:false};accept=true;button(render(),"Перезагрузить приложение").props.onClick();assert.equal(reloads,1);
  supportState={dirty:false,busy:false};button(render(),"Перезагрузить приложение").props.onClick();assert.equal(reloads,2);assert.equal(confirmations,2);
});

test("actual support publishes text, attachment and selected reply dirty/busy state to the updater",async()=>{
  const h=hooks();let current:any;
  const app={session:{authenticated:true,user:{userId:"owner"},familyId:"family"}};
  const out=await load("./pages.tsx",["SupportContent"],{react:h.react,"./store":{useApp:()=>app},"./api":{supportList:async()=>[{id:"thread",subject:"Тема",messages:[]}]},"./ux300":rules},{window:{confirm:()=>true}});
  const onState=(state:any)=>{current=state;};const render=()=>h.render(()=>out.SupportContent({onState}));
  render();await settle();let tree=render();assert.equal(current.dirty,false);
  nodes(tree).find(node=>node.type==="input"&&node.props.maxLength===120).props.onChange({target:{value:"Помогите"}});tree=render();assert.equal(current.dirty,true);
  button(tree,"Очистить черновик обращения").props.onClick();tree=render();assert.equal(current.dirty,false);
  const attach=nodes(tree).find(node=>typeof node.props?.onPhotos==="function");attach.props.onPhotos([{name:"photo.jpg"}]);tree=render();assert.equal(current.dirty,true);
  button(tree,"Очистить черновик обращения").props.onClick();tree=render();assert.equal(nodes(tree).some(node=>node.props?.["aria-label"]==="Лог"||typeof node.props?.onLogs==="function"),false);assert.equal(current.dirty,false);
  nodes(tree).find(node=>node.type==="button"&&text(node).includes("сообщений:")).props.onClick();tree=render();const reply=nodes(tree).find(node=>typeof node.props?.onSend==="function");reply.props.onState(true,true);render();assert.equal(current.dirty,true);assert.equal(current.busy,true);
  h.unmount();assert.equal(current.dirty,false);assert.equal(current.busy,false);
});

test("actual route prefill drops the previous target when the new lesson room is unknown",async()=>{
  const h=hooks();const source=await readFile(new URL("../../src/Vograph.Desktop/Assets/maps/campus-graph.json",import.meta.url));
  const out=await load("./campus-route-view.tsx",[],{react:h.react,"./campus-routing":routing,"./route-memory":routeMemory,"./ux300-controls":{useClock:()=>new Date(),useBrowseValue:(_key:string,initial:any)=>h.react.useState(initial)}},{URL,TextDecoder,AbortController,crypto:{},window:{location:{origin:"https://local.test"}},fetch:async()=>({ok:true,arrayBuffer:async()=>source.buffer.slice(source.byteOffset,source.byteOffset+source.byteLength)})});
  let classroom="320*;";const render=()=>h.render(()=>out.CampusRouteView({asset:{url:"/api/v1/maps/assets/campus-graph.json",bytes:source.byteLength,sha256:""},plan:null,plans:[],classroom,onPlan:()=>{},onMark:()=>{},onRoute:()=>{}}));
  render();await settle();render();let tree=render();assert.match(text(tree),/Куда:.*320.*УЛК/);
  classroom="unknown";render();tree=render();assert.match(text(tree),/Куда:.*Не выбрано/);
});

test("actual homework export previews before copy and aborts a pending private calendar after unmount",async()=>{
  const h=hooks();let copied="",clicks=0,resolveCalendar!:(value:any)=>void;
  const out=await load("./homework-export-view.tsx",[],{react:h.react,"./api":{authGeneration:()=>0},"./homework-export":homeworkExport,"./calendar-export":{createAllDayCalendarExport:()=>new Promise(resolve=>{resolveCalendar=resolve;})}},{navigator:{clipboard:{writeText:async(value:string)=>{copied=value;}}},URL:{createObjectURL:()=>"blob:private",revokeObjectURL:()=>{}},Blob,document:{createElement:()=>({click:()=>clicks++})},window:{setTimeout:()=>1}});
  const items=[{id:"one",subject:"Математика",text:"Решить",created:"",done:false,deadlineAt:new Date(2026,9,5).toISOString(),deadlinePrecision:"date"}];
  const render=()=>h.render(()=>out.HomeworkExportTools({items,groupId:"group",groupName:"Группа"}));
  button(render(),"Текст выбранной домашки").props.onClick();let tree=render();assert.equal(copied,"");assert.equal(clicks,0);assert.match(nodes(tree).find(node=>node.type==="textarea").props.value,/Решить/);
  button(tree,"Скопировать план").props.onClick();await settle();assert.match(copied,/Решить/);
  button(render(),"Вернуться к заданиям").props.onClick();button(render(),"Календарь личной домашки").props.onClick();button(render(),"Скачать .ics").props.onClick();h.unmount();resolveCalendar({content:"private",eventCount:1,skippedCount:0});await settle();assert.equal(clicks,0);assert.equal(h.lateWrites(),0);
});

test("actual common-free panel requires a selected compatible group and never declares an empty day free",async()=>{
  const h=hooks();let date=new Date(2026,9,2);const lesson=(start:string,end:string)=>({dayOfWeek:5,timeStart:start,timeEnd:end,parity:0});
  const friend={groupName:"Друг",enabled:true,lessons:[lesson("09:00","10:30"),lesson("12:00","13:00")] as any,members:""};
  const out=await load("./common-free-view.tsx",[],{react:h.react,"./study-planning":studyPlanning,"./parity":parity,"./planner":planner,"./ux300-controls":{useBrowseValue:(_key:string,initial:any)=>h.react.useState(initial)}});
  const render=()=>h.render(()=>out.CommonFreeTime({mine:[lesson("09:00","10:00"),lesson("12:00","13:00")],friends:[friend],period:{start:"2026-09-01",weekCount:2},date,onDate:(next:Date)=>{date=next;},invert:false,known:true}));
  let tree=render();assert.match(text(tree),/Выберите одну/);nodes(tree).find(node=>node.type==="select").props.onChange({target:{value:"Друг"}});tree=render();assert.match(text(tree),/10:30.*12:00.*90/);
  friend.lessons=[];assert.match(text(render()),/Свободный день автоматически не предполагается/);
  friend.lessons=null;assert.match(text(render()),/Нет совместимых/);
});

test("actual route picker pins places and successful route history can be repeated and cleared",async()=>{
  const h=hooks();const marks:any[]=[];const source=await readFile(new URL("../../src/Vograph.Desktop/Assets/maps/campus-graph.json",import.meta.url));
  const out=await load("./campus-route-view.tsx",[],{react:h.react,"./campus-routing":routing,"./route-memory":routeMemory,"./ux300-controls":{useClock:()=>new Date(),useBrowseValue:(_key:string,initial:any)=>h.react.useState(initial)}},{URL,TextDecoder,AbortController,crypto:{},window:{location:{origin:"https://local.test"}},fetch:async()=>({ok:true,arrayBuffer:async()=>source.buffer.slice(source.byteOffset,source.byteOffset+source.byteLength)})});
  const render=()=>h.render(()=>out.CampusRouteView({asset:{url:"/api/v1/maps/assets/campus-graph.json",bytes:source.byteLength,sha256:""},plan:{building:"ГК",floor:1},plans:[],classroom:"",onPlan:()=>{},onMark:(node:any,show=false)=>marks.push({node,show}),onRoute:()=>{}}));
  render();await settle();render();let tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Откуда:")).props.onClick();tree=render();nodes(tree).find(node=>node.props?.label==="Найти аудиторию или вход").props.onChange("101 ГК");tree=render();
  const pin=nodes(tree).find(node=>node.type==="button"&&node.props["aria-label"]?.startsWith("Закрепить 101 ·"));assert.ok(pin);pin.props.onClick();tree=render();assert.ok(nodes(tree).find(node=>node.props?.["aria-label"]?.startsWith("Открепить 101 ·")));
  nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("101 ·")).props.onClick();tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Куда:")).props.onClick();tree=render();nodes(tree).find(node=>node.props?.label==="Найти аудиторию или вход").props.onChange("102 ГК");tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("102 ·")).props.onClick();render();tree=render();assert.match(text(tree),/История маршрутов/);assert.equal(marks.at(-1).show,false);button(tree,"Показать назначение").props.onClick();assert.equal(marks.at(-1).show,true);
  const repeat=nodes(tree).find(node=>node.type==="button"&&text(node).includes("101 ·")&&text(node).includes("→"));assert.ok(repeat);repeat.props.onClick();tree=render();button(tree,"Очистить историю маршрутов").props.onClick();tree=render();assert.equal(button(tree,"Очистить историю маршрутов"),undefined);
});

test("actual bulk completion requires selection and keeps the partial result visible after the active list changes",async()=>{
  const h=hooks();let reject=true;const writes:string[]=[];
  const app:any={session:{authenticated:true,user:{userId:"owner"},familyId:"family"},groupId:"group",homework:[{id:"a",subject:"Мат",text:"Первое",done:false},{id:"b",subject:"Ист",text:"Второе",done:false}],saveHomework:(item:any)=>{if(item.id==="b"&&reject)throw Error("full");writes.push(item.id);app.homework=app.homework.map((row:any)=>row.id===item.id?item:row);}};
  const out=await load("./personal-homework-batch-view.tsx",[],{react:h.react,"./store":{useApp:()=>app},"./api":{authGeneration:()=>0},"./personal-homework-batch":personalBatch},{window:{setTimeout:(fn:()=>void)=>{fn();return 1;}}});
  const render=()=>h.render(()=>out.PersonalHomeworkBatch({items:app.homework.filter((row:any)=>!row.done)}));
  button(render(),"Завершить несколько личных заданий").props.onClick();let tree=render();assert.equal(writes.length,0);assert.equal(nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Подтвердить готовность")).props.disabled,true);
  button(tree,"Выбрать все показанные").props.onClick();tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Подтвердить готовность")).props.onClick();await settle();tree=render();assert.deepEqual(writes,["a"]);assert.match(text(tree),/Не сохранилось:.*1/);assert.match(text(tree),/Подтверждение сервера/);
  reject=false;button(tree,"Повторить только неудавшиеся").props.onClick();await settle();tree=render();assert.deepEqual(writes,["a","b"]);assert.ok(button(tree,"Закрыть"));assert.match(text(tree),/Не сохранилось:.*0/);
});

test("actual postpone panel confirms the preview and conditionally returns the same task without replacing files",async()=>{
  const h=hooks();const app:any={session:{user:{userId:"owner"},familyId:"family"},groupId:"group",homework:[{id:"a",subject:"Мат",text:"Задача",done:false,created:"2026-10-01",targetNthOccurrence:1,files:[{id:"file"}]}],saveHomework:(item:any)=>{app.homework=[item];}};
  const dateOf=(row:any)=>`2026-10-0${2+row.targetNthOccurrence}`;
  const out=await load("./homework-postpone-view.tsx",[],{react:h.react,"./store":{useApp:()=>app},"./api":{authGeneration:()=>0},"./homework-postpone":postpone},{window:{setTimeout:(fn:()=>void)=>{fn();return 1;}}});const render=()=>h.render(()=>out.HomeworkPostpone({items:app.homework,dateOf}));
  button(render(),"Перенести личную домашку на следующее занятие").props.onClick();let tree=render();assert.match(text(tree),/2026-10-03.*2026-10-04/);assert.equal(app.homework[0].targetNthOccurrence,1);nodes(tree).find(node=>node.type==="input").props.onChange({target:{checked:true}});tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Подтвердить перенос")).props.onClick();await settle();tree=render();assert.equal(app.homework[0].targetNthOccurrence,2);
  nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Отменить сохранённые переносы")).props.onClick();await settle();assert.equal(app.homework[0].targetNthOccurrence,1);assert.equal(app.homework[0].files[0].id,"file");
});

test("postpone close/reopen retains partial result and conditional undo without touching failed rows or files",async()=>{
  const h=hooks();const make=(id:string)=>({id,subject:id,text:`Задача ${id}`,done:false,created:"2026-10-01",targetNthOccurrence:1,files:[{id:`file-${id}`}]});
  const app:any={session:{user:{userId:"owner"},familyId:"family"},groupId:"group",homework:[make("a"),make("b")],saveHomework:(item:any)=>{if(item.id==="b")throw Error("storage");app.homework=app.homework.map((row:any)=>row.id===item.id?item:row);}};
  const dateOf=(row:any)=>`2026-10-0${2+row.targetNthOccurrence}`;
  const out=await load("./homework-postpone-view.tsx",[],{react:h.react,"./store":{useApp:()=>app},"./api":{authGeneration:()=>0},"./homework-postpone":postpone},{window:{setTimeout:(fn:()=>void)=>{fn();return 1;},confirm:()=>false}});const render=()=>h.render(()=>out.HomeworkPostpone({items:app.homework,dateOf}));
  button(render(),"Перенести личную домашку на следующее занятие").props.onClick();let tree=render();for(const input of nodes(tree).filter(node=>node.type==="input"))input.props.onChange({target:{checked:true}});tree=render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Подтвердить перенос")).props.onClick();await settle();tree=render();assert.match(text(tree),/Перенесено на устройстве: 1/);button(tree,"Закрыть").props.onClick();tree=render();
  nodes(tree).find(node=>node.type==="button").props.onClick();tree=render();assert.match(text(tree),/Перенесено на устройстве: 1/);assert.ok(button(tree,"Повторить неудавшиеся переносы"));
  button(tree,"Новый перенос").props.onClick();tree=render();assert.match(text(tree),/Перенесено на устройстве: 1/);
  nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Отменить сохранённые переносы")).props.onClick();await settle();assert.equal(app.homework[0].targetNthOccurrence,1);assert.equal(app.homework[1].targetNthOccurrence,1);assert.equal(app.homework[0].files[0].id,"file-a");assert.equal(app.homework[1].files[0].id,"file-b");
});

test("actual publication requires preview and explicit consent, retaining uncertain IDs after a route remount",async()=>{
  const profile=publication.publicationProfile("owner","family");publication.publicationMemory.profile(profile);let fail=true;const sent:any[]=[];
  const app:any={session:{authenticated:true,user:{userId:"owner"},familyId:"family"},groupId:"group",homework:[{id:"a",subject:"Мат",text:"Задача",done:false,created:"2026-10-01",files:[{id:"no-upload"}]}]};
  const home={communityId:"community",name:"Группа",classmates:[{userId:"owner",self:true}]};const space={capabilities:{homeworkAudience:true},desk:{roles:[]}};
  const recipients={home,space,supported:true,loading:false,error:false,retry:()=>{}};
  async function instance(){const h=hooks();const out=await load("./homework-publication-view.tsx",[],{react:h.react,"./store":{useApp:()=>app},"./api":{authGeneration:()=>0,groupSpace:async()=>space,groupHome:async()=>home,shareHomework:async(...args:any[])=>{sent.push(args);if(fail)throw Error("network");return{homeworkId:"published"};}},"./homework-publication-batch":publication,"./draft-revocation":revocation,"./homework-audience":{allHomeworkAudience:()=>({kind:"all",roleIds:[],userIds:[]}),audienceLabel:()=>"Вся группа"}},{sessionStorage:{getItem:()=>null},window:{confirm:()=>true}});return {h,render:()=>h.render(()=>out.HomeworkPublication({items:app.homework,communityId:"community",recipients,dateOf:()=>"2026-10-03"}))};}
  let view=await instance();button(view.render(),"Опубликовать выбранную личную домашку").props.onClick();let tree=view.render();nodes(tree).find(node=>node.type==="input").props.onChange({target:{checked:true}});tree=view.render();nodes(tree).find(node=>node.type==="button"&&text(node).startsWith("Предпросмотр публикаций")).props.onClick();tree=view.render();assert.equal(sent.length,0);button(tree,"Подтвердить публикацию группе").props.onClick();await settle();tree=view.render();assert.equal(sent.length,1);assert.match(text(tree),/Не подтверждено/);button(tree,"Закрыть, сохранив набор в памяти").props.onClick();view.h.unmount();
  view=await instance();button(view.render(),"Продолжить публикацию выбранной домашки").props.onClick();tree=view.render();fail=false;button(tree,"Повторить неподтверждённые с прежними номерами").props.onClick();await settle();tree=view.render();assert.equal(sent.length,2);assert.deepEqual(sent[0],sent[1]);assert.equal(sent[0][4],null);assert.ok(sent[0][6]);assert.equal(app.homework[0].done,false);assert.match(text(tree),/Опубликовано/);view.h.unmount();publication.publicationMemory.profile("cleanup");
});

test("actual week comparison responds to chosen real date and rejects an empty comparison date",async()=>{
  const h=hooks();const out=await load("./study-overviews-view.tsx",[],{react:h.react,"./study-overviews":overviews,"./parity":parity,"./planner":planner,"./summary":summary,"./schedule-text":scheduleText,"./ux300-controls":{useBrowseValue:(_key:string,initial:any)=>h.react.useState(initial)}});
  const lesson={dayOfWeek:1,timeStart:"09:00",timeEnd:"10:00",parity:1,subjectRaw:"Математика"};
  const render=()=>h.render(()=>out.WeekComparison({lessons:[lesson],date:new Date(2026,9,5),period:{start:"2026-09-01",weekCount:2},invert:false,available:true}));
  let tree=render();assert.match(text(tree),/5–11 окт\./);nodes(tree).find(node=>node.type==="input").props.onChange({target:{value:"2026-10-05"}});tree=render();assert.match(text(tree),/Состав пар совпадает/);
  nodes(tree).find(node=>node.type==="input").props.onChange({target:{value:""}});tree=render();assert.match(text(tree),/нужны данные всех семи дней/);
});

test("actual weekly deadline and subject-overview links keep precise task identity and canonical selection",async()=>{
  const h=hooks();const out=await load("./study-overviews-view.tsx",[],{react:h.react,"react-router-dom":{Link:"link"},"./study-overviews":overviews,"./parity":parity,"./planner":planner});
  const items=[{id:"a&b",subject:" МАТЕМАТИКА ",text:"Задача",done:true,created:""},{id:"unknown",subject:"Математика",text:"Без даты",done:false,created:""}];const days=Array.from({length:7},(_,i)=>new Date(2026,11,28+i));const dateOf=(row:any)=>row.id==="a&b"?"2027-01-01":null;
  const week=out.WeekHomework({items,days,dateOf});assert.equal(nodes(week).find(node=>node.type==="link").props.to,"/homework?id=a%26b");assert.match(text(week),/Выполнено/);assert.match(text(week),/Без известной даты:.*1/);
  let picked:any;const tree=out.HomeworkSubjectOverview({items,dateOf,today:"2026-10-02",onPick:(...args:any[])=>{picked=args;}});nodes(tree).find(node=>node.type==="button").props.onClick();assert.equal(picked[1],"математика");assert.match(text(tree),/Активно:.*1.*выполнено:.*1/);
});



test("actual diagnostics preview requires explicit copy and cancels after unmount",async()=>{
 const h=hooks();let resolve!:(value:any)=>void,copied='';const app:any={groupId:'secret-group',session:{user:{userId:'secret-owner'},familyId:'family'},timetableAvailable:true,privateHomework:{pending:[{conflict:undefined}]}};
 const out=await load('./study-discovery-view.tsx',[],{react:h.react,'./store':{useApp:()=>app},'./api':{authGeneration:()=>0,readCache:()=>({lessons:{'secret-group':{group:{id:'secret-group'},meta:{fetchedAt:'2026-10-01'}}}}),loadMaps:()=>new Promise(r=>resolve=r)},'./study-discovery':discovery},{document:{scripts:[{src:'https://local/app/assets/index-deadbeef.js'}]},navigator:{clipboard:{writeText:async(value:string)=>{copied=value;}}}});
 const render=()=>h.render(()=>out.SupportDiagnostics());button(render(),'Подготовить предпросмотр').props.onClick();resolve({maps:[1,2]});await settle();let tree=render();assert.equal(copied,'');assert.doesNotMatch(text(tree),/secret-group|secret-owner/);button(tree,'Скопировать эту сводку').props.onClick();await settle();assert.match(copied,/deadbeef/);assert.match(copied,/каталоге сервера: 2/);
 button(render(),'Подготовить предпросмотр').props.onClick();h.unmount();resolve({maps:[]});await settle();assert.equal(h.lateWrites(),0);
});

test("actual exact lesson action rejects a removed room and opens only its complete raw tuple",async()=>{
 const h=hooks();let url='',savedDate:any;const row:any={dayOfWeek:1,parity:0,index:1,timeStart:'09:00',timeEnd:'10:30',subjectRaw:'Алгебра',typeRaw:'зач',teacherRaw:'Иванов',classroomRaw:'101;'};
 const app:any={groupId:'g',session:{},subgroups:{},invert:false,setDate:(value:any)=>savedDate=value};let cache:any={lessons:{g:{period:{start:'2026-09-01',weekCount:2},lessons:[row]}}};
 const out=await load('./study-discovery-view.tsx',['useExactLesson'],{react:h.react,'./store':{useApp:()=>app},'react-router-dom':{useNavigate:()=>((value:string)=>url=value)},'./api':{readCache:()=>cache},'./study-discovery':discovery,'./planner':planner,'./parity':parity,'./subgroups':subgroups});
 let action=h.render(()=>out.useExactLesson());action.open('2026-10-05',{...row,classroomRaw:'102;'});assert.equal(url,'');assert.match(h.render(()=>out.useExactLesson()).note,/изменилась/);
 cache.lessons.g.lessons=[row,{...row}];action=h.render(()=>out.useExactLesson());action.open('2026-10-05',row);assert.equal(url,'');cache.lessons.g.lessons=[row];action=h.render(()=>out.useExactLesson());action.open('2026-10-05',row);assert.equal(savedDate.getDate(),5);assert.equal(new URLSearchParams(url.split('?')[1]).get('rawTarget'),discovery.rawLessonKey(row));url='';app.groupId='other';h.render(()=>out.useExactLesson());action.open('2026-10-05',row);assert.equal(url,'');
});

test("actual subgroup preview applies only unchanged snapshot and never mutates during preview",async()=>{
 const h=hooks();let writes=0;const row:any={dayOfWeek:1,parity:0,index:1,timeStart:'09:00',timeEnd:'10:30',subjectRaw:'Язык',typeRaw:'пр',teacherRaw:'А',classroomRaw:'101;'};
 const app:any={groupId:'g',session:{},subgroups:{g:{}},date:new Date(2026,9,5),invert:false,lessons:[row],timetableAvailable:true,pickSubgroup:()=>writes++};
 const out=await load('./study-discovery-view.tsx',[],{react:h.react,'./store':{useApp:()=>app},'./api':{readCache:()=>({lessons:{g:{period:{start:'2026-09-01',weekCount:2}}}})},'./study-discovery':discovery,'./study-overviews':overviews,'./parity':parity,'./planner':planner,'./subgroups':{...subgroups,subgroupIndex:()=>({streams:[{id:'stream',title:'Язык',options:[{id:'option',label:'А'}]}]})}});
 const render=()=>h.render(()=>out.SubgroupPreview());nodes(render()).find(node=>node.type==='button'&&text(node).includes('Посмотреть вариант:')).props.onClick();let tree=render();assert.equal(writes,0);const apply=button(tree,'Применить показанный вариант');app.invert=true;render();apply.props.onClick();assert.equal(writes,0);assert.match(text(render()),/изменились/);nodes(render()).find(node=>node.type==='button'&&text(node).includes('Посмотреть вариант:')).props.onClick();button(render(),'Применить показанный вариант').props.onClick();assert.equal(writes,1);
});

for(const reason of ['owner','lease','unmount'])test(`actual obligations cancels pending source after ${reason}`,async()=>{
 const h=hooks();let resolve!:(value:any)=>void,calls=0;const app:any={groupId:'g',session:{user:{userId:'owner'},familyId:'family'}};const memory=new Map<string,string>();const storage={getItem:(key:string)=>memory.get(key)??null,setItem:(key:string,value:string)=>memory.set(key,value),removeItem:(key:string)=>memory.delete(key)};
 const topics=[{topicId:'one',kind:'forms',title:'One'},{topicId:'two',kind:'forms',title:'Two'}];
 const out=await load('./group-obligations-view.tsx',[],{react:h.react,'./store':{useApp:()=>app},'./draft-revocation':revocation,'./group-obligations':obligations,'./ux300-controls':{useClock:()=>new Date()},'./api':{authGeneration:()=>0,topics:async()=>({topics}),ballots:async()=>({ballots:[]}),groupForms:()=>{calls++;return new Promise(r=>resolve=r);}}},{sessionStorage:storage});
 const render=()=>h.render(()=>out.GroupObligations({communityId:'community',onOpen:()=>{throw Error('late navigation');}}));button(render(),'Обновить обзор обязательств').props.onClick();await settle();assert.equal(calls,1);
 if(reason==='owner'){app.session.user.userId='other';render();}else if(reason==='lease')revocation.purgeGroupDrafts(storage,{owner:'owner',community:'community'});else h.unmount();resolve({forms:[{formId:'private',title:'Sensitive',canRespond:true,ownResponse:null,deadlineAt:null}]});await settle();assert.equal(calls,1);assert.equal(h.lateWrites(),0);if(reason!=='unmount')assert.doesNotMatch(text(render()),/Sensitive/);
});


test("actual obligations includes general board and revalidates exact ballot before opening",async()=>{
 const h=hooks();let reads=0,opened:any=null;const app:any={groupId:'g',session:{user:{userId:'owner'},familyId:'family'}};const storage={getItem:()=>null,setItem:()=>{},removeItem:()=>{}};
 const board={ballots:[{ballotId:'global-one',question:'Общее голосование',status:'open',deadlineAt:null,options:[{chosen:false}]},{ballotId:'typed-other',topicId:'unselected-topic',question:'Не включать из агрегата',status:'open',deadlineAt:null,options:[{chosen:false}]}]};
 const out=await load('./group-obligations-view.tsx',[],{react:h.react,'./store':{useApp:()=>app},'./draft-revocation':revocation,'./group-obligations':obligations,'./ux300-controls':{useClock:()=>new Date()},'./api':{authGeneration:()=>0,topics:async()=>({topics:[]}),ballots:async(_community:string,topic?:string)=>{assert.equal(topic,undefined);reads++;return board;}}},{sessionStorage:storage});
 const render=()=>h.render(()=>out.GroupObligations({communityId:'community',onOpen:(topic:any,target:any,fresh:any)=>opened={topic,target,fresh}}));button(render(),'Обновить обзор обязательств').props.onClick();await settle();let tree=render();assert.match(text(tree),/Общее голосование/);assert.doesNotMatch(text(tree),/Не включать из агрегата/);assert.equal(reads,1);button(tree,'Открыть этот объект').props.onClick();await settle();assert.equal(reads,2);assert.equal(opened.topic,null);assert.equal(opened.target.id,'global-one');assert.equal(opened.fresh,board);
 opened=null;board.ballots=[];button(render(),'Открыть этот объект').props.onClick();await settle();assert.equal(opened,null);assert.match(text(render()),/больше недоступно/);
});

test("delayed inbox source retry cannot replace a newer full refresh", async()=>{
  const h=hooks();
  let socialCalls=0;
  let resolveRetry!:(value:any)=>void;
  let timer:(()=>void)|null=null;
  const documentListeners=new Map<string,Set<()=>void>>();
  const windowListeners=new Map<string,Set<()=>void>>();
  const target=(listeners:Map<string,Set<()=>void>>)=>({
    addEventListener(type:string,listener:()=>void){const handlers=listeners.get(type)??new Set<()=>void>();handlers.add(listener);listeners.set(type,handlers);},
    removeEventListener(type:string,listener:()=>void){listeners.get(type)?.delete(listener);},
  });
  const document={...target(documentListeners),hidden:false};
  const fakeWindow={...target(windowListeners),setInterval:(callback:()=>void)=>{timer=callback;return 1;},clearInterval:()=>{timer=null;}};
  const app={session:{authenticated:true,user:{userId:"owner"},familyId:"family"}};
  const friendHome=(preview:string)=>({code:"ABCDEFGH",incoming:[],outgoing:[],friends:[{userId:"peer",username:"peer",displayName:"Пётр",conversationId:"personal",lastBody:preview,lastAt:"2026-10-04T12:00:00Z",unread:1}]});
  const api={communities:async()=>[],socialHome:()=>{
    socialCalls++;
    if(socialCalls===1)return Promise.reject(Error("offline"));
    if(socialCalls===2)return new Promise(resolve=>{resolveRetry=resolve;});
    return Promise.resolve(friendHome("Новое сообщение"));
  }};
  const modules:any={
    react:h.react,
    "react-router-dom":{Link:()=>null,useParams:()=>({})},
    "./api":api,
    "./chatInbox":chatInbox,
    "./people":{PeoplePanel:()=>null},
    "./store":{useApp:()=>app},
    "./types":{},
    "./personal-composer":{emptyChatState:()=>"empty"},
    "./avatar-view":{Avatar:()=>null},
    "./personal-composer-context":{usePersonalDrafts:()=>[]},
    "./visible-refresh":visibleRefresh,
    "./chat-ui":chatUi,
  };
  const out=await load("./chat.tsx",["ChatInboxContent"],modules,{document,window:fakeWindow});
  const render=()=>h.render(()=>out.ChatInboxContent());
  render();
  await settle();
  button(render(),"Повторить этот источник").props.onClick();
  await settle();
  assert.equal(socialCalls,2);
  timer!();
  await settle();
  assert.equal(socialCalls,3);
  assert.match(text(render()),/Новое сообщение/);
  resolveRetry(friendHome("Старый ответ повтора"));
  await settle();
  assert.match(text(render()),/Новое сообщение/);
  assert.doesNotMatch(text(render()),/Старый ответ повтора/);
  h.unmount();
});


test("route restoration yields to a child-owned exact target instead of scrolling/focusing the heading",async()=>{
 const h=hooks();let frame:any,scrolls=0,headingFocus=0;const target={matches:()=>true};const heading={tabIndex:0,focus:()=>headingFocus++,matches:()=>false};const document:any={activeElement:target,documentElement:{scrollHeight:2000},body:{},querySelector:(selector:string)=>selector==='.stage'?{contains:(node:any)=>node===target}:selector==='.stage h1'?heading:null};
 const out=await load('./ux300-controls.tsx',[],{react:h.react},{document,MutationObserver:class{observe(){}disconnect(){}},requestAnimationFrame:(callback:any)=>{frame=callback;return 1;},cancelAnimationFrame:()=>{},window:{innerHeight:1080,scrollY:0,scrollTo:()=>scrolls++,setTimeout:()=>1,clearTimeout:()=>{},addEventListener:()=>{},removeEventListener:()=>{}}});
 h.render(()=>out.useRoutePosition('explicit-target'));frame();out.focusRouteHeading();assert.equal(scrolls,0);assert.equal(headingFocus,0);
 document.activeElement=document.body;out.focusRouteHeading();assert.equal(headingFocus,1);
});


for(const failed of [true,false])test(`actual provider keeps settled timetable state when catalog arrives late (failed=${failed})`,async()=>{
 const h=hooks();let finishCatalog!:(value:any)=>void;const groups={period:{start:'2026-09-01',weekCount:2},meta:{stale:false},groups:[{id:'g',name:'G'}]};const old={...groups,lessons:[{subjectRaw:'Old'}]},fresh={...groups,lessons:[{subjectRaw:'Fresh'}]};let cache:any={groups,lessons:{g:old}};const memory=new Map([['zapara.group','g']]);const privateState={items:[],settings:null,saveSettings:()=>{}};
 const out=await load('./store.tsx',[],{react:{...h.react,createContext:()=>({Provider:'provider'})},'./api':{readCache:()=>cache,writeCache:(value:any)=>cache=value,loadGroups:()=>new Promise(resolve=>finishCatalog=resolve),loadTimetable:()=>failed?Promise.reject(Error('503')):Promise.resolve(fresh)},'./private-sync':{usePrivateHomework:()=>privateState},'./groupChoice':{resolveStoredGroup:(value:string)=>value},'./intersectionStrictness':{normalizeIntersectionStrictness:()=>1},'./session-refresh':{createSessionRefresher:()=>async()=>{}},'./subgroups':subgroups,'./next-workflows':{subgroupUndoCurrent:()=>false}},
 {localStorage:{getItem:(key:string)=>memory.get(key)??null,setItem:(key:string,value:string)=>memory.set(key,value)},document:{documentElement:{dataset:{}},addEventListener:()=>{},removeEventListener:()=>{}},window:{matchMedia:()=>({matches:false,addEventListener:()=>{},removeEventListener:()=>{}}),setInterval:()=>1,clearInterval:()=>{},addEventListener:()=>{},removeEventListener:()=>{}}});
 const render=()=>h.render(()=>out.Provider({children:null})).props.value;render();await settle();let state=render();assert.equal(state.timetableLoading,false);assert.equal(state.timetableFailed,failed);assert.equal(state.lessons[0].subjectRaw,failed?'Old':'Fresh');
 finishCatalog(groups);await settle();state=render();assert.equal(state.timetableLoading,false);assert.equal(state.timetableFailed,failed);assert.equal(state.lessons[0].subjectRaw,failed?'Old':'Fresh');if(failed)assert.match(state.notice,/не обновилось/);state.setGroupId('g');state=render();assert.equal(state.timetableLoading,false);
});
