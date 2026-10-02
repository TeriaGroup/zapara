type ClassroomContext = { classroomRaw?: string|null; roomRaw?: string|null; buildingRaw?: string|null };

/** Keep transport notation separate from the human-facing room caption. */
export function lessonMapContext(lesson: ClassroomContext): string {
  return new URLSearchParams({room:lesson.roomRaw||lesson.classroomRaw||"",building:lesson.buildingRaw||"",classroom:lesson.classroomRaw||""}).toString();
}

export function routeClassroom(classroom: string|null|undefined, room: string|null|undefined, building: string|null|undefined): string {
  if(classroom?.trim())return classroom.trim();
  const value=(room||"").trim();
  // Old links may already contain the canonical university notation.
  if(value.includes("*")||value.endsWith(";")||/^ВЦ\s*\d+[а-яa-z]?$/i.test(value))return value;
  const caption=/^(\d+[а-яa-z]?)\s+(ГК|УЛК|ВЦ)$/i.exec(value);
  const explicit=(building||"").trim().toLocaleUpperCase("ru");
  if(caption&&explicit&&caption[2].toLocaleUpperCase("ru")!==explicit)return "";
  const place=caption?.[1]||value;
  const block=explicit||caption?.[2].toLocaleUpperCase("ru")||"";
  if(!/^\d+[а-яa-z]?$/i.test(place)||!["ГК","УЛК","ВЦ"].includes(block))return "";
  return block==="ВЦ"?`ВЦ ${place}`:block==="УЛК"?`${place}*;`:`${place};`;
}
