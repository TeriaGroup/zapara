import { moscowLessonInstant } from "./calendar-export.ts";
import type { HomeworkAudience, HomeworkItem } from "./types.ts";
export type PublicationRow={sourceId:string;sourceStamp:string;operationId:string;payload:{title:string;body:string;deadlineAt:string|null};attempted:boolean;status:"ready"|"uncertain"|"published"|"changed";error:string};
export type PublicationBatch={audience:HomeworkAudience;rows:PublicationRow[];busy:boolean};
const stamp=(row:HomeworkItem,day:string|null)=>JSON.stringify([row.id,row.subject,row.text,row.created,row.legacyCreatedLocalDate,row.targetNthOccurrence??1,row.done,day]);
export function makePublicationBatch(items:HomeworkItem[],audience:HomeworkAudience,dateOf:(row:HomeworkItem)=>string|null,id:()=>string=()=>crypto.randomUUID()):PublicationBatch{
  const unique=[...new Map(items.filter(row=>!row.done).map(row=>[row.id,row])).values()].slice(0,50);
  return {audience:{kind:audience.kind,roleIds:[...audience.roleIds],userIds:[...audience.userIds]},busy:false,rows:unique.map(row=>{const day=dateOf(row),instant=day?moscowLessonInstant(day,"23:59"):null;return{sourceId:row.id,sourceStamp:stamp(row,day),operationId:id(),payload:{title:row.subject.trim(),body:row.text.trim(),deadlineAt:instant?new Date(Date.parse(instant)+59000).toISOString():null},attempted:false,status:"ready",error:""};})};
}
export async function publishHomeworkBatch(batch:PublicationBatch,actions:{current:()=>boolean;read:(id:string)=>HomeworkItem|undefined;dateOf:(row:HomeworkItem)=>string|null;send:(row:PublicationRow)=>Promise<unknown>;onChange:()=>void}){
  for(const row of batch.rows){if(!actions.current())break;if(row.status==="published")continue;
    if(!row.attempted){const local=actions.read(row.sourceId);if(!local||stamp(local,actions.dateOf(local))!==row.sourceStamp){row.status="changed";row.error="Личное задание изменилось после предпросмотра. Начните новый набор.";actions.onChange();continue;}}
    row.attempted=true;row.status="uncertain";row.error="";actions.onChange();
    try{await actions.send(row);if(!actions.current())break;row.status="published";}
    catch(error){if(!actions.current())break;row.error=error instanceof Error&&error.message==="409"?"Номер отправки не соответствует содержимому. Проверьте общую домашку; не создавайте повтор автоматически.":"Отправка не подтверждена. Повтор использует прежние данные и номер.";if(error instanceof Error&&["401","403","404"].includes(error.message)){actions.onChange();break;}}
    actions.onChange();
  }
}
/** Private drafts live only in this tab; switching account/session removes the previous profile. */
export class PublicationMemory{
  private owner="";private batches=new Map<string,PublicationBatch>();private listeners=new Set<()=>void>();
  profile(owner:string){if(owner!==this.owner){this.owner=owner;this.batches.clear();this.notify();}}
  get(owner:string,key:string){return owner===this.owner?this.batches.get(key)||null:null;}
  set(owner:string,key:string,batch:PublicationBatch|null){if(owner!==this.owner)return;if(batch)this.batches.set(key,batch);else this.batches.delete(key);this.notify();}
  pending(owner:string){if(owner!==this.owner)return{count:0,busy:false};const batches=[...this.batches.values()];return{count:batches.filter(batch=>batch.rows.some(row=>row.status!=="published")).length,busy:batches.some(batch=>batch.busy)};}
  subscribe(listener:()=>void){this.listeners.add(listener);return()=>{this.listeners.delete(listener);};}
  notify(){this.listeners.forEach(listener=>listener());}
}
export const publicationMemory=new PublicationMemory();
export const publicationProfile=(owner?:string|null,family?:string|null)=>JSON.stringify([owner||"guest",family||""]);
