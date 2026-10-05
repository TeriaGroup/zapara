package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.runBlocking
import org.junit.Assert.*
import org.junit.Test
import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.testToken
import ru.bgtu_voenmeh.zapara.data.api.*

class GroupSpaceClientTest {
    private val id = "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa"
    private val topicId = "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb"
    private val formId = "cccccccc-cccc-4ccc-8ccc-cccccccccccc"
    private val questionId = "dddddddd-dddd-4ddd-8ddd-dddddddddddd"
    private val time = "2026-09-26T12:00:00Z"
    private val token = testToken("za_", 5)
    private fun client(http: FakeHttp) = CommunityHttpClient(http, AccountServerScope.parse("https://example.invalid/"))
    private fun topic(kind: String, template: String) = """{"topicId":"$topicId","title":"Topic","icon":"","kind":"$kind","lastBody":null,"lastAuthor":null,"lastAt":null,"unread":0,"canDelete":false,"activeBallots":0,"description":"","accent":"default","pinned":false,"writePolicy":"all","canPost":true,"template":"$template","categoryId":null,"position":2,"subject":null,"archived":false,"revision":3,"permissions":["read","formsRespond"],"supported":true}"""
    private fun space(topic: String) = """{"topics":[$topic],"categories":[],"capabilities":{"maxRoles":12,"maxRolesPerMember":3,"maxTopics":24,"powers":["read"],"templates":["forms"]},"desk":{"headman":false,"roles":[],"grants":[],"applicants":[],"powers":[],"mine":[]}}"""
    private fun form() = """{"formId":"$formId","topicId":"$topicId","title":"Form","description":"","deadlineAt":null,"anonymous":true,"questions":[{"questionId":"$questionId","title":"Question","kind":"singleChoice","required":true,"options":["Yes","No"]}],"createdBy":"$id","createdAt":"$time","canRespond":true,"canViewResponses":false,"ownResponse":{"respondentId":null,"answers":[{"questionId":"$questionId","text":null,"choices":["Yes"]}],"updatedAt":"$time"},"responseCount":1}"""
    @Test fun unknown_kind_and_template_are_preserved_without_composer_permission() = runBlocking {
        val rows = client(FakeHttp { jsonReply(space(topic("future", "future"))) }).space(token, id).topics
        assertEquals("future", rows.single().kind)
        assertFalse(rows.single().supported)
        assertFalse(rows.single().canPost)
    }
    @Test fun forms_use_specialized_permissions_and_advertise_protocol_header() = runBlocking {
        val api = client(FakeHttp { call -> assertEquals("1", call.headers["X-Zapara-Group-Space"]); jsonReply(space(topic("forms", "forms"))) })
        val row = api.space(token, id).topics.single()
        assertTrue(row.supported); assertFalse(row.canPost); assertTrue("formsRespond" in row.permissions); assertEquals(3, row.revision)
    }
    @Test fun anonymous_form_preserves_own_choices_without_exposing_person_identity() = runBlocking {
        val row = client(FakeHttp { jsonReply("""{"forms":[${form()}]}""") }).forms(token, id, topicId).single()
        assertNull(row.ownResponse!!.respondentId); assertEquals(listOf("Yes"), row.ownResponse.answers.single().choices)
    }
    @Test fun form_submission_uses_native_scoped_endpoint_and_question_identifiers() = runBlocking {
        val http = FakeHttp { call -> assertTrue(call.url.endsWith("/$id/space/forms/$formId/response")); val body = StrictJson.parse(String(call.body!!)).obj(); val answer = body.array("answers", 5).items.single().obj(); assertEquals(questionId, answer.text("questionId", 36)); assertEquals(listOf(JsonValue.Str("Yes")), answer.array("choices", 5).items); jsonReply(form()) }
        client(http).submitForm(token, id, formId, listOf(GroupFormAnswer(questionId, null, listOf("Yes"))))
        Unit
    }
    @Test fun preview_requires_exactly_one_subject_before_network_call() = runBlocking {
        val http = FakeHttp { error("must not be called") }
        try { client(http).previewPermissions(token, id, id, id); fail() } catch (e: CommunityClientException) { assertEquals(CommunityClientFailure.InvalidRequest, e.failure) }
    }
    @Test fun shared_homework_decodes_deadline_and_topic_without_personal_status() = runBlocking {
        val data = """[{"homeworkId":"$formId","communityId":"$id","title":"Task","body":"Body","revision":1,"createdAt":"$time","updatedAt":"$time","deadlineAt":"$time","topicId":"$topicId"}]"""
        val row = client(FakeHttp { jsonReply(data) }).listHomework(token, id).single()
        assertEquals(topicId, row.topicId); assertEquals(java.time.Instant.parse(time), row.deadlineAt)
    }
    @Test fun audience_contract_reads_capability_permissions_and_sends_selected_targets_with_stable_operation() = runBlocking {
        val roleId = "11111111-1111-4111-8111-111111111111"
        val personId = "22222222-2222-4222-8222-222222222222"
        val operationId = "33333333-3333-4333-8333-333333333333"
        val selected = HomeworkAudience("selected", listOf(roleId), listOf(personId))
        val reply = """{"homeworkId":"$formId","communityId":"$id","title":"Task","body":"Body","revision":1,"createdAt":"$time","updatedAt":"$time","audience":{"kind":"selected","roleIds":["$roleId"],"userIds":["$personId"]},"canEdit":true,"canComplete":false}"""
        val http = FakeHttp { call ->
            assertEquals("1", call.headers["X-Zapara-Homework"])
            if (call.method == "POST") {
                val body = StrictJson.parse(String(call.body!!)).obj()
                assertEquals(operationId, body.text("operationId", 36))
                assertEquals("selected", body.field("audience").obj().text("kind", 16))
                HttpReply(201, reply.toByteArray())
            } else jsonReply(space(topic("homework", "homework")).replace("\"templates\":[\"forms\"]", "\"templates\":[\"forms\"],\"homeworkAudience\":true"))
        }
        val api = client(http)
        assertTrue(api.space(token, id).capabilities.homeworkAudience)
        val row = api.shareHomework(token, id, "Task", "Body", 0, audience = selected, operationId = operationId)
        assertEquals(selected, row.audience)
        assertTrue(row.canEdit)
        assertFalse(row.canComplete)
    }
    @Test fun legacy_homework_preserves_completion_default_and_omits_new_fields() = runBlocking {
        val data = """{"homeworkId":"$formId","communityId":"$id","title":"Task","body":"Body","revision":1,"createdAt":"$time","updatedAt":"$time"}"""
        val http = FakeHttp { call ->
            assertFalse(String(call.body!!).contains("audience"))
            assertFalse(String(call.body!!).contains("operationId"))
            HttpReply(201, data.toByteArray())
        }
        val row = client(http).shareHomework(token, id, "Task", "Body", 0)
        assertTrue(row.canComplete)
        assertFalse(row.canEdit)
        assertNull(row.audience)
    }
    @Test fun aggregate_homework_copies_avoid_one_completion_request_per_task() = runBlocking {
        val second = "99999999-9999-4999-8999-999999999999"
        val data = """{"homeworkId":"$formId","communityId":"$id","title":"Task","body":"Body","revision":1,"createdAt":"$time","updatedAt":"$time"}"""
        val http = FakeHttp { call -> when {
            call.url.endsWith("/homework/copies") -> jsonReply("""[{"homeworkId":"$formId","title":"Task","body":"Body","revision":1,"completed":true,"completionRevision":2}]""")
            call.url.endsWith("/homework/$formId/completion") -> throw AssertionError("N+1 completion request")
            else -> throw AssertionError(call.url)
        } }
        val api = client(http)
        val row = CommunityHomework(formId, id, "Task", "Body", 1, java.time.Instant.parse(time), java.time.Instant.parse(time))
        val result = api.homeworkCompletions(token, id, listOf(row, row.copy(homeworkId = second, canComplete = false)))
        assertTrue(result.getValue(formId).completed)
        assertFalse(second in result)
    }
    @Test fun utc_writers_trim_zero_fraction_and_truncate_to_seven_digits() {
        val milli = java.time.Instant.parse("2026-09-26T12:00:00.120Z")
        assertEquals("2026-09-26T12:00:00.12Z", CommunityUtc.format(milli))
        assertEquals("2026-09-26T12:00:00.12Z", ru.bgtu_voenmeh.zapara.data.sync.SyncJson.utcText(milli))
        val nano = java.time.Instant.parse("2026-09-26T12:00:00.123456789Z")
        assertEquals("2026-09-26T12:00:00.1234567Z", CommunityUtc.format(nano))
        assertEquals("2026-09-26T12:00:00.1234567Z", ru.bgtu_voenmeh.zapara.data.sync.SyncJson.utcText(nano))
    }

    @Test fun response_pages_preserve_opaque_cursor_and_total() = runBlocking {
        val http = FakeHttp { call -> assertTrue(call.url.endsWith("?after=$questionId")); jsonReply("""{"formId":"$formId","responses":[],"nextCursor":"$topicId","totalResponses":70}""") }
        val page = client(http).formResponsesPage(token, id, formId, questionId)
        assertEquals(topicId, page.nextCursor); assertEquals(70, page.totalResponses)
    }
    @Test fun modern_large_response_requires_server_protocol_echo() = runBlocking {
        val data = """{"formId":"$formId","responses":[{"respondentId":null,"answers":[{"questionId":"$questionId","text":"${"x".repeat(70000)}","choices":[]}],"updatedAt":"$time"}],"nextCursor":null,"totalResponses":1}"""
        try { client(FakeHttp { HttpReply(200, data.toByteArray()) }).formResponsesPage(token, id, formId); fail() }
        catch (e: CommunityClientException) { assertEquals(CommunityClientFailure.BodyTooLarge, e.failure) }
        // Use many valid short answers instead of one oversized field.
        val padded = " ".repeat(70000) + """{"formId":"$formId","responses":[],"nextCursor":null,"totalResponses":0}"""
        val page = client(FakeHttp { HttpReply(200, padded.toByteArray(), headers = mapOf("X-Zapara-Group-Space" to "1")) }).formResponsesPage(token, id, formId)
        assertEquals(0, page.totalResponses)
    }

    @Test fun access_preview_uses_exact_draft_and_preserves_server_consequences_and_sources() = runBlocking {
        val http = FakeHttp { call ->
            assertTrue(call.url.endsWith("/space/topics/$topicId/access-preview"))
            val request=StrictJson.parse(String(call.body!!)).obj(); assertEquals(3,request.int("expectedRevision"))
            val rule=request.array("rules",5).items.single().obj(); assertEquals("allow",rule.text("state",16))
            jsonReply("""{"topicId":"$topicId","revision":3,"affectedCount":1,"beforeReaders":[],"afterReaders":["$id"],"participants":[{"userId":"$id","beforePermissions":[],"afterPermissions":["read"],"sources":{"read":"Server decision"}}]}""")
        }
        val rule=GroupAccessRule(null,"read","allow")
        val response=client(http).topicAccessPreview(token,id,topicId,listOf(rule),3)
        val approval=GroupAccessApproval(listOf(rule),response)
        assertEquals(listOf(id),approval.addedReaders); assertEquals("Server decision",response.participants.single().sources["read"])
        assertTrue(approval.matches(topicId,3,listOf(rule)))
        assertFalse(approval.matches(topicId,4,listOf(rule))); assertFalse(approval.matches(topicId,3,listOf(rule.copy(state="deny"))))
    }

}
