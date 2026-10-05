export type NonSecretAccountDraft={username:string;display:string;mode:"login"|"register";accepted:boolean};
const drafts=new Map<string,NonSecretAccountDraft>();
const names=new Map<string,string>();
export function accountDraft(owner:string):NonSecretAccountDraft{return {...(drafts.get(owner)??{username:"",display:"",mode:"login",accepted:false})};}
export function rememberAccountDraft(owner:string,value:NonSecretAccountDraft){drafts.set(owner,{username:value.username,display:value.display,mode:value.mode,accepted:value.accepted});}
export function displayNameDraft(owner:string,confirmed:string){return names.get(owner)??confirmed;}
export function rememberDisplayName(owner:string,value:string){names.set(owner,value);}
export function clearDisplayNameDraft(owner:string){names.delete(owner);}
export function legalReturn(state:unknown,owner:string):string {
  const value=state as {returnTo?:unknown;owner?:unknown}|null;
  return value?.owner===owner&&typeof value.returnTo==="string"&&/^\/settings(?:\?section=(?:account|study|appearance|notifications|data|help))?$/.test(value.returnTo)?value.returnTo:"/settings";
}
