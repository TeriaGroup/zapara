export function scalarInput(value:string,min:number,max:number,controls:boolean):boolean {
  let count=0;
  for(const char of value){const code=char.codePointAt(0)!;if(code===0||code>=0xd800&&code<=0xdfff||controls&&(code<32||code>=127&&code<=159))return false;count++;}
  return count>=min&&count<=max;
}
export function groupWireText(draft:string,context="",editing=false) {
  const text=draft.replace(/\r\n?/g,"\n").trim();const prefix=context.replace(/\r\n?/g,"\n").trim();
  const body=prefix&&!editing?`${prefix}\n\n${text}`:text;let count=0,invalid=false;
  for(const char of body){const code=char.codePointAt(0)!;count++;if(code>=0xd800&&code<=0xdfff||code===0||(code<32&&code!==9&&code!==10)||code>=127&&code<=159)invalid=true;}
  return {body,count,valid:!!text&&!invalid&&count<=2000,error:invalid?"В тексте есть недопустимые символы.":count>2000?`Сообщение вместе с контекстом: ${count} из 2000. Сократите текст или уберите связь.`:""};
}
