package ru.bgtu_voenmeh.zapara.ui.groups

import ru.bgtu_voenmeh.zapara.data.communities.*
import java.time.Instant
import kotlinx.coroutines.CancellationException

data class GroupObligation(val id: String, val topicId: String?, val kind: String, val title: String,
    val deadline: Instant?, val needsMe: Boolean)
data class GroupObligations(val rows: List<GroupObligation>, val loaded: Int, val requested: Int, val failed: Int,
    val skipped: Int, val unread: List<GroupTopic>, val completionUnknown: Int = 0)

internal fun obligationAuthority(topics: List<GroupTopic>) = topics.map { topic ->
    listOf(topic.topicId, topic.kind, topic.archived, topic.supported, topic.permissions.sorted(), topic.revision)
}.sortedBy { it.first().toString() }

internal suspend fun collectGroupObligations(api: CommunityHttpClient, token: String, community: String,
    topics: List<GroupTopic>, current: () -> Boolean): GroupObligations? {
    val readable = topics.filter { !it.archived && it.supported && "read" in it.permissions }
    val candidates = readable.filter { it.topicId != null && it.kind in setOf("forms", "ballots", "homework") }.take(24)
    val rows = mutableListOf<GroupObligation>()
    var loaded = 0
    var failed = 0
    var completionUnknown = 0
    var homework: List<CommunityHomework> = emptyList()
    var copies: Map<String, GroupHomeworkCopy> = emptyMap()
    suspend fun request(work: suspend () -> Unit) {
        if (!current()) return
        try { work() }
        catch (cancel: CancellationException) { throw cancel }
        catch (error: CommunityClientException) {
            if (error.failure in setOf(CommunityClientFailure.InvalidSession, CommunityClientFailure.Forbidden, CommunityClientFailure.NotFound)) throw error
            failed++
        }
        catch (_: Exception) { failed++ }
    }
    if (candidates.any { it.kind == "homework" }) {
        request { homework = api.listHomework(token, community) }
        if (!current()) return null
        request { copies = api.listHomeworkCopies(token, community).associateBy { it.homeworkId } }
        if (!current()) return null
    }
    request {
        val board = api.ballots(token, community)
        if (current()) {
            rows += board.ballots.filter { it.topicId == null }.map { ballot -> GroupObligation(ballot.ballotId,
                null, "ballots", ballot.question, ballot.deadlineAt, ballot.status == "open" && ballot.options.none { it.chosen }) }
            loaded++
        }
    }
    if (!current()) return null
    for (topic in candidates) {
        if (!current()) return null
        val id = topic.topicId!!
        try {
            when (topic.kind) {
                "forms" -> rows += api.forms(token, community, id).map { form -> GroupObligation(form.formId,
                    id, "forms", form.title, form.deadlineAt, form.canRespond && form.ownResponse == null) }
                "ballots" -> rows += api.ballots(token, community, id).ballots.map { ballot -> GroupObligation(ballot.ballotId,
                    id, "ballots", ballot.question, ballot.deadlineAt,
                    ballot.status == "open" && "vote" in topic.permissions && ballot.options.none { it.chosen }) }
                "homework" -> {
                    rows += homework.filter { it.topicId == id }.map { task ->
                        val done = copies[task.homeworkId]
                        if (task.canComplete && done == null) completionUnknown++
                        GroupObligation(task.homeworkId, id, "homework", task.title,
                            task.deadlineAt, task.canComplete && done?.canComplete == true && !done.completed)
                    }
                }
            }
            if (!current()) return null
            loaded++
        } catch (cancel: CancellationException) { throw cancel }
        catch (error: CommunityClientException) {
            if (error.failure in setOf(CommunityClientFailure.InvalidSession, CommunityClientFailure.Forbidden, CommunityClientFailure.NotFound)) throw error
            failed++
        }
        catch (_: Exception) { failed++ }
    }
    if (!current()) return null
    return GroupObligations(rows.distinctBy { it.kind to it.id }.sortedWith(compareBy<GroupObligation> { it.deadline == null }
        .thenBy { it.deadline }.thenBy { it.title }), loaded, candidates.size + 1, failed,
        topics.size - candidates.size, readable.filter { it.unread > 0 }, completionUnknown)
}

internal fun browseObligations(rows: List<GroupObligation>, needsMe: Boolean, now: Instant): List<GroupObligation> =
    rows.filter { !needsMe || it.needsMe && (it.kind == "homework" || it.deadline == null || it.deadline.isAfter(now)) }
