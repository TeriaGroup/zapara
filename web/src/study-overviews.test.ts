import assert from "node:assert/strict";
import { test } from "node:test";
import { actualStudyWeek, compareStudyWeeks, weekHomework, homeworkSubjectOverview } from "./study-overviews.ts";
import { browseHomework } from "./homework-browse.ts";
const period={start:"2026-09-01",weekCount:2,title:"Осень",timeZone:"Europe/Moscow"};
const lesson:any={dayOfWeek:1,timeStart:"09:00",timeEnd:"10:30",parity:0,subjectRaw:"Математика",typeRaw:"лек",teacherRaw:"Иванов",classroomRaw:"101;"};
test("actual week comparison aligns weekday, preserves duplicate multiplicity and raw teacher/room changes",()=>{
  const a=actualStudyWeek([lesson,lesson],new Date(2026,9,5),period,false,true);const b=actualStudyWeek([lesson],new Date(2026,9,12),period,false,true);
  const result=compareStudyWeeks(a,b);assert.equal(result.known,true);assert.equal(result.removed.length,1);assert.equal(result.added.length,0);assert.equal(result.removed[0].date.getDate(),5);
  const changed=compareStudyWeeks(actualStudyWeek([lesson],new Date(2026,9,5),period,false,true),actualStudyWeek([{...lesson,classroomRaw:"102;"}],new Date(2026,9,12),period,false,true));assert.equal(changed.added.length,1);assert.equal(changed.removed.length,1);assert.equal(changed.added[0].date.getDate(),12);
  assert.equal(compareStudyWeeks(a,actualStudyWeek([lesson,lesson],new Date(2026,9,12),period,false,true)).added.length,0);
});
test("unavailable or pre-period weeks cannot masquerade as a fully removed schedule",()=>{
  const valid=actualStudyWeek([lesson],new Date(2026,9,5),period,false,true);
  assert.equal(compareStudyWeeks(valid,actualStudyWeek([],new Date(2026,9,12),period,false,false)).known,false);
  assert.equal(compareStudyWeeks(valid,actualStudyWeek([lesson],new Date(2026,7,31),period,false,true)).known,false);
});
const item=(id:string,subject:string,done=false):any=>({id,subject,text:id,done,created:"2026-10-01"});
test("weekly personal deadlines retain completed rows, exact cross-year dates and unknown count",()=>{
  const rows=[item("inside","Мат"),item("done","Мат",true),item("outside","Мат"),item("unknown","Мат")];const dates:Record<string,string|null>={inside:"2026-12-31",done:"2027-01-01",outside:"2027-01-04",unknown:null};
  const result=weekHomework(rows,["2026-12-28","2026-12-29","2026-12-30","2026-12-31","2027-01-01","2027-01-02","2027-01-03"],row=>dates[row.id]);assert.equal(result.unknown,1);assert.equal(result.total,2);assert.equal(result.days[3].items[0].id,"inside");assert.equal(result.days[4].items[0].done,true);
});
test("canonical subject metrics and exact drilldown agree without the legacy broad prefix matcher",()=>{
  const rows=[item("past","  МАТЕМАТИКА "),item("today","Математика"),item("unknown","Математика"),item("done","Математика",true),item("suffix","Математика (лек)")];
  const due:Record<string,string|null>={past:"2026-10-01",today:"2026-10-02",unknown:null,done:"2026-09-01",suffix:"2026-10-05"};
  const result=homeworkSubjectOverview(rows,row=>due[row.id],"2026-10-02");const math=result.find(row=>row.key==="математика")!;assert.equal(math.active,3);assert.equal(math.done,1);assert.equal(math.overdue,1);assert.equal(math.noDate,1);assert.equal(math.nearest,"2026-10-01");assert.equal(result.length,2);
  const target=browseHomework(rows,[],{subject:math.subject,exactSubjectKey:math.key,query:"",status:"all",target:null,source:"personal"});assert.deepEqual(target.local.map(row=>row.id),math.ids);
});
