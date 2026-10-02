import type { HomeworkItem } from "./types.ts";
export type PostponePreview = {id:string;subject:string;text:string;beforeDay:string;afterDay:string;beforeNth:number;afterNth:number;beforeStamp:string;afterStamp:string};
type Actions={current:()=>boolean;read:(id:string)=>HomeworkItem|undefined;dateOf:(row:HomeworkItem)=>string|null;save:(row:HomeworkItem)=>void|Promise<void>;checkpoint?:()=>Promise<void>};
export type PostponeResult={saved:PostponePreview[];failed:string[];stopped:boolean};
function stamp(row:HomeworkItem,day:string|null){return JSON.stringify([row.id,row.subject,row.text,row.created,row.legacyCreatedLocalDate,row.targetNthOccurrence??1,row.done,day]);}
export function postponePreview(rows:HomeworkItem[],dateOf:Actions["dateOf"]):PostponePreview[]{
  return rows.filter(row=>!row.done).flatMap(row=>{const n=row.targetNthOccurrence??1;if(!Number.isInteger(n)||n<1||n>=10)return[];const next={...row,targetNthOccurrence:n+1};const beforeDay=dateOf(row),afterDay=dateOf(next);if(!beforeDay||!afterDay||afterDay<=beforeDay)return[];
    return[{id:row.id,subject:row.subject,text:row.text,beforeDay,afterDay,beforeNth:n,afterNth:n+1,beforeStamp:stamp(row,beforeDay),afterStamp:stamp(next,afterDay)}];}).slice(0,50);
}
async function apply(rows:PostponePreview[],actions:Actions,undo:boolean):Promise<PostponeResult>{
  const result:PostponeResult={saved:[],failed:[],stopped:false};
  for(const preview of rows){if(!actions.current()){result.stopped=true;break;}const current=actions.read(preview.id);const expected=undo?preview.afterStamp:preview.beforeStamp;
    if(!current||stamp(current,actions.dateOf(current))!==expected){result.failed.push(preview.id);continue;}
    const next={...current,targetNthOccurrence:undo?preview.beforeNth:preview.afterNth};
    if(actions.dateOf(next)!==(undo?preview.beforeDay:preview.afterDay)){result.failed.push(preview.id);continue;}
    try{await actions.save(next);result.saved.push(preview);}catch{result.failed.push(preview.id);}
    if(!actions.current()){result.stopped=true;break;}await actions.checkpoint?.();
  }
  if(!actions.current())result.stopped=true;return result;
}
export const applyPostponement=(rows:PostponePreview[],actions:Actions)=>apply(rows,actions,false);
export const undoPostponement=(rows:PostponePreview[],actions:Actions)=>apply(rows,actions,true);
