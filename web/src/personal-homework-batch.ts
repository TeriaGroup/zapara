import { sameSubject } from "./parity.ts";
import type { HomeworkItem } from "./types.ts";

export type PersonalBatchResult = { saved: string[]; failed: string[]; skipped: string[]; stopped: boolean };
export async function completePersonalBatch(ids: string[], actions: {
  current:()=>boolean;read:(id:string)=>HomeworkItem|undefined;save:(item:HomeworkItem)=>void|Promise<void>;checkpoint?:()=>Promise<void>;
}):Promise<PersonalBatchResult> {
  const result:PersonalBatchResult={saved:[],failed:[],skipped:[],stopped:false};
  for(const id of new Set(ids)){
    if(!actions.current()){result.stopped=true;break;}
    const row=actions.read(id);
    if(!row||row.done){result.skipped.push(id);continue;}
    try{await actions.save({...row,done:true});result.saved.push(id);}catch{result.failed.push(id);}
    if(!actions.current()){result.stopped=true;break;}
    await actions.checkpoint?.();
  }
  if(!actions.current())result.stopped=true;
  return result;
}
export function duplicatePersonalHomework(items: HomeworkItem[], draft:{subject:string;text:string}, due:string|null, dateOf:(item:HomeworkItem)=>string|null) {
  const text=draft.text.trim().toLocaleLowerCase("ru");
  return items.find(item=>sameSubject(item.subject,draft.subject)&&item.text.trim().toLocaleLowerCase("ru")===text&&dateOf(item)===due);
}
