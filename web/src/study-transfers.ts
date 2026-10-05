import { computeCampusRoute, resolveCampusClassroom, type CampusGraph } from "./campus-routing.ts";
import { routeClassroom } from "./route-context.ts";
import { assessTransfer } from "./study-planning.ts";
import { clockMinutes } from "./planner.ts";
import type { Lesson } from "./types.ts";

export function lessonTransfers(lessons: Lesson[], graph: CampusGraph) {
  const ordered=[...lessons].sort((a,b)=>(clockMinutes(a.timeStart)??Infinity)-(clockMinutes(b.timeStart)??Infinity));
  const overlapping=(row:Lesson)=>{
    const start=clockMinutes(row.timeStart),end=clockMinutes(row.timeEnd);
    if(start===null||end===null||end<=start)return true;
    return ordered.some(other=>{if(other===row)return false;const from=clockMinutes(other.timeStart),to=clockMinutes(other.timeEnd);return from!==null&&to!==null&&from<end&&start<to;});
  };
  return ordered.slice(1).map((next,index)=>{
    const previous=ordered[index];
    const from=resolveCampusClassroom(graph,routeClassroom(previous.classroomRaw,previous.roomRaw,previous.buildingRaw));
    const to=resolveCampusClassroom(graph,routeClassroom(next.classroomRaw,next.roomRaw,next.buildingRaw));
    const result=from&&to&&!overlapping(previous)&&!overlapping(next)?computeCampusRoute(graph,from.id,to.id):null;
    return {previous,next,...assessTransfer(previous.timeEnd,next.timeStart,result?.ok?result.route.durationSeconds:null)};
  });
}
