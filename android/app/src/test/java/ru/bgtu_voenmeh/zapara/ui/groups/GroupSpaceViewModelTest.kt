package ru.bgtu_voenmeh.zapara.ui.groups

import kotlinx.coroutines.*
import kotlinx.coroutines.test.*
import org.junit.*
import org.junit.Assert.*
import ru.bgtu_voenmeh.zapara.data.accounts.*
import ru.bgtu_voenmeh.zapara.data.api.*
import ru.bgtu_voenmeh.zapara.data.communities.*

@OptIn(ExperimentalCoroutinesApi::class)
class GroupSpaceViewModelTest {
    private val dispatcher = StandardTestDispatcher()
    private val group = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val conversation = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val user = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val forms = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    private val schedule = "eeeeeeee-eeee-4eee-8eee-eeeeeeeeeeee"
    @Before fun before() { Dispatchers.setMain(dispatcher) }
    @After fun after() { Dispatchers.resetMain() }
    private fun topic(id: String?, kind: String): String = """{"topicId":${id?.let { "\"$it\"" } ?: "null"},"title":"$kind","icon":"","kind":"$kind","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":false,"writePolicy":"all","canPost":false,"template":"$kind","categoryId":null,"position":0,"subject":null,"archived":false,"revision":1,"permissions":["read"],"supported":true}"""
    private fun space(hidden: Boolean) = """{"topics":[${topic(null, "chat")},${topic(schedule, "schedule")}${if (hidden) "" else ",${topic(forms, "forms")}"}],"categories":[],"capabilities":{"maxRoles":12,"maxRolesPerMember":3,"maxTopics":24,"powers":[],"templates":["forms","schedule"]},"desk":{"headman":false,"roles":[],"grants":[],"applicants":[],"powers":[],"mine":[]}}"""
    private fun server() = FakeHttp { call -> when {
        call.url.endsWith("/communities") -> jsonReply("""[{"communityId":"$group","name":"O3313","description":"","revision":1,"role":"member"}]""")
        call.url.endsWith("/$group/home") -> jsonReply("""{"communityId":"$group","name":"O3313","groupName":"O3313","groupChat":{"conversationId":"$conversation","kind":"group","communityId":"$group","title":"O3313","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0},"classmates":[],"directs":[]}""")
        call.url.endsWith("/$group/space") -> jsonReply(space(false))
        call.url.endsWith("/forms") -> jsonReply("""{"forms":[]}""")
        else -> error(call.url)
    } }
    private fun vm(http: FakeHttp) = GroupViewModel(GroupRuntime(false, user, CommunityHttpClient(http, AccountServerScope.parse("http://127.0.0.1:9/")), { testToken("za_", 6) }, { "O3313" }, startInChannelList = true, scheduleRows = { _, date -> listOf(date.toString()) }))
    @Test fun form_channel_never_opens_a_plain_chat_or_sends_its_draft() = runTest(dispatcher) {
        val http = server(); val vm = vm(http); runCurrent()
        vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
        assertEquals("forms", vm.state.value.activeChannelKind); assertNull(vm.state.value.activeConversationId)
        vm.onEvent(GroupEvent.Draft("Never send")); vm.onEvent(GroupEvent.Send); runCurrent()
        assertFalse(http.requests.any { it.url.contains("/messages") || it.url.contains("/topic-messages") })
        vm.onEvent(GroupEvent.Back); runCurrent()
    }
    @Test fun late_form_reply_cannot_replace_another_channel_or_leave_it_busy() = runTest(dispatcher) {
        val http = server(); val normal = http.handler; val release = CompletableDeferred<Unit>()
        http.handler = { call -> if (call.url.endsWith("/forms")) { release.await(); jsonReply("""{"forms":[]}""") } else normal(call) }
        val vm = vm(http); runCurrent(); vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
        assertTrue(vm.state.value.channelBusy)
        vm.onEvent(GroupEvent.OpenChannel(schedule)); runCurrent()
        assertEquals("schedule", vm.state.value.activeChannelKind); assertFalse(vm.state.value.channelBusy)
        release.complete(Unit); runCurrent()
        assertEquals("schedule", vm.state.value.activeChannelKind); assertEquals(listOf(vm.state.value.scheduleDate.toString()), vm.state.value.scheduleRows); assertFalse(vm.state.value.channelBusy)
        vm.onEvent(GroupEvent.Back); runCurrent()
    }
    @Test fun permission_refresh_closes_a_removed_topic_with_no_cached_content() = runTest(dispatcher) {
        val http = server(); val normal = http.handler; var hidden = false
        http.handler = { call -> if (call.url.endsWith("/$group/space")) jsonReply(space(hidden)) else normal(call) }
        val vm = vm(http); runCurrent(); vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
        hidden = true; advanceTimeBy(8000); runCurrent()
        assertTrue(vm.state.value.showChannels); assertNull(vm.state.value.activeTopicId); assertTrue(vm.state.value.forms.isEmpty())
        assertFalse(vm.state.value.channels.any { it.topicId == forms })
        vm.onEvent(GroupEvent.Back); runCurrent()
    }
    @Test fun excluded_membership_hides_cached_group_content() = runTest(dispatcher) {
        val http = server(); val normal = http.handler; var excluded = false
        http.handler = { call -> if (excluded && call.url.endsWith("/$group/space")) HttpReply(403, """{"title":"Forbidden","status":403,"code":"forbidden"}""".toByteArray()) else normal(call) }
        val vm = vm(http); runCurrent(); vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
        excluded = true; advanceTimeBy(8000); runCurrent()
        assertTrue(vm.state.value.accessRevoked); assertTrue(vm.state.value.channels.isEmpty()); assertTrue(vm.state.value.forms.isEmpty()); assertFalse(vm.state.value.canManageChannels)
        vm.onEvent(GroupEvent.Back); runCurrent()
    }
    @Test fun chat_polling_immediately_purges_known_access_loss_before_space_reconciliation() = runTest(dispatcher) {
        for (status in listOf(401, 403, 404)) {
            val http = server(); val normal = http.handler; var revoked = false
            http.handler = { call -> when {
                call.url.contains("/conversations/$conversation/messages") -> if (revoked) HttpReply(status, "{}".toByteArray()) else jsonReply("""{"messages":[{"messageId":"$forms","conversationId":"$conversation","senderId":"$user","senderName":"Member","body":"Secret","createdAt":"2026-09-26T12:00:00Z"}],"hasMore":false}""")
                revoked && call.url.endsWith("/$group/space") -> throw java.io.IOException("space unavailable")
                else -> normal(call)
            } }
            val vm = vm(http)
            try { runCurrent(); vm.onEvent(GroupEvent.OpenChannel(null)); runCurrent()
            assertEquals("Secret", vm.state.value.messages.single().body)
            revoked = true; advanceTimeBy(4000); runCurrent()
            assertTrue("status $status retained messages", vm.state.value.messages.isEmpty())
            assertTrue(vm.state.value.mediaFiles.isEmpty()); assertFalse(vm.state.value.canPost)
            assertNull(vm.state.value.activeConversationId)
            } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
        }
    }
    @Test fun ballot_polling_immediately_purges_known_access_loss() = runTest(dispatcher) {
        for (status in listOf(401, 403, 404)) {
            val http = server(); val normal = http.handler; var revoked = false
            http.handler = { call -> when {
                call.url.endsWith("/$group/space") -> jsonReply(space(false).replace(topic(forms, "forms"), topic(forms, "ballots").replace("\"template\":\"ballots\"", "\"template\":\"polls\"")))
                call.url.contains("/$group/ballots") -> if (revoked) HttpReply(status, "{}".toByteArray()) else jsonReply("""{"headman":false,"canOpen":false,"canClose":false,"members":2,"supportersNeeded":1,"ballots":[]}""")
                else -> normal(call)
            } }
            val vm = vm(http)
            try { runCurrent(); vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
            assertNotNull(vm.state.value.board)
            revoked = true; advanceTimeBy(8000); runCurrent()
            assertNull("status $status retained board", vm.state.value.board)
            assertFalse(vm.state.value.canPost)
            } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
        }
    }

    @Test fun pin_only_and_moderator_role_use_supported_mutations_without_channels() = runTest(dispatcher) {
        val http=server(); val normal=http.handler
        val scoped=topic(forms,"chat").replace("\"permissions\":[\"read\"]","\"permissions\":[\"read\",\"pin\",\"access\",\"moderate\"]")
        var pinned=false; var deleted=false
        http.handler={ call -> when {
            call.url.endsWith("/$group/space") -> jsonReply(space(false).replace(topic(forms,"forms"),scoped.replace("\"pinned\":false","\"pinned\":$pinned")))
            call.method=="POST" && call.url.endsWith("/$group/topics/$forms?typed=1") -> {
                val body=StrictJson.parse(String(call.body!!)).obj()
                assertEquals(1,body.int("expectedRevision")); assertEquals("chat",body.text("title",80)); assertEquals("all",body.text("writePolicy",16)); assertTrue(body.bool("pinned")); pinned=true
                jsonReply("""{"topics":[${topic(null,"chat")},${scoped.replace("\"pinned\":false","\"pinned\":true")}],"canManageChannels":false}""")
            }
            call.url.contains("/messages?") -> jsonReply("""{"messages":[{"messageId":"$forms","conversationId":"$conversation","senderId":"$schedule","senderName":"Other","body":"Private","createdAt":"2026-09-26T12:00:00Z"}],"hasMore":false}""")
            call.url.endsWith("/messages/$forms/delete") -> { deleted=true; jsonReply("""{"messageId":"$forms","conversationId":"$conversation","senderId":"$schedule","senderName":"Other","body":"","createdAt":"2026-09-26T12:00:00Z","deleted":true}""") }
            else -> normal(call)
        } }
        val vm=vm(http)
        try {
            runCurrent(); assertFalse(vm.state.value.canManageChannels)
            val row=vm.state.value.channels.first { it.topicId==forms }; assertTrue(GroupActions.canAccess(vm.state.value,row))
            vm.onEvent(GroupEvent.PinChannel(row,true)); runCurrent(); assertTrue(pinned)
            vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent(); assertFalse(vm.state.value.messages.single().mine); assertFalse(vm.state.value.canPost)
            vm.onEvent(GroupEvent.Hold(forms,"delete")); runCurrent(); assertTrue(deleted); assertTrue(vm.state.value.messages.single().deleted)
        } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
    }
    @Test fun access_save_conflict_invalidates_approval_and_keeps_save_blocked_until_fresh_preview() = runTest(dispatcher) {
        val http=server(); val normal=http.handler; var revision=1; var saves=0
        http.handler={ call -> when {
            call.url.endsWith("/topics/$forms/access-preview") -> jsonReply("""{"topicId":"$forms","revision":$revision,"affectedCount":1,"beforeReaders":[],"afterReaders":["$user"],"participants":[]}""")
            call.url.endsWith("/topics/$forms/access") && call.method=="GET" -> jsonReply("""{"topicId":"$forms","revision":$revision,"rules":[]}""")
            call.url.endsWith("/topics/$forms/access") && call.method=="POST" -> { saves++; revision=2; HttpReply(409,"""{"status":409,"code":"revision_conflict","title":"Conflict"}""".toByteArray()) }
            else -> normal(call)
        } }
        val vm=vm(http); val rules=listOf(GroupAccessRule(null,"read","allow"))
        try {
            runCurrent(); vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.LoadAccess(forms))); runCurrent()
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.PreviewAccess(forms,rules,1))); runCurrent(); assertNotNull(vm.state.value.accessApproval)
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Access(forms,rules,1))); runCurrent()
            assertEquals(1,saves); assertNull(vm.state.value.accessApproval); assertEquals(2,vm.state.value.access!!.revision)
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Access(forms,rules,2))); runCurrent(); assertEquals(1,saves)
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.PreviewAccess(forms,rules,2))); runCurrent()
            assertTrue(vm.state.value.accessApproval!!.matches(forms,2,rules)); assertEquals("allow",rules.single().state)
        } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
    }
    @Test fun access_preview_late_reply_is_invalid_after_panel_close_or_topic_switch() = runTest(dispatcher) {
        val http=server(); val normal=http.handler; val release=CompletableDeferred<Unit>()
        http.handler={ call -> when {
            call.url.endsWith("/topics/$forms/access") -> jsonReply("""{"topicId":"$forms","revision":1,"rules":[]}""")
            call.url.endsWith("/access-preview") -> { release.await(); jsonReply("""{"topicId":"$forms","revision":1,"affectedCount":1,"beforeReaders":[],"afterReaders":["$user"],"participants":[]}""") }
            else -> normal(call)
        } }
        val vm=vm(http)
        try {
            runCurrent(); vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.LoadAccess(forms))); runCurrent()
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.PreviewAccess(forms,listOf(GroupAccessRule(null,"read","allow")),1))); runCurrent()
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel(null))); release.complete(Unit); runCurrent()
            assertNull(vm.state.value.accessApproval); assertFalse(vm.state.value.channelBusy)
            vm.onEvent(GroupEvent.OpenChannel(schedule)); runCurrent(); assertNull(vm.state.value.accessApproval)
        } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
    }
    @Test fun subject_channel_loads_bound_context_and_only_related_personal_and_shared_tasks() = runTest(dispatcher) {
        val http=server(); val normal=http.handler; val at="2026-09-26T12:00:00Z"
        val scoped=topic(forms,"chat").replace("\"template\":\"chat\"","\"template\":\"subject\"").replace("\"subject\":null","\"subject\":\"Math\"")
        http.handler={ call -> when {
            call.url.endsWith("/$group/space") -> jsonReply(space(false).replace(topic(forms,"forms"),scoped))
            call.url.contains("/messages?") -> jsonReply("""{"messages":[],"hasMore":false}""")
            call.url.endsWith("/$group/homework") -> jsonReply("""[{"homeworkId":"$schedule","communityId":"$group","title":"Math","body":"Shared math","revision":1,"createdAt":"$at","updatedAt":"$at"},{"homeworkId":"$forms","communityId":"$group","title":"Physics","body":"Wrong subject","revision":1,"createdAt":"$at","updatedAt":"$at"}]""")
            call.url.endsWith("/completion") -> jsonReply("""{"homeworkId":"$schedule","completed":true,"revision":1,"updatedAt":"$at"}""")
            else -> normal(call)
        } }
        val vm=GroupViewModel(GroupRuntime(false,user,CommunityHttpClient(http,AccountServerScope.parse("http://127.0.0.1:9/")),{testToken("za_",6)},{"O3313"},startInChannelList=true,
            lessonContext={GroupLessonHint(java.time.LocalDate.now(),"08:00","Physics","1")},
            subjectContext={ name,subject,date -> assertEquals("O3313",name); assertEquals("Math",subject); GroupSubjectContext(GroupLessonHint(date,"12:00","Math","2"),listOf(GroupSubjectHomeworkUi(1,null,"Math","Personal math",date,false))) }))
        try {
            runCurrent(); vm.onEvent(GroupEvent.OpenChannel(forms)); runCurrent()
            assertEquals("Math",vm.state.value.subjectLesson!!.subject)
            assertEquals(listOf("Personal math","Shared math"),vm.state.value.subjectHomework.map { it.body }); assertTrue(vm.state.value.subjectHomework.last().done)
        } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
    }

    @Test fun readable_archive_survives_active_list_polling_then_purges_on_archive_authority_loss() = runTest(dispatcher) {
        val http=server(); val normal=http.handler; var hidden=false
        val archived=topic(forms,"chat").replace("\"archived\":false","\"archived\":true")
        http.handler={ call -> when {
            call.url.endsWith("/$group/space") -> jsonReply(space(true))
            call.url.endsWith("/$group/space/archive") -> jsonReply("""{"topics":${if(hidden) "[]" else "[$archived]"},"canManageChannels":false}""")
            call.url.contains("/messages?") -> jsonReply("""{"messages":[{"messageId":"$forms","conversationId":"$conversation","senderId":"$user","senderName":"Member","body":"Archived readable","createdAt":"2026-09-26T12:00:00Z"}],"hasMore":false}""")
            else -> normal(call)
        } }
        val vm=vm(http)
        try {
            runCurrent(); vm.onEvent(GroupEvent.OpenArchived(forms)); runCurrent()
            assertEquals(forms,vm.state.value.activeTopicId); assertNotNull(vm.state.value.activeArchivedTopic); assertFalse(vm.state.value.canPost)
            advanceTimeBy(4000); runCurrent()
            assertEquals("Archived readable",vm.state.value.messages.single().body); assertEquals(forms,vm.state.value.activeTopicId)
            val requests=http.requests.size; vm.onEvent(GroupEvent.Hold(forms,"delete")); runCurrent(); assertEquals(requests,http.requests.size)
            hidden=true; advanceTimeBy(4000); runCurrent()
            assertTrue(vm.state.value.messages.isEmpty()); assertNull(vm.state.value.activeArchivedTopic)
        } finally { vm.onEvent(GroupEvent.Back); runCurrent() }
    }

    @Test fun archive_panel_refreshes_authority_while_General_stays_selected_and_blocks_old_commands()=runTest(dispatcher) {
        val http=server();val normal=http.handler;var hidden=false
        val archived=topic(forms,"chat").replace("\"archived\":false","\"archived\":true").replace("\"title\":\"chat\"","\"title\":\"Private archive A\"").replace("\"description\":\"\"","\"description\":\"Secret description\"").replace("\"permissions\":[\"read\"]","\"permissions\":[\"read\",\"channels\"]")
        http.handler={call->when {
            call.url.endsWith("/space/archive")->jsonReply("""{"topics":${if(hidden) "[]" else "[$archived]"},"canManageChannels":false}""")
            call.url.contains("/messages?")->jsonReply("""{"messages":[],"hasMore":false}""")
            else->normal(call)
        }}
        val vm=vm(http)
        try{runCurrent();vm.onEvent(GroupEvent.OpenChannel(null));runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent()
            val old=vm.state.value.archived.single();assertEquals("Private archive A",old.title);assertEquals(conversation,vm.state.value.activeConversationId)
            hidden=true;advanceTimeBy(4000);runCurrent()
            assertEquals("archive",vm.state.value.spacePanel);assertEquals(conversation,vm.state.value.activeConversationId);assertTrue(vm.state.value.archived.isEmpty())
            val count=http.requests.size;vm.onEvent(GroupEvent.OpenArchived(forms));vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Archive(old,false)));runCurrent()
            assertEquals(count,http.requests.size);assertTrue(vm.state.value.archived.isEmpty())
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun old_slow_archive_reply_cannot_restore_rows_after_newer_authority()=runTest(dispatcher) {
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();var calls=0
        val archived=topic(forms,"chat").replace("\"archived\":false","\"archived\":true")
        http.handler={call->when {
            call.url.endsWith("/space/archive")->{val number=++calls;if(number==2) release.await();jsonReply("""{"topics":${if(number<=2) "[$archived]" else "[]"},"canManageChannels":false}""")}
            else->normal(call)
        }}
        val vm=vm(http)
        try{runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent();assertEquals(1,vm.state.value.archived.size)
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.LoadArchive));runCurrent();assertEquals(2,calls)
            advanceTimeBy(8000);runCurrent();assertTrue(calls>=3);assertTrue(vm.state.value.archived.isEmpty())
            release.complete(Unit);runCurrent();assertTrue(vm.state.value.archived.isEmpty());assertFalse(vm.state.value.channelBusy)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun archive_reply_is_bound_to_credentials_and_never_probes_legacy()=runTest(dispatcher) {
        val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>();var token=testToken("za_",6)
        val archived=topic(forms,"chat").replace("\"archived\":false","\"archived\":true")
        http.handler={call->if(call.url.endsWith("/space/archive")){release.await();jsonReply("""{"topics":[$archived]}""")}else normal(call)}
        val vm=GroupViewModel(GroupRuntime(false,user,CommunityHttpClient(http,AccountServerScope.parse("http://127.0.0.1:9/")),{token},{"O3313"},startInChannelList=true))
        try{runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent();token=testToken("za_",7);release.complete(Unit);runCurrent();assertTrue(vm.state.value.archived.isEmpty())}
        finally{vm.onEvent(GroupEvent.Back);runCurrent()}
        val legacy=server();val legacyNormal=legacy.handler
        legacy.handler={call->when {
            call.url.endsWith("/space")->HttpReply(404,"""{"status":404,"code":"not_found"}""".toByteArray())
            call.url.endsWith("/topics?typed=1")->jsonReply("""{"topics":[${topic(null,"chat")}],"canManageChannels":false}""")
            else->legacyNormal(call)
        }}
        val old=vm(legacy)
        try{runCurrent();assertNull(old.state.value.space);old.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));old.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.LoadArchive));old.onEvent(GroupEvent.OpenArchived(forms));old.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Archive(old.state.value.channels.single().copy(topicId=forms),true)));runCurrent();advanceTimeBy(8000);runCurrent();assertFalse(legacy.requests.any {it.url.contains("/space/")})}
        finally{old.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun delayed_archive_reply_after_community_switch_does_not_restore_old_scope()=runTest(dispatcher) {
        val other="ffffffff-ffff-4fff-8fff-ffffffffffff";val http=server();val normal=http.handler;val release=CompletableDeferred<Unit>()
        val archived=topic(forms,"chat").replace("\"archived\":false","\"archived\":true")
        http.handler={call->when {
            call.url.endsWith("/$group/space/archive")->{release.await();jsonReply("""{"topics":[$archived]}""")}
            call.url.endsWith("/$other/home")->jsonReply("""{"communityId":"$other","name":"O3314","groupName":"O3314","groupChat":{"conversationId":"$conversation","kind":"group","communityId":"$other","title":"O3314","peerUserId":null,"lastBody":null,"lastAt":null,"unread":0},"classmates":[],"directs":[]}""")
            call.url.endsWith("/$other/space")->jsonReply(space(true))
            call.url.endsWith("/$other/space/archive")->jsonReply("""{"topics":[]}""")
            else->normal(call)
        }}
        val vm=vm(http)
        try{runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent();vm.onEvent(GroupEvent.Open(other));runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent()
            release.complete(Unit);runCurrent();assertEquals(other,vm.state.value.communityId);assertTrue(vm.state.value.archived.isEmpty());assertEquals("archive",vm.state.value.spacePanel)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun closed_archive_authority_forgets_pre_archive_private_draft()=runTest(dispatcher) {
        val http=server();val normal=http.handler;var phase=0
        val active=topic(forms,"chat").replace("\"canPost\":false","\"canPost\":true").replace("\"permissions\":[\"read\"]","\"permissions\":[\"read\",\"post\"]")
        val archived=active.replace("\"archived\":false","\"archived\":true")
        http.handler={call->when {
            call.url.endsWith("/$group/space")->jsonReply(if(phase==0 || phase==3) space(false).replace(topic(forms,"forms"),active) else space(true))
            call.url.endsWith("/space/archive")->jsonReply("""{"topics":${if(phase==1) "[$archived]" else "[]"}}""")
            call.url.contains("/messages?")->jsonReply("""{"messages":[],"hasMore":false}""")
            else->normal(call)
        }}
        val vm=vm(http)
        try{runCurrent();vm.onEvent(GroupEvent.OpenChannel(forms));runCurrent();vm.onEvent(GroupEvent.Draft("Private old draft"));assertEquals("Private old draft",vm.state.value.draft)
            phase=1;vm.onEvent(GroupEvent.OpenChannel(null));runCurrent();vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Panel("archive")));runCurrent();assertEquals(1,vm.state.value.archived.size)
            phase=2;advanceTimeBy(4000);runCurrent();assertTrue(vm.state.value.archived.isEmpty())
            phase=3;advanceTimeBy(4000);runCurrent();vm.onEvent(GroupEvent.OpenChannel(forms));runCurrent();assertEquals("",vm.state.value.draft)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
    @Test fun restoring_selected_archive_is_not_misclassified_as_read_revocation()=runTest(dispatcher) {
        val http=server();val normal=http.handler;var restored=false
        val active=topic(forms,"chat").replace("\"permissions\":[\"read\"]","\"permissions\":[\"read\",\"channels\"]")
        val archived=active.replace("\"archived\":false","\"archived\":true")
        http.handler={call->when {
            call.url.endsWith("/$group/space")->jsonReply(if(restored) space(false).replace(topic(forms,"forms"),active) else space(true))
            call.url.endsWith("/space/archive")->jsonReply("""{"topics":${if(restored) "[]" else "[$archived]"}}""")
            call.url.endsWith("/topics/$forms/archive")->{restored=true;jsonReply(space(false).replace(topic(forms,"forms"),active))}
            call.url.contains("/messages?")->jsonReply("""{"messages":[],"hasMore":false}""")
            else->normal(call)
        }}
        val vm=vm(http)
        try{runCurrent();vm.onEvent(GroupEvent.OpenArchived(forms));runCurrent();val row=vm.state.value.activeArchivedTopic!!
            vm.onEvent(GroupEvent.SpaceAction(GroupSpaceAction.Archive(row,false)));runCurrent();assertTrue(restored);assertFalse(vm.state.value.accessRevoked);assertEquals(forms,vm.state.value.activeTopicId);assertNull(vm.state.value.activeArchivedTopic)
        }finally{vm.onEvent(GroupEvent.Back);runCurrent()}
    }
}
