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
import ru.bgtu_voenmeh.zapara.data.api.bool
import ru.bgtu_voenmeh.zapara.data.api.field
import ru.bgtu_voenmeh.zapara.data.api.int
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
    suspend fun list(accessToken: String, groupId: String? = null): List<Community> {
        val path = if (groupId == null) ""
        else "?groupId=" + URLEncoder.encode(CommunityValidation.groupId(groupId), StandardCharsets.UTF_8).replace("+", "%20")
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
            transport.exchange(
                HttpCall(method, scope.baseUri.toString() + "api/v1/communities" + path, headers, bytes, CommunityValidation.RequestBytes)
            )
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
