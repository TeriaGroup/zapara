package ru.bgtu_voenmeh.zapara.ui.schedule

import kotlinx.coroutines.*
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.test.*
import org.junit.Test
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.data.accounts.*
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.communities.*
import java.time.*
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor

@OptIn(ExperimentalCoroutinesApi::class)
class ScheduleProjectionControllerTest {
    private val date=LocalDate.of(2026,9,26)
    private val cid="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val hid="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val at=Instant.parse("2026-09-26T12:00:00Z")
    private val own=HomeworkRowUi(1,"Personal","Local","active",false)
    private val lesson=LessonUi(1,"09:00","10:35","","Math",null,"","","",null,emptyList(),emptyList(),false,"Math","math")
    private fun page(snapshot:ScheduleProjectionController.Shared)=DayPage(date,true,"Public",listOf(lesson),null,false,
        deadlines=listOf(own)+snapshot.rows.map { HomeworkRowUi(0,it.body,"Shared","active",snapshot.completions[it.homeworkId]?.completed==true,it.homeworkId) })
    @Test fun delayed_io_page_cannot_restore_shared_after_a_real_denied_refresh() = runTest {
        val http=FakeHttp { call -> when {
            call.url.contains("/communities?") -> jsonReply("""[{"communityId":"$cid","name":"Group","description":"","revision":1,"role":"member"}]""")
            else -> HttpReply(403,"{}".toByteArray())
        } }
        val cache=SharedHomeworkCache()
        val api=CommunityHttpClient(http,AccountServerScope.parse("https://example.invalid/"))
        val state=MutableStateFlow(ScheduleUiState(loaded=true,hasGroup=true,selected=date))
        val controller=ScheduleProjectionController(state)
        val captured=controller.assign(cid,listOf(CommunityHomework(hid,cid,"Math","Private task",1,at,at)),mapOf(hid to HomeworkCompletion(hid,false,1,at)))
        val shared=page(captured).deadlines.last()
        state.value=state.value.copy(pages=mapOf(date to page(captured)),subjectRows=listOf(own,shared),sharedDetail=shared,undoShared=hid to false)
        val built=CompletableDeferred<Unit>(); val release=CompletableDeferred<Unit>(); var calls=0
        val io=StandardTestDispatcher(testScheduler)
        val pending=launch { controller.ensure(date,true,{true}) { snapshot -> withContext(io) {
            val result=page(snapshot); calls++
            if(calls==1) { built.complete(Unit); release.await() }
            result
        } } }
        runCurrent(); assertTrue(built.isCompleted)
        val denied=cache.refresh(api,testToken("za_",5),"3313"); assertTrue(denied.revoked)
        controller.purge()
        assertNull(state.value.sharedDetail); assertNull(state.value.undoShared)
        release.complete(Unit); pending.join()
        assertTrue(state.value.pages.values.all { p -> p.deadlines.none { it.sharedId!=null } })
        assertEquals(listOf(own),state.value.subjectRows); assertNull(state.value.sharedDetail); assertNull(state.value.undoShared)
        assertEquals(listOf(lesson),state.value.pages[date]!!.lessons); assertEquals(listOf(own),state.value.pages[date]!!.deadlines)
        assertEquals(date,state.value.selected); assertTrue(calls>=2)
    }
    @Test fun completion_change_reprojects_instead_of_accepting_old_done_flag() = runTest {
        val state=MutableStateFlow(ScheduleUiState(loaded=true,hasGroup=true,selected=date))
        val controller=ScheduleProjectionController(state)
        controller.assign(cid,listOf(CommunityHomework(hid,cid,"Math","Private task",1,at,at)),mapOf(hid to HomeworkCompletion(hid,false,1,at)))
        val release=CompletableDeferred<Unit>(); var calls=0
        val pending=launch { controller.ensure(date,true,{true}) { snapshot ->
            val result=page(snapshot); calls++; if(calls==1) release.await(); result
        } }
        runCurrent()
        controller.complete(HomeworkCompletion(hid,true,2,at))
        controller.ensure(date,true,{true}) { page(it) }
        assertTrue(state.value.pages[date]!!.deadlines.last().done)
        release.complete(Unit); pending.join()
        assertTrue(state.value.pages[date]!!.deadlines.last().done); assertTrue(calls>=2)
    }
    @Test fun reassignment_uses_immutable_snapshot_and_reloads_a_previously_unloaded_page() = runTest {
        val state=MutableStateFlow(ScheduleUiState(loaded=true,hasGroup=true,selected=date));val controller=ScheduleProjectionController(state)
        val old=CommunityHomework(hid,cid,"Math","Old private",1,at,at)
        val rows=mutableListOf(old);val completions=mutableMapOf(hid to HomeworkCompletion(hid,false,1,at))
        val snapshot=controller.assign(cid,rows,completions);rows.clear();completions.clear()
        assertEquals(listOf(old),snapshot.rows);assertEquals(1,snapshot.completions.size)
        val release=CompletableDeferred<Unit>();var calls=0
        val pending=launch {controller.ensure(date,false,{true}) {s->val result=page(s);calls++;if(calls==1) release.await();result}}
        runCurrent();controller.assign(cid,listOf(old.copy(body="Current private",revision=2)),emptyMap());release.complete(Unit);pending.join()
        assertEquals("Current private",state.value.pages[date]!!.deadlines.last().text);assertEquals(2,calls)
    }
    @Test fun delayed_denial_of_A_does_not_clear_loaded_B_or_its_cache() = runTest {
        for(status in listOf(403,404)) {
            deniedCompletion(status,switchGroup=true,switchAccount=false)
            deniedCompletion(status,switchGroup=true,switchAccount=false,keepOldCache=true)
        }
    }
    @Test fun delayed_denial_after_account_change_same_group_does_not_clear_new_account() = runTest {
        for(status in listOf(401,403,404)) deniedCompletion(status,switchGroup=false,switchAccount=true)
    }
    @Test fun same_scope_denial_purges_even_after_epoch_and_completion_change() = runTest {
        for(status in listOf(403,404)) deniedCompletion(status,switchGroup=false,switchAccount=false)
    }
    private suspend fun TestScope.deniedCompletion(status:Int,switchGroup:Boolean,switchAccount:Boolean,keepOldCache:Boolean=false) {
        val profile=ProfileDescriptor(false,"ownerA","server","dbA")
        val nextProfile=if(switchAccount) profile.copy(userId="ownerB",databaseName="dbB") else profile
        val nextGroup=if(switchGroup) "B" else "A"
        val nextCid=if(switchGroup) "cccccccc-cccc-4ccc-8ccc-cccccccccccc" else cid
        val firstToken=testToken("za_",5);val nextToken=if(switchAccount) testToken("za_",6) else firstToken
        val release=CompletableDeferred<Unit>()
        val http=FakeHttp {call->when {
            call.method=="PUT" && call.url.endsWith("/completion") -> {release.await();HttpReply(status,"""{"status":$status,"code":"${when(status){401->"invalid_session";403->"forbidden";else->"not_found"}}"}""".toByteArray())}
            call.url.contains("/communities?") -> {val id=if(call.url.endsWith("groupId=B")) nextCid else cid;jsonReply("""[{"communityId":"$id","name":"Group","description":"","revision":1,"role":"member"}]""")}
            call.url.endsWith("/homework") -> {val id=if(call.url.contains(nextCid)) nextCid else cid;jsonReply("""[{"homeworkId":"$hid","communityId":"$id","title":"Math","body":"Current private","revision":1,"createdAt":"2026-09-26T12:00:00Z","updatedAt":"2026-09-26T12:00:00Z"}]""")}
            call.url.endsWith("/completion") -> jsonReply("""{"homeworkId":"$hid","completed":false,"revision":1,"updatedAt":"2026-09-26T12:00:00Z"}""")
            else -> error(call.url)
        }}
        val api=CommunityHttpClient(http,AccountServerScope.parse("https://example.invalid/"));val cache=SharedHomeworkCache()
        val initial=cache.refresh(api,firstToken,"A").snapshot!!
        val state=MutableStateFlow(ScheduleUiState(loaded=true,hasGroup=true,selected=date));val controller=ScheduleProjectionController(state)
        controller.assign(initial.communityId,initial.rows,initial.completions)
        val operation=ScheduleProjectionController.CompletionScope(profile,"A",cid)
        var current=operation
        val pending=launch {try{api.upsertCompletion(firstToken,cid,hid,true,1)}catch(e:CommunityClientException){
            controller.completionDenied(operation,current,e.failure,{cache.invalidate("A",cid,firstToken)},{cache.clear();controller.purge()})
        }}
        runCurrent()
        val currentCache=if(keepOldCache) SharedHomeworkCache() else cache
        val fresh=currentCache.refresh(api,nextToken,nextGroup).snapshot!!
        current=ScheduleProjectionController.CompletionScope(nextProfile,nextGroup,nextCid)
        controller.assign(fresh.communityId,fresh.rows,fresh.completions)
        controller.complete(HomeworkCompletion(hid,true,2,at))
        val private=page(controller.shared).deadlines.last()
        state.value=state.value.copy(pages=mapOf(date to page(controller.shared)),subjectRows=listOf(own,private),sharedDetail=private,undoShared=hid to false)
        val before=state.value;release.complete(Unit);pending.join()
        if(switchGroup || switchAccount) {
            assertEquals(before,state.value);assertEquals(fresh,currentCache.snapshot);assertEquals(nextCid,controller.shared.communityId)
            if(keepOldCache) assertNull(cache.snapshot)
        }else{
            assertNull(cache.snapshot);assertNull(state.value.sharedDetail);assertNull(state.value.undoShared);assertEquals(listOf(own),state.value.subjectRows)
            assertEquals(listOf(own),state.value.pages[date]!!.deadlines);assertEquals(listOf(lesson),state.value.pages[date]!!.lessons)
        }
    }
    @Test fun invalid_session_is_account_wide_but_scoped_denial_invalidates_only_existing_old_cache_key()=runTest {
        val state=MutableStateFlow(ScheduleUiState(loaded=true,hasGroup=true,selected=date));val controller=ScheduleProjectionController(state)
        val profile=ProfileDescriptor(false,"owner","server","db")
        val op=ScheduleProjectionController.CompletionScope(profile,"A",cid)
        val current=op.copy(groupId="B",communityId="new")
        var invalidations=0;var purges=0
        controller.completionDenied(op,current,CommunityClientFailure.Forbidden,{invalidations++},{purges++})
        assertEquals(1,invalidations);assertEquals(0,purges)
        controller.completionDenied(op,current,CommunityClientFailure.InvalidSession,{invalidations++},{purges++})
        assertEquals(2,invalidations);assertEquals(1,purges)
    }
}
