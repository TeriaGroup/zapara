package ru.bgtu_voenmeh.zapara.data.communities

import ru.bgtu_voenmeh.zapara.data.accounts.AccountServerScope
import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
import ru.bgtu_voenmeh.zapara.data.api.HttpBodyTooLargeException
import ru.bgtu_voenmeh.zapara.data.api.HttpCall
import ru.bgtu_voenmeh.zapara.data.api.HttpExchange
import ru.bgtu_voenmeh.zapara.data.api.JsonFail
import ru.bgtu_voenmeh.zapara.data.api.JsonValue
import ru.bgtu_voenmeh.zapara.data.api.StrictJson
import ru.bgtu_voenmeh.zapara.data.api.arr
import ru.bgtu_voenmeh.zapara.data.api.array
import ru.bgtu_voenmeh.zapara.data.api.bool
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.int
import ru.bgtu_voenmeh.zapara.data.api.nullableText
import ru.bgtu_voenmeh.zapara.data.api.obj
import ru.bgtu_voenmeh.zapara.data.api.text
import java.io.IOException
import java.net.URLEncoder
import java.nio.charset.StandardCharsets
import java.time.Instant

class CommunityHttpClient(
    private val transport: HttpExchange,
    val scope: AccountServerScope
) {
    @Volatile private var legacyRoutes = false
    suspend fun list(accessToken: String, groupId: String? = null): List<Community> {
        val path = if (groupId == null) ""
        else "?groupId=" + URLEncoder.encode(CommunityValidation.groupId(groupId), StandardCharsets.UTF_8.name()).replace("+", "%20")
        return read("GET", path, null, accessToken, 200) { it.arr().items.map { row -> community(row.obj()) } }
    }

    suspend fun get(accessToken: String, communityId: String): Community {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id", null, accessToken, 200) { community(it.obj()) }
    }

    suspend fun requestJoin(accessToken: String, communityId: String): JoinRequest {
        val id = CommunityValidation.id(communityId)
        return read("POST", "/$id/join-requests", null, accessToken, 201) { join(it.obj()) }
    }

    suspend fun listJoinRequests(accessToken: String, communityId: String): List<JoinRequest> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/join-requests", null, accessToken, 200) { it.arr().items.map { row -> join(row.obj()) } }
    }

    suspend fun acceptJoin(accessToken: String, communityId: String, requestId: String): JoinRequest {
        val id = CommunityValidation.id(communityId)
        val rid = CommunityValidation.id(requestId)
        return read("POST", "/$id/join-requests/$rid/accept", null, accessToken, 200) { join(it.obj()) }
    }

    suspend fun rejectJoin(accessToken: String, communityId: String, requestId: String): JoinRequest {
        val id = CommunityValidation.id(communityId)
        val rid = CommunityValidation.id(requestId)
        return read("POST", "/$id/join-requests/$rid/reject", null, accessToken, 200) { join(it.obj()) }
    }

    suspend fun listMembers(accessToken: String, communityId: String): List<CommunityMember> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/members", null, accessToken, 200) { it.arr().items.map { row -> member(row.obj()) } }
    }

    suspend fun listStaff(accessToken: String, communityId: String): List<CommunityMember> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/staff", null, accessToken, 200) { it.arr().items.map { row -> member(row.obj()) } }
    }

    suspend fun listHomework(accessToken: String, communityId: String): List<CommunityHomework> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/homework", null, accessToken, 200) { it.arr().items.map { row -> homework(row.obj()) } }
    }

    suspend fun publishHomework(
        accessToken: String,
        communityId: String,
        title: String,
        body: String,
        expectedRevision: Long
    ): CommunityHomework {
        val id = CommunityValidation.id(communityId)
        return read("POST", "/$id/homework", homeworkBody(title, body, expectedRevision), accessToken, 201) { homework(it.obj()) }
    }

    suspend fun shareHomework(
        accessToken: String,
        communityId: String,
        title: String,
        body: String,
        expectedRevision: Long
    ): CommunityHomework {
        val id = CommunityValidation.id(communityId)
        return read("POST", "/$id/homework/share", homeworkBody(title, body, expectedRevision), accessToken, 201) { homework(it.obj()) }
    }

    suspend fun getHomework(accessToken: String, communityId: String, homeworkId: String): CommunityHomework {
        val id = CommunityValidation.id(communityId)
        val hid = CommunityValidation.id(homeworkId)
        return read("GET", "/$id/homework/$hid", null, accessToken, 200) { homework(it.obj()) }
    }

    suspend fun updateHomework(
        accessToken: String,
        communityId: String,
        homeworkId: String,
        title: String,
        body: String,
        expectedRevision: Long
    ): CommunityHomework {
        val id = CommunityValidation.id(communityId)
        val hid = CommunityValidation.id(homeworkId)
        return read("PUT", "/$id/homework/$hid", homeworkBody(title, body, expectedRevision), accessToken, 200) { homework(it.obj()) }
    }

    suspend fun getCompletion(accessToken: String, communityId: String, homeworkId: String): HomeworkCompletion {
        val id = CommunityValidation.id(communityId)
        val hid = CommunityValidation.id(homeworkId)
        return read("GET", "/$id/homework/$hid/completion", null, accessToken, 200) { completion(it.obj()) }
    }

    suspend fun upsertCompletion(
        accessToken: String,
        communityId: String,
        homeworkId: String,
        completed: Boolean,
        expectedRevision: Long
    ): HomeworkCompletion {
        val id = CommunityValidation.id(communityId)
        val hid = CommunityValidation.id(homeworkId)
        val revision = CommunityValidation.revision(expectedRevision)
        val body = """{"completed":$completed,"expectedRevision":$revision}"""
        return read("PUT", "/$id/homework/$hid/completion", body, accessToken, 200) { completion(it.obj()) }
    }

    suspend fun listAnnouncements(accessToken: String, communityId: String): List<CommunityAnnouncement> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/announcements", null, accessToken, 200) { it.arr().items.map { row -> announcement(row.obj()) } }
    }

    suspend fun publishAnnouncement(
        accessToken: String,
        communityId: String,
        title: String,
        body: String,
        expectedRevision: Long
    ): CommunityAnnouncement {
        val id = CommunityValidation.id(communityId)
        return read("POST", "/$id/announcements", homeworkBody(title, body, expectedRevision), accessToken, 201) { announcement(it.obj()) }
    }

    suspend fun updateAnnouncement(
        accessToken: String,
        communityId: String,
        announcementId: String,
        title: String,
        body: String,
        expectedRevision: Long
    ): CommunityAnnouncement {
        val id = CommunityValidation.id(communityId)
        val aid = CommunityValidation.id(announcementId)
        return read("PUT", "/$id/announcements/$aid", homeworkBody(title, body, expectedRevision), accessToken, 200) { announcement(it.obj()) }
    }

    suspend fun listPolls(accessToken: String, communityId: String): List<CommunityPoll> {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/polls", null, accessToken, 200) { it.arr().items.map { row -> poll(row.obj()) } }
    }

    suspend fun publishPoll(
        accessToken: String,
        communityId: String,
        question: String,
        deadlineAt: Instant,
        options: List<String>,
        expectedRevision: Long
    ): CommunityPoll {
        val id = CommunityValidation.id(communityId)
        val body = buildString {
            append("{\"question\":").append(q(CommunityValidation.question(question)))
            append(",\"deadlineAt\":").append(q(CommunityUtc.format(deadlineAt)))
            append(",\"options\":[")
            CommunityValidation.options(options).forEachIndexed { i, option ->
                if (i > 0) append(',')
                append(q(option))
            }
            append("],\"expectedRevision\":").append(CommunityValidation.revision(expectedRevision))
            append('}')
        }
        return read("POST", "/$id/polls", body, accessToken, 201) { poll(it.obj()) }
    }

    suspend fun getPoll(accessToken: String, communityId: String, pollId: String): CommunityPoll {
        val id = CommunityValidation.id(communityId)
        val pid = CommunityValidation.id(pollId)
        return read("GET", "/$id/polls/$pid", null, accessToken, 200) { poll(it.obj()) }
    }

    suspend fun vote(accessToken: String, communityId: String, pollId: String, optionId: String): PollVote {
        val id = CommunityValidation.id(communityId)
        val pid = CommunityValidation.id(pollId)
        val oid = CommunityValidation.id(optionId)
        return read("POST", "/$id/polls/$pid/votes", """{"optionId":${q(oid)}}""", accessToken, 201) { readVote(it.obj()) }
    }

    suspend fun groupHome(accessToken: String, communityId: String): GroupHome {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/home", null, accessToken, 200) { groupHome(it.obj()) }
    }

    suspend fun topics(accessToken: String, communityId: String): GroupTopicList {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/topics?typed=1", null, accessToken, 200) { topicList(it.obj()) }
    }

    suspend fun desk(accessToken: String, communityId: String): GroupDesk {
        val id = CommunityValidation.id(communityId)
        return read("GET", "/$id/desk", null, accessToken, 200) { groupDesk(it.obj()) }
    }

    suspend fun createRole(accessToken: String, communityId: String, name: String): GroupDesk {
        val id = CommunityValidation.id(communityId)
        val clean = CommunityValidation.text(name.trim(), 32)
        if (clean.length < 2) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        return read("POST", "/$id/roles", """{"name":${q(clean)}}""", accessToken, 201) { groupDesk(it.obj()) }
    }

    suspend fun setRolePower(accessToken: String, communityId: String, roleId: String, enabled: Boolean): GroupDesk {
        val id = CommunityValidation.id(communityId)
        val role = CommunityValidation.id(roleId)
        return read("POST", "/$id/roles/$role/powers", """{"power":"channels","enabled":$enabled}""", accessToken, 200) { groupDesk(it.obj()) }
    }

    suspend fun grantRole(accessToken: String, communityId: String, roleId: String, userId: String): GroupDesk {
        val id = CommunityValidation.id(communityId)
        val role = CommunityValidation.id(roleId)
        val user = CommunityValidation.id(userId)
        return read("POST", "/$id/roles/$role/grants", """{"userId":${q(user)}}""", accessToken, 200) { groupDesk(it.obj()) }
    }

    suspend fun revokeRole(accessToken: String, communityId: String, roleId: String, userId: String): GroupDesk {
        val id = CommunityValidation.id(communityId)
        val role = CommunityValidation.id(roleId)
        val user = CommunityValidation.id(userId)
        return read("POST", "/$id/roles/$role/grants/$user/delete", null, accessToken, 200) { groupDesk(it.obj()) }
    }

    suspend fun createTopic(accessToken: String, communityId: String, title: String, icon: String, kind: String,
        description: String? = null, accent: String? = null, pinned: Boolean? = null, writePolicy: String? = null): GroupTopicList {
        val id = CommunityValidation.id(communityId)
        return read("POST", "/$id/topics?typed=1", topicBody(title, icon, kind, description, accent, pinned, writePolicy), accessToken, 201) { topicList(it.obj()) }
    }

    suspend fun renameTopic(accessToken: String, communityId: String, topicId: String, title: String, icon: String, kind: String,
        description: String, accent: String, pinned: Boolean, writePolicy: String): GroupTopicList {
        val id = CommunityValidation.id(communityId)
        val tid = CommunityValidation.id(topicId)
        return read("POST", "/$id/topics/$tid?typed=1", topicBody(title, icon, kind, description, accent, pinned, writePolicy), accessToken, 200) { topicList(it.obj()) }
    }

    suspend fun deleteTopic(accessToken: String, communityId: String, topicId: String): GroupTopicList {
        val id = CommunityValidation.id(communityId)
        val tid = CommunityValidation.id(topicId)
        return read("POST", "/$id/topics/$tid/delete?typed=1", null, accessToken, 200) { topicList(it.obj()) }
    }

    suspend fun ballots(accessToken: String, communityId: String, topicId: String? = null): BallotBoard {
        val id = CommunityValidation.id(communityId)
        val query = topicId?.let { "?topic=" + CommunityValidation.id(it) }.orEmpty()
        return read("GET", "/$id/ballots$query", null, accessToken, 200) { ballotBoard(it.obj()) }
    }

    suspend fun openHeadmanBallot(accessToken: String, communityId: String, question: String, options: List<String>, days: Int, topicId: String? = null): BallotBoard =
        draftBallot(accessToken, communityId, "headman", question, options, days, topicId)

    suspend fun proposeBallot(accessToken: String, communityId: String, question: String, options: List<String>, days: Int, topicId: String? = null): BallotBoard =
        draftBallot(accessToken, communityId, "collective", question, options, days, topicId)

    private suspend fun draftBallot(accessToken: String, communityId: String, route: String, question: String, options: List<String>, days: Int, topicId: String?): BallotBoard {
        val id = CommunityValidation.id(communityId)
        if (options.size !in 2..6 || days !in 1..14) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        val body = buildString {
            append("{\"question\":").append(q(CommunityValidation.question(question)))
            append(",\"options\":[")
            CommunityValidation.options(options).forEachIndexed { index, option ->
                if (index > 0) append(',')
                append(q(option))
            }
            append("],\"days\":").append(days)
            if (topicId != null) append(",\"topicId\":").append(q(CommunityValidation.id(topicId)))
            append('}')
        }
        return read("POST", "/$id/ballots/$route", body, accessToken, 201) { ballotBoard(it.obj()) }
    }

    suspend fun supportBallot(accessToken: String, communityId: String, ballotId: String): BallotBoard =
        ballotAction(accessToken, communityId, ballotId, "support", null)

    suspend fun voteBallot(accessToken: String, communityId: String, ballotId: String, optionId: String): BallotBoard =
        ballotAction(accessToken, communityId, ballotId, "votes", """{"optionId":${q(CommunityValidation.id(optionId))}}""")

    suspend fun closeBallot(accessToken: String, communityId: String, ballotId: String): BallotBoard =
        ballotAction(accessToken, communityId, ballotId, "close", null)

    private suspend fun ballotAction(accessToken: String, communityId: String, ballotId: String, route: String, body: String?): BallotBoard {
        val id = CommunityValidation.id(communityId)
        val bid = CommunityValidation.id(ballotId)
        return read("POST", "/$id/ballots/$bid/$route", body, accessToken, 200) { ballotBoard(it.obj()) }
    }

    suspend fun openDirect(accessToken: String, communityId: String, userId: String): Conversation {
        val community = CommunityValidation.id(communityId)
        val peer = CommunityValidation.id(userId)
        return read("POST", "/direct", """{"communityId":${q(community)},"userId":${q(peer)}}""", accessToken, 201) { conversation(it.obj()) }
    }

    suspend fun messages(accessToken: String, conversationId: String, before: String? = null, after: String? = null, topic: String? = null): ChatPage {
        val id = CommunityValidation.id(conversationId)
        val parts = buildList {
            if (topic != null) add("topic=" + if (topic == "general") topic else CommunityValidation.id(topic))
            if (before != null) add("before=" + CommunityValidation.id(before))
            if (after != null) add("after=" + CommunityValidation.id(after))
        }
        if (before != null && after != null) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        val query = if (parts.isEmpty()) "" else parts.joinToString("&", "?")
        return read("GET", "/conversations/$id/messages$query", null, accessToken, 200) { page(it.obj()) }
    }

    suspend fun sendMedia(accessToken: String, conversationId: String, kind: String, name: String, bytes: ByteArray, replyTo: String? = null, durationMs: Int? = null, topicId: String? = null): ChatMessage {
        if (kind !in setOf("image", "video", "file", "voice", "circle")) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        val maxBytes = if (kind == "voice") 2 * 1024 * 1024 else 8 * 1024 * 1024
        if (bytes.isEmpty() || bytes.size > maxBytes) throw CommunityClientException(CommunityClientFailure.PayloadTooLarge)
        if (kind in setOf("voice", "circle") && durationMs == null) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        if (durationMs != null && (kind !in setOf("voice", "circle") || durationMs !in 1..(if (kind == "voice") 180_000 else 60_000)))
            throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        val id = CommunityValidation.id(conversationId)
        val token = AccountValidation.token(accessToken, "za_")
        val headers = linkedMapOf(
            "Accept" to "application/json",
            "Authorization" to "Bearer $token",
            "Content-Type" to "application/octet-stream",
            "X-Zapara-Kind" to kind,
            "X-Zapara-Name" to URLEncoder.encode(name.ifBlank { when(kind) { "image" -> "Фото"; "video" -> "Видео"; "voice" -> "Голосовое сообщение"; "circle" -> "Кружок"; else -> "Документ" } }, StandardCharsets.UTF_8.name()).replace("+", "%20")
        )
        if (replyTo != null) headers["X-Zapara-Reply"] = CommunityValidation.id(replyTo)
        if (durationMs != null) headers["X-Zapara-Duration-Ms"] = durationMs.toString()
        if (topicId != null) headers["X-Zapara-Topic"] = CommunityValidation.id(topicId)
        val reply = exchange("POST", "/conversations/$id/media", headers, bytes, 201)
        return payload { message(reply.obj()) }
    }

    suspend fun downloadMedia(accessToken: String, conversationId: String, messageId: String): ByteArray {
        val id = CommunityValidation.id(conversationId)
        val target = CommunityValidation.id(messageId)
        val token = AccountValidation.token(accessToken, "za_")
        val path = "/conversations/$id/messages/$target/media"
        val headers = mapOf("Accept" to "application/octet-stream", "Authorization" to "Bearer $token")
        val limit = 8 * 1024 * 1024
        val reply = try {
            val version = if (legacyRoutes) 1 else 2
            val first = transport.exchange(HttpCall("GET", scope.baseUri.toString() + "api/v$version/communities" + path, headers, maxBytes = limit))
            val code = if (first.status == 404) runCatching { StrictJson.parse(first.body, 16).obj().text("code", 64) }.getOrNull() else null
            if (version == 2 && first.status == 404 && code != "not_found") {
                legacyRoutes = true
                transport.exchange(HttpCall("GET", scope.baseUri.toString() + "api/v1/communities" + path, headers, maxBytes = limit))
            } else first
        } catch (_: HttpBodyTooLargeException) {
            throw CommunityClientException(CommunityClientFailure.PayloadTooLarge)
        } catch (_: IOException) {
            throw CommunityClientException(CommunityClientFailure.Transport)
        }
        if (reply.status != 200) throw mapError(reply.status, reply.body)
        if (reply.headers.any { it.key.equals("Content-Encoding", true) && it.value.isNotBlank() }) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
        if (reply.body.size > limit) throw CommunityClientException(CommunityClientFailure.PayloadTooLarge)
        if (reply.body.isEmpty() || !reply.contentType?.substringBefore(';')?.trim().equals("application/octet-stream", true)) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
        return reply.body
    }

    suspend fun sendMessage(accessToken: String, conversationId: String, body: String, replyTo: String? = null): ChatMessage {
        val id = CommunityValidation.id(conversationId)
        val text = CommunityValidation.message(body)
        val reply = if (replyTo == null) "" else ""","replyTo":${q(CommunityValidation.id(replyTo))}"""
        return read("POST", "/conversations/$id/messages", """{"body":${q(text)}$reply}""", accessToken, 201) { message(it.obj()) }
    }

    suspend fun sendTopicMessage(accessToken: String, conversationId: String, body: String, topicId: String?, replyTo: String? = null): ChatMessage {
        val id = CommunityValidation.id(conversationId)
        val text = CommunityValidation.message(body)
        val topic = topicId?.let { q(CommunityValidation.id(it)) } ?: "null"
        val reply = replyTo?.let { ""","replyTo":${q(CommunityValidation.id(it))}""" }.orEmpty()
        return read("POST", "/conversations/$id/topic-messages", """{"body":${q(text)},"topicId":$topic$reply}""", accessToken, 201) { message(it.obj()) }
    }

    suspend fun editMessage(accessToken: String, conversationId: String, messageId: String, body: String): ChatMessage {
        val id = CommunityValidation.id(conversationId)
        val target = CommunityValidation.id(messageId)
        val text = CommunityValidation.message(body)
        return read("POST", "/conversations/$id/messages/$target/edit", """{"body":${q(text)}}""", accessToken, 200) { message(it.obj()) }
    }

    suspend fun deleteMessage(accessToken: String, conversationId: String, messageId: String): ChatMessage {
        val id = CommunityValidation.id(conversationId)
        val target = CommunityValidation.id(messageId)
        return read("POST", "/conversations/$id/messages/$target/delete", null, accessToken, 200) { message(it.obj()) }
    }

    suspend fun reactMessage(accessToken: String, conversationId: String, messageId: String, emoji: String): ChatMessage {
        val id = CommunityValidation.id(conversationId)
        val target = CommunityValidation.id(messageId)
        if (emoji !in setOf("like", "heart", "laugh", "wow", "sad")) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        return read("POST", "/conversations/$id/messages/$target/react", """{"emoji":${q(emoji)}}""", accessToken, 200) { message(it.obj()) }
    }

    suspend fun markRead(accessToken: String, conversationId: String): Conversation {
        val id = CommunityValidation.id(conversationId)
        return read("POST", "/conversations/$id/read", null, accessToken, 200) { conversation(it.obj()) }
    }

    suspend fun results(accessToken: String, communityId: String, pollId: String): PollResults {
        val id = CommunityValidation.id(communityId)
        val pid = CommunityValidation.id(pollId)
        return read("GET", "/$id/polls/$pid/results", null, accessToken, 200) { readResults(it.obj()) }
    }

    private suspend fun <T> read(
        method: String,
        path: String,
        body: String?,
        access: String,
        expected: Int,
        parse: (JsonValue) -> T
    ): T {
        val json = send(method, path, body, access, expected)
        return payload { parse(json) }
    }

    private suspend fun exchange(method: String, path: String, headers: Map<String, String>, bytes: ByteArray, expected: Int): JsonValue {
        val reply = try {
            val version = if (legacyRoutes) 1 else 2
            val first = transport.exchange(HttpCall(method, scope.baseUri.toString() + "api/v$version/communities" + path, headers, bytes, CommunityValidation.RequestBytes))
            val code = if (first.status == 404) runCatching { StrictJson.parse(first.body, 16).obj().text("code", 64) }.getOrNull() else null
            if (version == 2 && first.status == 404 && code != "not_found") {
                legacyRoutes = true
                transport.exchange(HttpCall(method, scope.baseUri.toString() + "api/v1/communities" + path, headers, bytes, CommunityValidation.RequestBytes))
            } else first
        } catch (_: HttpBodyTooLargeException) {
            throw CommunityClientException(CommunityClientFailure.BodyTooLarge)
        } catch (_: IOException) {
            throw CommunityClientException(CommunityClientFailure.Transport)
        }
        if (reply.headers.keys.any { it.equals("Content-Encoding", true) && reply.headers.getValue(it).isNotBlank() }) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
        if (reply.body.size > CommunityValidation.RequestBytes) throw CommunityClientException(CommunityClientFailure.BodyTooLarge)
        if (reply.status != expected) throw mapError(reply.status, reply.body)
        return try {
            StrictJson.parse(reply.body, 16)
        } catch (_: JsonFail) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
    }

    private suspend fun send(method: String, path: String, body: String?, access: String, expected: Int): JsonValue {
        val token = AccountValidation.token(access, "za_")
        val headers = linkedMapOf("Accept" to "application/json")
        headers["Authorization"] = "Bearer $token"
        val bytes = body?.toByteArray(Charsets.UTF_8)
        if (bytes != null) {
            if (bytes.size > CommunityValidation.RequestBytes) {
                throw CommunityClientException(CommunityClientFailure.InvalidRequest)
            }
            headers["Content-Type"] = "application/json"
        }
        val reply = try {
            val version = if (legacyRoutes) 1 else 2
            val first = transport.exchange(HttpCall(method, scope.baseUri.toString() + "api/v$version/communities" + path, headers, bytes, CommunityValidation.RequestBytes))
            val code = if (first.status == 404) runCatching { StrictJson.parse(first.body, 16).obj().text("code", 64) }.getOrNull() else null
            if (version == 2 && first.status == 404 && code != "not_found") {
                legacyRoutes = true
                transport.exchange(HttpCall(method, scope.baseUri.toString() + "api/v1/communities" + path, headers, bytes, CommunityValidation.RequestBytes))
            } else first
        } catch (_: HttpBodyTooLargeException) {
            throw CommunityClientException(CommunityClientFailure.BodyTooLarge)
        } catch (_: IOException) {
            throw CommunityClientException(CommunityClientFailure.Transport)
        }
        if (reply.headers.keys.any { it.equals("Content-Encoding", true) && reply.headers.getValue(it).isNotBlank() }) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
        if (reply.body.size > CommunityValidation.RequestBytes) {
            throw CommunityClientException(CommunityClientFailure.BodyTooLarge)
        }
        if (reply.status != expected) throw mapError(reply.status, reply.body)
        return try {
            StrictJson.parse(reply.body, 16)
        } catch (_: JsonFail) {
            throw CommunityClientException(CommunityClientFailure.InvalidPayload)
        }
    }

    private fun mapError(status: Int, body: ByteArray): CommunityClientException {
        val code = try {
            val root = StrictJson.parse(body, 16).obj()
            if (root.int("status") != status) null else root.text("code", 64)
        } catch (_: Exception) {
            null
        }
        val failure = when (status to code) {
            400 to "invalid_request", 413 to "invalid_request", 415 to "invalid_request" -> CommunityClientFailure.InvalidRequest
            413 to "payload_too_large" -> CommunityClientFailure.PayloadTooLarge
            401 to "invalid_session" -> CommunityClientFailure.InvalidSession
            403 to "forbidden" -> CommunityClientFailure.Forbidden
            404 to "not_found" -> CommunityClientFailure.NotFound
            409 to "revision_conflict" -> CommunityClientFailure.RevisionConflict
            409 to "already_voted" -> CommunityClientFailure.AlreadyVoted
            409 to "already_member" -> CommunityClientFailure.AlreadyMember
            409 to "already_requested" -> CommunityClientFailure.AlreadyRequested
            409 to "poll_closed" -> CommunityClientFailure.PollClosed
            429 to "rate_limited" -> CommunityClientFailure.RateLimited
            503 to "db_unavailable" -> CommunityClientFailure.DbUnavailable
            500 to "internal_error" -> CommunityClientFailure.InternalError
            else -> when (status) {
                401 -> CommunityClientFailure.InvalidSession
                403 -> CommunityClientFailure.Forbidden
                404 -> CommunityClientFailure.NotFound
                409 -> CommunityClientFailure.Conflict
                else -> CommunityClientFailure.ServerUnavailable
            }
        }
        return CommunityClientException(failure)
    }

    private fun groupHome(obj: JsonValue.Obj): GroupHome {
        obj.requireKeys("communityId", "name", "groupName", "groupChat", "classmates", "directs")
        val classmates = obj.array("classmates", 500).items.map { classmate(it.obj()) }
        val directs = obj.array("directs", 500).items.map { conversation(it.obj()) }
        return GroupHome(
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.name(obj.text("name", 80)),
            obj.nullableText("groupName", 80)?.let { CommunityValidation.name(it) },
            conversation(obj.field("groupChat").obj()),
            classmates,
            directs
        )
    }

    private fun topicList(obj: JsonValue.Obj): GroupTopicList {
        obj.requireKeys("topics", "canManageChannels")
        val rows = obj.array("topics", 25).items.map { topic(it.obj()) }
        if (rows.count { it.topicId == null } != 1 || rows.first().topicId != null || rows.first().kind != "chat") throw JsonFail()
        if (rows.mapNotNull { it.topicId }.distinct().size != rows.size - 1) throw JsonFail()
        return GroupTopicList(rows, obj.bool("canManageChannels"))
    }

    private fun groupDesk(obj: JsonValue.Obj): GroupDesk {
        obj.requireKeys("headman", "roles", "grants", "applicants", "powers", "mine")
        val roles = obj.array("roles", 100).items.map { item ->
            val row = item.obj()
            row.requireKeys("roleId", "name")
            GroupRole(CommunityValidation.id(row.text("roleId", 36)), row.text("name", 32, nonempty = true))
        }
        val grants = obj.array("grants", 500).items.map { item ->
            val row = item.obj()
            row.requireKeys("roleId", "userId")
            GroupGrant(CommunityValidation.id(row.text("roleId", 36)), CommunityValidation.id(row.text("userId", 36)))
        }
        obj.array("applicants", 500).items.forEach { item ->
            val row = item.obj()
            row.requireKeys("requestId", "userId", "username", "displayName")
            CommunityValidation.id(row.text("requestId", 36))
            CommunityValidation.id(row.text("userId", 36))
        }
        val powers = obj.array("powers", 500).items.map { item ->
            val row = item.obj()
            row.requireKeys("roleId", "power")
            GroupPower(CommunityValidation.id(row.text("roleId", 36)), row.text("power", 32, nonempty = true))
        }
        val mine = obj.array("mine", 100).items.map { item ->
            val value = (item as? JsonValue.Str)?.value ?: throw JsonFail()
            if (value.isBlank() || value.length > 32) throw JsonFail()
            value
        }
        return GroupDesk(obj.bool("headman"), roles, grants, powers, mine)
    }

    private fun topic(obj: JsonValue.Obj): GroupTopic {
        val metadata = listOf("description", "accent", "pinned", "writePolicy", "canPost")
        val modern = metadata.all { it in obj.fields }
        if (metadata.any { it in obj.fields } && !modern) throw JsonFail()
        if (modern) obj.requireKeys("topicId", "title", "icon", "kind", "lastBody", "lastAuthor", "lastAt", "unread", "canDelete", "activeBallots",
            "description", "accent", "pinned", "writePolicy", "canPost")
        else obj.requireKeys("topicId", "title", "icon", "kind", "lastBody", "lastAuthor", "lastAt", "unread", "canDelete", "activeBallots")
        val id = obj.nullableText("topicId", 36)?.let { CommunityValidation.id(it) }
        val kind = obj.text("kind", 16)
        if (kind != "chat" && kind != "ballots") throw JsonFail()
        val unread = obj.int("unread")
        val active = obj.int("activeBallots")
        if (unread < 0 || active < 0) throw JsonFail()
        val accent = if (modern) obj.text("accent", 16) else "default"
        val policy = if (modern) obj.text("writePolicy", 16) else "all"
        if (accent !in setOf("default", "blue", "green", "purple", "orange", "red") || policy !in setOf("all", "managers")) throw JsonFail()
        val lastAt = obj.nullableText("lastAt", 40)?.let { CommunityUtc.parse(it) }
        return GroupTopic(
            id, obj.text("title", 80, nonempty = true), obj.text("icon", 16), kind,
            obj.nullableText("lastBody", 2000), obj.nullableText("lastAuthor", 80), lastAt,
            unread, obj.bool("canDelete"), active,
            if (modern) obj.text("description", 240) else "", accent, if (modern) obj.bool("pinned") else false,
            policy, if (modern) obj.bool("canPost") else true
        )
    }

    private fun topicBody(title: String, icon: String, kind: String, description: String?, accent: String?, pinned: Boolean?, writePolicy: String?): String {
        if (kind !in setOf("chat", "ballots")) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        val cleanTitle = CommunityValidation.text(title.trim(), 40)
        val cleanIcon = CommunityValidation.text(icon.trim(), 8, allowEmpty = true)
        if (accent != null && accent !in setOf("default", "blue", "green", "purple", "orange", "red")) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        if (writePolicy != null && writePolicy !in setOf("all", "managers")) throw CommunityClientException(CommunityClientFailure.InvalidRequest)
        return buildString {
            append("{\"title\":").append(q(cleanTitle)).append(",\"icon\":").append(q(cleanIcon))
            append(",\"kind\":").append(q(kind))
            if (description != null) append(",\"description\":").append(q(CommunityValidation.text(description, 240, allowEmpty = true)))
            if (accent != null) append(",\"accent\":").append(q(accent))
            if (pinned != null) append(",\"pinned\":").append(pinned)
            if (writePolicy != null) append(",\"writePolicy\":").append(q(writePolicy))
            append('}')
        }
    }

    private fun ballotBoard(obj: JsonValue.Obj): BallotBoard {
        obj.requireKeys("headman", "canOpen", "canClose", "members", "supportersNeeded", "ballots")
        val members = obj.int("members")
        val needed = obj.int("supportersNeeded")
        if (members < 0 || needed < 1) throw JsonFail()
        return BallotBoard(obj.bool("headman"), obj.bool("canOpen"), obj.bool("canClose"), members, needed,
            obj.array("ballots", 500).items.map { ballot(it.obj()) })
    }

    private fun ballot(obj: JsonValue.Obj): Ballot {
        if ("topicId" in obj.fields) obj.requireKeys("ballotId", "question", "origin", "status", "deadlineAt", "supporters", "supportersNeeded", "supported", "options", "effect", "outcome", "topicId")
        else obj.requireKeys("ballotId", "question", "origin", "status", "deadlineAt", "supporters", "supportersNeeded", "supported", "options", "effect", "outcome")
        val status = obj.text("status", 16)
        if (status !in setOf("collecting", "open", "closed")) throw JsonFail()
        val supporters = obj.int("supporters")
        val needed = obj.int("supportersNeeded")
        if (supporters < 0 || needed < 1) throw JsonFail()
        return Ballot(
            CommunityValidation.id(obj.text("ballotId", 36)), CommunityValidation.question(obj.text("question", 800)),
            obj.text("origin", 32), status, CommunityUtc.parse(obj.text("deadlineAt", 40)),
            supporters, needed, obj.bool("supported"), obj.array("options", 6).items.map { ballotOption(it.obj()) },
            obj.text("effect", 80), obj.text("outcome", 80),
            if ("topicId" in obj.fields) obj.nullableText("topicId", 36)?.let { CommunityValidation.id(it) } else null
        )
    }

    private fun ballotOption(obj: JsonValue.Obj): BallotOption {
        obj.requireKeys("optionId", "label", "votes", "chosen")
        val votes = obj.int("votes")
        if (votes < 0) throw JsonFail()
        return BallotOption(CommunityValidation.id(obj.text("optionId", 36)), CommunityValidation.option(obj.text("label", 160)), votes, obj.bool("chosen"))
    }

    private fun classmate(obj: JsonValue.Obj): Classmate {
        obj.requireKeys("userId", "username", "displayName", "role", "self")
        return Classmate(
            CommunityValidation.id(obj.text("userId", 36)),
            obj.text("username", 32, nonempty = true),
            obj.nullableText("displayName", 80),
            CommunityValidation.role(obj.text("role", 16)),
            obj.bool("self")
        )
    }

    private fun conversation(obj: JsonValue.Obj): Conversation {
        obj.requireKeys("conversationId", "kind", "communityId", "title", "peerUserId", "lastBody", "lastAt", "unread")
        val kind = obj.text("kind", 16)
        if (kind != "direct" && kind != "group") throw JsonFail()
        val peer = obj.nullableText("peerUserId", 36)?.let { CommunityValidation.id(it) }
        if ((kind == "group") != (peer == null)) throw JsonFail()
        val unread = obj.int("unread")
        if (unread < 0) throw JsonFail()
        val lastBody = obj.nullableText("lastBody", 2000)
        val lastAt = if (obj.field("lastAt") is JsonValue.Null) null else CommunityUtc.parse(obj.text("lastAt", 40))
        if ((lastBody == null) != (lastAt == null)) throw JsonFail()
        return Conversation(
            CommunityValidation.id(obj.text("conversationId", 36)),
            kind,
            CommunityValidation.id(obj.text("communityId", 36)),
            obj.text("title", 80, nonempty = true),
            peer,
            lastBody,
            lastAt,
            unread
        )
    }

    private fun message(obj: JsonValue.Obj): ChatMessage {
        val kind = if ("kind" in obj.fields) obj.text("kind", 16) else "text"
        if (kind !in setOf("text", "image", "video", "file", "voice", "circle")) throw JsonFail()
        val deleted = if ("deleted" in obj.fields) obj.bool("deleted") else false
        val reply = if ("replyTo" in obj.fields && obj.field("replyTo") !is JsonValue.Null) CommunityValidation.id(obj.text("replyTo", 36)) else null
        val reactions = when (val value = obj.fields["reactions"]) {
            null -> emptyList()
            is JsonValue.Arr -> value.items.map { item ->
                val reaction = item.obj()
                val emoji = reaction.text("emoji", 16)
                val count = reaction.int("count")
                if (emoji !in setOf("like", "heart", "laugh", "wow", "sad") || count < 1) throw JsonFail()
                ChatReaction(emoji, count, reaction.bool("mine"))
            }
            else -> throw JsonFail()
        }
        return ChatMessage(
            CommunityValidation.id(obj.text("messageId", 36)),
            CommunityValidation.id(obj.text("conversationId", 36)),
            CommunityValidation.id(obj.text("senderId", 36)),
            obj.text("senderName", 80, nonempty = true),
            if (deleted) obj.text("body", 4000) else CommunityValidation.message(obj.text("body", 4000)),
            CommunityUtc.parse(obj.text("createdAt", 40)),
            kind,
            deleted,
            reply,
            reactions
        )
    }

    private fun page(obj: JsonValue.Obj): ChatPage {
        obj.requireKeys("messages", "hasMore")
        return ChatPage(obj.array("messages", 51).items.map { message(it.obj()) }, obj.bool("hasMore"))
    }

    private fun community(obj: JsonValue.Obj): Community {
        obj.requireKeys("communityId", "name", "description", "revision", "role")
        val revision = CommunityValidation.revision(obj.long("revision"))
        if (revision <= 0) throw JsonFail()
        val role = obj.field("role")
        return Community(
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.name(obj.text("name", 160)),
            CommunityValidation.description(obj.text("description", 4000)),
            revision,
            if (role is JsonValue.Null) null else CommunityValidation.role((role as? JsonValue.Str)?.value)
        )
    }

    private fun join(obj: JsonValue.Obj): JoinRequest {
        obj.requireKeys("requestId", "communityId", "userId", "status", "createdAt")
        return JoinRequest(
            CommunityValidation.id(obj.text("requestId", 36)),
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.id(obj.text("userId", 36)),
            CommunityValidation.joinStatus(obj.text("status", 16)),
            CommunityUtc.parse(obj.text("createdAt", 40))
        )
    }

    private fun member(obj: JsonValue.Obj): CommunityMember {
        obj.requireKeys("userId", "role")
        return CommunityMember(CommunityValidation.id(obj.text("userId", 36)), CommunityValidation.role(obj.text("role", 16)))
    }

    private fun homework(obj: JsonValue.Obj): CommunityHomework {
        obj.requireKeys("homeworkId", "communityId", "title", "body", "revision", "createdAt", "updatedAt")
        val revision = CommunityValidation.revision(obj.long("revision"))
        if (revision <= 0) throw JsonFail()
        return CommunityHomework(
            CommunityValidation.id(obj.text("homeworkId", 36)),
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.title(obj.text("title", 400)),
            CommunityValidation.body(obj.text("body", 16000)),
            revision,
            CommunityUtc.parse(obj.text("createdAt", 40)),
            CommunityUtc.parse(obj.text("updatedAt", 40))
        )
    }

    private fun completion(obj: JsonValue.Obj): HomeworkCompletion {
        obj.requireKeys("homeworkId", "completed", "revision", "updatedAt")
        val completed = obj.bool("completed")
        val revision = CommunityValidation.revision(obj.long("revision"))
        if (revision == 0L && completed) throw JsonFail()
        val updated = obj.field("updatedAt")
        return HomeworkCompletion(
            CommunityValidation.id(obj.text("homeworkId", 36)),
            completed,
            revision,
            if (updated is JsonValue.Null) null else CommunityUtc.parse((updated as? JsonValue.Str)?.value ?: throw JsonFail())
        )
    }

    private fun announcement(obj: JsonValue.Obj): CommunityAnnouncement {
        obj.requireKeys("announcementId", "communityId", "title", "body", "revision", "createdAt", "updatedAt")
        val revision = CommunityValidation.revision(obj.long("revision"))
        if (revision <= 0) throw JsonFail()
        return CommunityAnnouncement(
            CommunityValidation.id(obj.text("announcementId", 36)),
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.title(obj.text("title", 400)),
            CommunityValidation.body(obj.text("body", 16000)),
            revision,
            CommunityUtc.parse(obj.text("createdAt", 40)),
            CommunityUtc.parse(obj.text("updatedAt", 40))
        )
    }

    private fun poll(obj: JsonValue.Obj): CommunityPoll {
        obj.requireKeys("pollId", "communityId", "question", "deadlineAt", "revision", "options")
        val revision = CommunityValidation.revision(obj.long("revision"))
        if (revision <= 0) throw JsonFail()
        val options = obj.field("options").arr().items.map { pollOption(it.obj()) }
        if (options.size !in 2..16) throw JsonFail()
        return CommunityPoll(
            CommunityValidation.id(obj.text("pollId", 36)),
            CommunityValidation.id(obj.text("communityId", 36)),
            CommunityValidation.question(obj.text("question", 800)),
            CommunityUtc.parse(obj.text("deadlineAt", 40)),
            revision,
            options
        )
    }

    private fun pollOption(obj: JsonValue.Obj): PollOption {
        obj.requireKeys("optionId", "label", "ordinal")
        val ordinal = obj.int("ordinal")
        if (ordinal <= 0) throw JsonFail()
        return PollOption(
            CommunityValidation.id(obj.text("optionId", 36)),
            CommunityValidation.option(obj.text("label", 160)),
            ordinal
        )
    }

    private fun readVote(obj: JsonValue.Obj): PollVote {
        obj.requireKeys("pollId", "optionId", "createdAt")
        return PollVote(
            CommunityValidation.id(obj.text("pollId", 36)),
            CommunityValidation.id(obj.text("optionId", 36)),
            CommunityUtc.parse(obj.text("createdAt", 40))
        )
    }

    private fun readResults(obj: JsonValue.Obj): PollResults {
        obj.requireKeys("pollId", "totalVotes", "options")
        val total = obj.int("totalVotes")
        if (total < 0) throw JsonFail()
        val options = obj.field("options").arr().items.map { tally(it.obj()) }
        if (options.size !in 2..16) throw JsonFail()
        if (options.sumOf { it.votes } != total) throw JsonFail()
        return PollResults(CommunityValidation.id(obj.text("pollId", 36)), total, options)
    }

    private fun tally(obj: JsonValue.Obj): PollOptionTally {
        obj.requireKeys("optionId", "label", "votes")
        val votes = obj.int("votes")
        if (votes < 0) throw JsonFail()
        return PollOptionTally(
            CommunityValidation.id(obj.text("optionId", 36)),
            CommunityValidation.option(obj.text("label", 160)),
            votes
        )
    }

    private fun homeworkBody(title: String, body: String, expectedRevision: Long): String {
        return "{\"title\":${q(CommunityValidation.title(title))},\"body\":${q(CommunityValidation.body(body))},\"expectedRevision\":${CommunityValidation.revision(expectedRevision)}}"
    }

    private inline fun <T> payload(block: () -> T): T = try {
        block()
    } catch (e: CommunityClientException) {
        throw e
    } catch (_: JsonFail) {
        throw CommunityClientException(CommunityClientFailure.InvalidPayload)
    } catch (_: IllegalArgumentException) {
        throw CommunityClientException(CommunityClientFailure.InvalidPayload)
    }

    private fun q(value: String): String = buildString {
        append('"')
        for (c in value) when (c) {
            '\\' -> append("\\\\")
            '"' -> append("\\\"")
            '\n' -> append("\\n")
            '\r' -> append("\\r")
            '\t' -> append("\\t")
            else -> append(c)
        }
        append('"')
    }
}

private fun JsonValue.Obj.requireKeys(vararg names: String) {
    if (fields.size != names.size) throw JsonFail()
    for (name in names) if (name !in fields) throw JsonFail()
}

private fun JsonValue.Obj.long(name: String): Long {
    val n = field(name) as? JsonValue.Num ?: throw JsonFail()
    val v = n.raw.toLongOrNull() ?: throw JsonFail()
    if (n.raw != v.toString() && n.raw != "-0") throw JsonFail()
    return v
}
