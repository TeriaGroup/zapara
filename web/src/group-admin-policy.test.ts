import * as workflowRules from "./ux300.ts";
import assert from "node:assert/strict";
import { test } from "node:test";
import { readFile } from "node:fs/promises";
import { runInNewContext } from "node:vm";
import ts from "typescript";
import * as policy from "./topic-policy.ts";
import { groupPowers } from "./powers.ts";
const code=ts.transpileModule(await readFile(new URL("./group-admin.tsx",import.meta.url),"utf8"),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,jsx:ts.JsxEmit.ReactJSX}}).outputText;
const nodes=(tree:any):any[]=>Array.isArray(tree)?tree.flatMap(nodes):tree&&typeof tree==="object"?[tree,...nodes(tree.props?.children)]:[];
const text=(tree:any):string=>Array.isArray(tree)?tree.map(text).join(" "):tree&&typeof tree==="object"?text(tree.props?.children):typeof tree==="string"||typeof tree==="number"?String(tree):"";
const button=(tree:any,label:string)=>nodes(tree).find(node=>node.type==="button"&&text(node)===label);
const desk=(mine:string[]=[])=>({headman:false,mine,roles:[{roleId:"target",name:"Исходная роль",icon:"📚",position:1,revision:1},{roleId:"manager",name:"Управляющий",icon:"",position:100,revision:1}],grants:[{roleId:"manager",userId:"owner"}],powers:[],applicants:[],capabilities:{powers:["joins","exclude","roles","grants"],maxRoles:12,maxRolesPerMember:3,maxTopics:24,templates:[]}});
function harness(initial:any,api:any,classmates:any[]=[{userId:"owner",self:true,role:"member",username:"owner",displayName:"Я"}]) {
  const states:any[]=[];
  const effects = new Map<number,{deps?:any[];cleanup?:()=>void}>();
  const pending = new Map<number,()=>any>();
  let index=0,current=initial,communityId="c",changed=false;
  const errors:string[]=[];
  const react = {
    useState(initial:any) {
      const slot=index++;
      if (!(slot in states)) states[slot]=typeof initial==="function"?initial():initial;
      return [states[slot],(value:any)=>{ const next=typeof value==="function"?value(states[slot]):value;
        if (!Object.is(next,states[slot])) { states[slot]=next; changed=true; } }];
    },
    useEffect(effect:()=>any,deps?:any[]) {
      const slot=index++,old=effects.get(slot);
      if (!old || !deps || !old.deps || deps.length!==old.deps.length || deps.some((value,i)=>!Object.is(value,old.deps![i]))) {
        effects.set(slot,{deps:deps?.slice(),cleanup:old?.cleanup}); pending.set(slot,effect);
      }
    },
  };
  const jsx=(type:any,props:any)=>({type,props});
  const modules:any={react,"react/jsx-runtime":{jsx,jsxs:jsx,Fragment:"fragment"},"./api":api,"./ux300":workflowRules,"./ux300-controls":{SearchField:()=>null},"./topic-policy":policy,"./topics":{powerTitles:{}},"./powers":{groupPowers},"./avatar-view":{AvatarEditor:()=>null}};
  const context={Error,exports:{} as any,require:(id:string)=>modules[id],window:{confirm:()=>true}};
  runInNewContext(code,context);
  return {
    errors,
    confirm(value:boolean){context.window.confirm=()=>value;},
    replace(next:any){current=next;},
    community(value:string){communityId=value;},
    unmount(){effects.forEach(effect=>effect.cleanup?.());effects.clear();pending.clear();},
    render(){
      let tree:any;
      for(let pass=0;pass<25;pass++) {
        changed=false;index=0;
        tree=context.exports.GroupAdmin({communityId,groupName:"Группа",classmates,desk:current,onChange:(next:any)=>{current=next;},onReload:async()=>{},onError:(message:string)=>errors.push(message)});
        const scheduled=[...pending];pending.clear();
        for(const [slot,effect] of scheduled) { const record=effects.get(slot)!;record.cleanup?.();const cleanup=effect();record.cleanup=typeof cleanup==="function"?cleanup:undefined; }
        if(!changed)return tree;
      }
      throw new Error("Role effects did not settle");
    },
  };
}
test("actual role editor submits opening revision after desk refresh and requires explicit conflict resolution",async()=>{const submitted:number[]=[];const fresh=desk(["roles"]);fresh.roles[0]={...fresh.roles[0],name:"Серверная роль",icon:"🧪",position:2,revision:2};const app=harness(desk(["roles"]),{groupDesk:async()=>fresh,saveRoleSettings:async(_c:string,_id:string,name:string,_icon:string,_position:number,revision:number)=>{submitted.push(revision);if(revision===1)throw new Error("409");assert.equal(revision,2);assert.equal(name,"Мой черновик");return fresh;}});let tree=app.render();button(tree,"Изменить").props.onClick();tree=app.render();nodes(tree).find(node=>node.type==="input"&&node.props.value==="Исходная роль").props.onChange({target:{value:"Мой черновик"}});app.replace(fresh);tree=app.render();nodes(tree).find(node=>node.type==="form"&&text(node).includes("Базовая ревизия редактора")).props.onSubmit({preventDefault(){}});await new Promise(resolve=>setImmediate(resolve));tree=app.render();assert.deepEqual(submitted,[1]);assert.match(text(tree),/Базовая ревизия редактора:.*1/);assert.match(text(tree),/Серверная роль/);assert.match(text(tree),/Мой черновик/);assert.equal(button(tree,"Сохранить").props.disabled,true);button(tree,"Использовать актуальную ревизию").props.onClick();tree=app.render();assert.equal(button(tree,"Сохранить").props.disabled,false);nodes(tree).find(node=>node.type==="form"&&text(node).includes("Базовая ревизия редактора")).props.onSubmit({preventDefault(){}});await new Promise(resolve=>setImmediate(resolve));assert.deepEqual(submitted,[1,2]);});
test("actual audit control follows only final global channels/access/roles policy",async()=>{for(const powers of [[],["pin","moderate"],["channels"],["access"],["roles"]]){let reads=0;const app=harness(desk(powers),{groupAudit:async()=>{reads++;return{events:[]};}});const tree=app.render();const audit=button(tree,"Журнал управления");if(powers.some(power=>["channels","access","roles"].includes(power))){assert.ok(audit);audit.props.onClick();await new Promise(resolve=>setImmediate(resolve));assert.equal(reads,1);}else{assert.equal(audit,undefined);assert.equal(reads,0);}}});
test("exclude authority remains actionable before the group has any custom role",async()=>{const base=desk(["exclude"]);base.roles=[];base.grants=[];let removed="";const app=harness(base,{removeGroupMember:async(_community:string,id:string)=>{removed=id;return base;}},[{userId:"owner",self:true,role:"member",username:"owner"},{userId:"peer",self:false,role:"member",username:"peer"},{userId:"protected",self:false,role:"curator",username:"protected"}]);const tree=app.render();assert.match(text(tree),/Ролей пока нет/);const exclude=button(tree,"Исключить");assert.ok(exclude);exclude.props.onClick();await new Promise(resolve=>setImmediate(resolve));assert.equal(removed,"peer");assert.equal(nodes(tree).filter(node=>node.type==="button"&&text(node)==="Исключить").length,1);});

test("actual member filters survive same-role refresh and reset only for another role/community", () => {
  const base = desk(["grants"]);
  const classmates = Array.from({length:45},(_,index)=>({userId:`person-${index}`,self:false,role:"member",username:`person${index}`,displayName:`Участник ${index}`}));
  const app = harness(base,{},classmates);
  button(app.render(),"Участники").props.onClick();
  let tree = app.render();
  assert.equal(nodes(tree).filter(node=>node.props?.className==="role-person row").length,45);
  const search = () => nodes(tree).find(node=>node.type==="input"&&node.props.type==="search");
  search().props.onChange({target:{value:"person44"}});
  tree=app.render();
  assert.equal(nodes(tree).filter(node=>node.props?.className==="role-person row").length,1);
  app.replace({...base,roles:base.roles.map(role=>({...role,revision:role.revision+1}))});
  tree=app.render();assert.equal(search().props.value,"person44");
  nodes(tree).find(node=>node.props?.className==="btn group-role-card"&&text(node).includes("Управляющий")).props.onClick();
  tree=app.render();button(tree,"Участники").props.onClick();tree=app.render();
  assert.equal(search().props.value,"");
  search().props.onChange({target:{value:"person1"}});
  nodes(tree).find(node=>node.type==="input"&&node.props.type==="checkbox").props.onChange({target:{checked:true}});
  tree=app.render();app.community("another-community");tree=app.render();
  assert.equal(search().props.value,"");
  assert.equal(nodes(tree).find(node=>node.type==="input"&&node.props.type==="checkbox").props.checked,false);
  app.unmount();
});


test("dirty role selection and cancel retain draft when discard is refused",()=>{
  const app=harness(desk(["roles"]),{});let tree=app.render();button(tree,"Изменить").props.onClick();tree=app.render();nodes(tree).find(n=>n.type==="input"&&n.props.value==="Исходная роль").props.onChange({target:{value:"Несохранённое имя"}});app.confirm(false);tree=app.render();button(tree,"Отмена").props.onClick();tree=app.render();assert.ok(nodes(tree).some(n=>n.type==="input"&&n.props.value==="Несохранённое имя"));const other=nodes(tree).find(n=>n.type==="button"&&n.props.className==="btn group-role-card"&&text(n).includes("Управляющий"));other.props.onClick();tree=app.render();assert.ok(nodes(tree).some(n=>n.type==="input"&&n.props.value==="Несохранённое имя"));app.confirm(true);button(tree,"Отмена").props.onClick();assert.equal(nodes(app.render()).some(n=>n.type==="input"&&n.props.value==="Несохранённое имя"),false);
});
test("headman role editor accepts 10000 but blocks fractional or out of bound positions",async()=>{
  const base={...desk(["roles"]),headman:true};const positions:number[]=[];const app=harness(base,{saveRoleSettings:async(_c:string,_id:string,_name:string,_icon:string,p:number)=>{positions.push(p);return base;}});let tree=app.render();button(tree,"Изменить").props.onClick();tree=app.render();const field=()=>nodes(tree).find(n=>n.type==="input"&&n.props.type==="number");assert.equal(field().props.max,10000);for(const value of ["10001","-1","1.5",""]){field().props.onChange({target:{value}});tree=app.render();assert.equal(button(tree,"Сохранить").props.disabled,true);nodes(tree).find(n=>n.type==="form"&&text(n).includes("Базовая ревизия")).props.onSubmit({preventDefault(){}});}assert.deepEqual(positions,[]);field().props.onChange({target:{value:"10000"}});tree=app.render();assert.equal(button(tree,"Сохранить").props.disabled,false);nodes(tree).find(n=>n.type==="form"&&text(n).includes("Базовая ревизия")).props.onSubmit({preventDefault(){}});await new Promise(resolve=>setImmediate(resolve));assert.deepEqual(positions,[10000]);
});
