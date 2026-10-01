import test from "node:test";
import assert from "node:assert/strict";
import { calendarWeek, currentSummarySegment, summaryDayDate, roomPlan, RequestEpoch } from "./ux-navigation.ts";
import { isoDay, parityOf } from "./parity.ts";
import { summaryCode } from "./summary.ts";
const period = { start: "2026-09-01", weekCount: 2, title: "Осень", timeZone: "Europe/Moscow" };
test("calendar week crosses month/year and keeps all seven absolute dates", () => {
  assert.deepEqual(calendarWeek(new Date(2027, 0, 3)).map(isoDay), ["2026-12-28","2026-12-29","2026-12-30","2026-12-31","2027-01-01","2027-01-02","2027-01-03"]);
});
test("current summary has the same effective parity as today's timetable, including inversion", () => {
  for (const date of [new Date(2026,8,1), new Date(2026,8,9)]) for (const invert of [false,true]) {
    const segment = currentSummarySegment(date, period);
    assert.equal(summaryCode(segment, invert), parityOf(date, period.start, 2, invert));
  }
  assert.equal(isoDay(summaryDayDate(new Date(2026,8,1), period, 1, false, 3)), "2026-09-09");
});
test("room context chooses known building/floor and never invents unknown plans", () => {
  const plans = [{id:"g4",building:"ГК",floor:4,url:"g4"},{id:"u2",building:"УЛК",floor:2,url:"u2"}];
  assert.equal(roomPlan(plans,"493а ГК")?.id,"g4");
  assert.equal(roomPlan(plans,"201*;")?.id,"u2");
  assert.equal(roomPlan(plans,"онлайн"),null);
  assert.equal(roomPlan(plans,"999 ГК"),null);
});
test("response from a previous choice/closed card/owner is rejected", () => {
  const requests = new RequestEpoch();
  const first = requests.begin("A"); const second = requests.begin("A");
  assert.equal(first(),false); assert.equal(second(),true);
  requests.invalidate(); assert.equal(second(),false);
  const old = requests.begin("A"); requests.scope("B"); assert.equal(old(),false);
});
