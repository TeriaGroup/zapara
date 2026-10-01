export function quoteTarget(rows: {messageId:string;deleted?:boolean}[], id:string) {
  const target=rows.find(row=>row.messageId===id);
  return !target ? "unloaded" : target.deleted ? "deleted" : "loaded";
}
export function revealQuote(rows: {messageId:string;deleted?:boolean}[], id:string, prefix:string):string {
  const state=quoteTarget(rows,id);
  if(state!=="loaded") return state==="deleted" ? "Исходное сообщение удалено." : "Исходное сообщение не загружено. Загрузите более ранние сообщения.";
  const element=document.getElementById(prefix+id);
  if(!element)return "Исходное сообщение скрыто текущим фильтром.";
  element.scrollIntoView({block:"center",behavior:"auto"}); element.focus({preventScroll:true});
  element.classList.add("quote-target"); window.setTimeout(()=>element.classList.remove("quote-target"),1800);
  return "Исходное сообщение найдено.";
}
