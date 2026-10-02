import assert from "node:assert/strict";
import { test } from "node:test";
import { rememberRoute, validRouteHistory, togglePinnedPlace, validPinnedPlaces } from "./route-memory.ts";

test("route history preserves distinct directions, moves a reused pair first and bounds to six",()=>{
  let rows:any[]=[];for(let i=0;i<8;i++)rows=rememberRoute(rows,{fromId:`a${i}`,toId:"end"});
  assert.equal(rows.length,6);assert.equal(rows[0].fromId,"a7");
  rows=rememberRoute(rows,{fromId:"a5",toId:"end"});assert.equal(rows[0].fromId,"a5");assert.equal(rows.length,6);
  rows=rememberRoute(rows,{fromId:"end",toId:"a5"});assert.equal(rows.length,6);assert.equal(rows[1].fromId,"a5");
  assert.deepEqual(validRouteHistory(rows,new Set(["a5","end"])),[{fromId:"end",toId:"a5"},{fromId:"a5",toId:"end"}]);
});
test("pinned places retain order, toggle independently and never exceed eight valid identities",()=>{
  let pins:string[]=[];for(let i=0;i<10;i++)pins=togglePinnedPlace(pins,`room${i}`);
  assert.equal(pins.length,8);assert.equal(pins.includes("room8"),false);
  pins=togglePinnedPlace(pins,"room3");assert.equal(pins.includes("room3"),false);
  pins=togglePinnedPlace(pins,"room8");assert.equal(pins.at(-1),"room8");
  assert.deepEqual(validPinnedPlaces([...pins,"room0"],new Set(["room0","room8"])),["room0","room8"]);
});
