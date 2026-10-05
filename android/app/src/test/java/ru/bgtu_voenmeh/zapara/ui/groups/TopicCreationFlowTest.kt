package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.*
import kotlinx.coroutines.test.*
import org.junit.*
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.data.accounts.*
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.communities.*

@OptIn(ExperimentalCoroutinesApi::class)
class TopicCreationFlowTest {
    private val dispatcher=StandardTestDispatcher()
    private val group="aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val other="eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
    private val conversation="bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val user="cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val role="dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    @Before fun before(){Dispatchers.setMain(dispatcher)}
    @After fun after(){Dispatchers.resetMain()}
    private val general="""{"topicId":null,"title":"General","icon":"","kind":"chat","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":false,"writePolicy":"all","canPost":true}"""
    private val list="""{"topics":[$general],"canManageChannels":true}"""
    private fun server(modern:Boolean=true,access:Boolean=true)=FakeHttp { call -> when {
        call.url.endsWith("/communities") -> jsonReply("""[{"communityId":"$group","name":"O3313","description":"","revision":1,"role":"member"}]""")
        call.url.endsWith("/home") -> { val id=if(call.url.contains(other)) other else group; jsonReply("""{"communityId":"$id","name":"O3313","groupName":"O3313","groupChat":{"conversationId":"$conversation","kind":"group","communityId":"$id","title":"O3313","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0},"classmates":[],"directs":[]}""") }
        call.url.endsWith("/space") -> if(!modern) HttpReply(404,"{}".toByteArray()) else jsonReply("""{"topics":[],"categories":[],"capabilities":{"maxRoles":12,"maxRolesPerMember":3,"maxTopics":24,"powers":[],"templates":["chat","polls"]},"desk":{"headman":false,"roles":[{"roleId":"$role","name":"Chosen"}],"grants":[],"applicants":[],"powers":[],"mine":["channels"${if(access) ",\"access\"" else ""}]}}""")
        call.method=="POST" && call.url.endsWith("/topics?typed=1") -> HttpReply(201,list.toByteArray())
        call.url.endsWith("/topics?typed=1") -> jsonReply(list)
        else -> error(call.url)
    } }
    private fun vm(http:FakeHttp)=GroupViewModel(GroupRuntime(false,user,CommunityHttpClient(http,AccountServerScope.parse("http://127.0.0.1:9/")),{testToken("za_",6)},{"O3313"},startInChannelList=true))
    private fun draft(vm:GroupViewModel,custom:Boolean=false):TopicCreationDraft {
        vm.onEvent(GroupEvent.BeginCreation)
        val d=vm.state.value.creationDraft!!.copy(title="New channel",read=GroupAccessPresets.Selection(if(custom) GroupAccessPresets.Mode.Selected else GroupAccessPresets.Mode.All,if(custom) setOf(role) else emptySet()),post=GroupAccessPresets.Selection(if(custom) GroupAccessPresets.Mode.Headman else GroupAccessPresets.Mode.All))
        vm.onEvent(GroupEvent.CreationChanged(d)); return d
    }
    private fun creates(http:FakeHttp)=http.requests.filter { it.method=="POST" && it.url.endsWith("/topics?typed=1") }
    @Test fun presets_and_roles_use_one_atomic_create_and_hidden_topic_is_success()=runTest(dispatcher){
        val http=server();val vm=vm(http)
        try{runCurrent();val d=draft(vm,true);assertTrue(d.canSubmit(vm.state.value));vm.onEvent(GroupEvent.SubmitCreation);runCurrent()
            val body=StrictJson.parse(String(creates(http).single().body!!)).obj()
            val rows=body.array("initialAccessRules",100).items.map { it.obj() }
            assertEquals(setOf(Triple(null,"read","deny"),Triple(role,"read","allow"),Triple(null,"post","deny")),rows.map { Triple(it.nullableText("roleId",36),it.text("power",32),it.text("state",16)) }.toSet())
            assertFalse(http.requests.any { it.method=="POST" && it.url.contains("/access") });assertNull(vm.state.value.creationDraft);assertFalse(vm.state.value.failed);assertNull(vm.state.value.activeTopicId)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun defaults_omit_initial_acl_modern_channels_only_and_legacy()=runTest(dispatcher){
        for(modern in listOf(true,false)){val http=server(modern,false);val vm=vm(http)
            try{runCurrent();val d=draft(vm);assertTrue(d.canSubmit(vm.state.value));assertFalse(d.canCustomize(vm.state.value));vm.onEvent(GroupEvent.SubmitCreation);runCurrent()
                assertFalse(StrictJson.parse(String(creates(http).single().body!!)).obj().fields.containsKey("initialAccessRules"))
                if(!modern) assertFalse(StrictJson.parse(String(creates(http).single().body!!)).obj().fields.containsKey("template"))
                val custom=draft(vm,true);assertFalse(custom.canSubmit(vm.state.value));vm.onEvent(GroupEvent.SubmitCreation);runCurrent();assertEquals(1,creates(http).size)
            }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
        }
    }
    @Test fun failed_create_keeps_exact_draft_and_never_retries_public()=runTest(dispatcher){
        val http=server();val normal=http.handler;http.handler={if(it.method=="POST") HttpReply(403,"{}".toByteArray()) else normal(it)};val vm=vm(http)
        try{runCurrent();val d=draft(vm,true);vm.onEvent(GroupEvent.SubmitCreation);runCurrent();assertEquals(d,vm.state.value.creationDraft);assertEquals(1,creates(http).size);assertTrue(vm.state.value.failed)}finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun late_ack_preserves_newer_input_and_other_group_draft()=runTest(dispatcher){
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();http.handler={if(it.method=="POST"){release.await();normal(it)}else normal(it)};val vm=vm(http)
        try{runCurrent();val submitted=draft(vm,true);vm.onEvent(GroupEvent.SubmitCreation);runCurrent()
            val newer=submitted.copy(title="Newer unsaved");vm.onEvent(GroupEvent.CreationChanged(newer));vm.onEvent(GroupEvent.Open(other));runCurrent();val otherDraft=draft(vm)
            release.complete(Unit);runCurrent();assertEquals(otherDraft,vm.state.value.creationDraft);assertEquals(other,vm.state.value.communityId)
            vm.onEvent(GroupEvent.Open(group));runCurrent();assertEquals(newer,vm.state.value.creationDraft);assertEquals(1,creates(http).size)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun late_ack_in_current_group_preserves_newer_input_but_reports_success()=runTest(dispatcher){
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();http.handler={if(it.method=="POST"){release.await();normal(it)}else normal(it)};val vm=vm(http)
        try{runCurrent();val submitted=draft(vm,true);vm.onEvent(GroupEvent.SubmitCreation);runCurrent()
            val newer=submitted.copy(description="New unsaved text");vm.onEvent(GroupEvent.CreationChanged(newer));release.complete(Unit);runCurrent()
            assertEquals(newer,vm.state.value.creationDraft);assertNotNull(vm.state.value.creationNotice);assertFalse(vm.state.value.channelBusy);assertFalse(vm.state.value.failed)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun unchanged_ack_after_group_switch_clears_only_submitted_draft_on_return()=runTest(dispatcher){
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();http.handler={if(it.method=="POST"){release.await();normal(it)}else normal(it)};val vm=vm(http)
        try{runCurrent();draft(vm,true);vm.onEvent(GroupEvent.SubmitCreation);runCurrent();vm.onEvent(GroupEvent.Open(other));runCurrent();val b=draft(vm)
            release.complete(Unit);runCurrent();assertEquals(b,vm.state.value.creationDraft);assertNull(vm.state.value.creationNotice)
            vm.onEvent(GroupEvent.Open(group));runCurrent();assertNull(vm.state.value.creationDraft)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun navigating_back_to_pending_creation_cannot_send_duplicate_rpc()=runTest(dispatcher){
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();http.handler={if(it.method=="POST"){release.await();normal(it)}else normal(it)};val vm=vm(http)
        try{runCurrent();val submitted=draft(vm,true);vm.onEvent(GroupEvent.SubmitCreation);runCurrent();vm.onEvent(GroupEvent.Open(other));runCurrent();vm.onEvent(GroupEvent.Open(group));runCurrent()
            assertEquals(submitted,vm.state.value.creationDraft);assertTrue(vm.state.value.creationSubmitting);assertFalse(submitted.canSubmit(vm.state.value))
            vm.onEvent(GroupEvent.SubmitCreation);runCurrent();assertEquals(1,creates(http).size);release.complete(Unit);runCurrent()
            assertNull(vm.state.value.creationDraft);assertFalse(vm.state.value.creationSubmitting)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun creation_selected_post_preserves_empty_picker_then_sends_first_role()=runTest(dispatcher){
        val http=server();val vm=vm(http)
        try{runCurrent();val empty=draft(vm).copy(post=GroupAccessPresets.Selection(GroupAccessPresets.Mode.Selected));vm.onEvent(GroupEvent.CreationChanged(empty));runCurrent()
            assertEquals(GroupAccessPresets.Mode.Selected,vm.state.value.creationDraft!!.post.mode)
            vm.onEvent(GroupEvent.CreationChanged(empty.copy(post=empty.post.copy(roles=setOf(role)))));vm.onEvent(GroupEvent.SubmitCreation);runCurrent()
            val rules=StrictJson.parse(String(creates(http).single().body!!)).obj().array("initialAccessRules",100).items.map { it.obj() }
            assertTrue(rules.any { it.nullableText("roleId",36)==role && it.text("power",32)=="post" && it.text("state",16)=="allow" })
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
}
