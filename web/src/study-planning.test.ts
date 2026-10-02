import assert from 'node:assert/strict';
import { test } from 'node:test';
import { commonFreeIntervals, assessTransfer } from './study-planning.ts';
import { createAllDayCalendarExport } from './calendar-export.ts';
const first = [{start:'9:00',end:'10:00'},{start:'12:00',end:'13:00'}];
test('common free windows intersect known day bounds and merge busy overlaps', () => {
  assert.deepEqual(commonFreeIntervals(first,[{start:'09:30',end:'10:30'},{start:'11:30',end:'12:30'}]),
    [{start:'10:30',end:'11:30',minutes:60}]);
  assert.deepEqual(commonFreeIntervals(first,[{start:'09:30',end:'10:30'},{start:'10:15',end:'11:45'},{start:'12:00',end:'12:30'}],20),[]);
  assert.deepEqual(commonFreeIntervals(first,[]),[]);
  assert.deepEqual(commonFreeIntervals(first,[{start:'09:00',end:'bad'}]),[]);
});
test('transfer distinguishes unknown data, overlap, insufficient gap and exact equality', () => {
  assert.equal(assessTransfer('10:00','10:05',301).status,'tight');
  assert.equal(assessTransfer('10:00','10:05',300).status,'fits');
  assert.equal(assessTransfer('10:00','10:05',null).status,'unknown');
  assert.equal(assessTransfer('10:00','09:55',0).status,'overlap');
  assert.equal(assessTransfer('24:00','10:00',0).status,'unknown');
});
test('all-day calendar uses real date, exclusive rollover, stable UID and skips bad dates', async () => {
  const entry={id:'task',day:'2026-12-31',summary:'Задача, 1;\nстрока'};
  const result=await createAllDayCalendarExport([entry,entry,{...entry,day:'2026-02-30'},{...entry,day:''}], 'Домашка', new Date('2026-10-02T00:00:00Z'));
  assert.equal(result.eventCount,1); assert.equal(result.skippedCount,3);
  assert.ok(result.content.includes('DTSTART;VALUE=DATE:20261231\r\nDTEND;VALUE=DATE:20270101'));
  assert.ok(result.content.includes('SUMMARY:Задача\\, 1\\;\\nстрока'));
  assert.ok(result.content.includes('TRANSP:TRANSPARENT'));
  const again=await createAllDayCalendarExport([entry],'Другой',new Date('2027-01-01T00:00:00Z'));
  assert.equal(result.content.match(/UID:(.+)/)?.[1],again.content.match(/UID:(.+)/)?.[1]);
});
