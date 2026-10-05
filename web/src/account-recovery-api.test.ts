import test from "node:test";
import assert from "node:assert/strict";
import { requestPasswordReset, confirmPasswordReset, revokeDevice, revokeAllDevices, updateDisplayName, changeAccountPassword } from "./api.ts";

test("profile and password changes use existing authenticated BFF contracts",async()=>{
  const original=globalThis.fetch;const calls:any[]=[];
  globalThis.fetch=async(input,init)=>{calls.push({url:String(input),method:init?.method,body:JSON.parse(String(init?.body)),credentials:init?.credentials});return String(input).endsWith("/me")?new Response(JSON.stringify({userId:"owner",username:"user",displayName:"Новое имя"}),{status:200}):new Response(null,{status:204});};
  try { const user=await updateDisplayName("Новое имя");assert.equal(user.displayName,"Новое имя");await changeAccountPassword("old_test_password","new_test_password");assert.deepEqual(calls,[{url:"/web-api/account/me",method:"PATCH",body:{displayName:"Новое имя"},credentials:"same-origin"},{url:"/web-api/account/password/change",method:"POST",body:{currentPassword:"old_test_password",newPassword:"new_test_password"},credentials:"same-origin"}]); }
  finally {globalThis.fetch=original;}
});
test("recovery uses existing request/confirm BFF contracts and targeted revocation keeps the captured family", async () => {
  const original = globalThis.fetch;
  const calls: {url:string;body:unknown;method:string}[] = [];
  globalThis.fetch = async (input, init) => {
    calls.push({url:String(input),body:init?.body ? JSON.parse(String(init.body)) : null,method:init?.method || "GET"});
    return String(input).endsWith("/request") ? new Response("{}",{status:202,headers:{"Content-Type":"application/json"}}) : new Response(null,{status:204});
  };
  try {
    await requestPasswordReset("test_user");
    await confirmPasswordReset("test_token","test_password");
    await revokeDevice("captured-family"); await revokeAllDevices();
    assert.deepEqual(calls,[
      {url:"/web-api/auth/password-reset/request",body:{username:"test_user"},method:"POST"},
      {url:"/web-api/auth/password-reset/confirm",body:{token:"test_token",newPassword:"test_password"},method:"POST"},
      {url:"/web-api/account/devices/captured-family",body:null,method:"DELETE"},
      {url:"/web-api/account/sessions/revoke-all",body:null,method:"POST"},
    ]);
  } finally { globalThis.fetch = original; }
});
