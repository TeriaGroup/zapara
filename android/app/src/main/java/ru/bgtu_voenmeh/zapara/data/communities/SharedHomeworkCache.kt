package ru.bgtu_voenmeh.zapara.data.communities

import kotlinx.coroutines.CancellationException

data class SharedHomeworkSnapshot(val groupId: String, val communityId: String, val rows: List<CommunityHomework>, val completions: Map<String, HomeworkCompletion>)
data class SharedHomeworkRefresh(val snapshot: SharedHomeworkSnapshot?, val revoked: Boolean, val applied: Boolean = true)

class SharedHomeworkCache {
    var snapshot: SharedHomeworkSnapshot? = null
        private set
    private var serial = 0L
    private var activeGroup: String? = null
    private var activeToken: String? = null
    fun clear() { serial++; snapshot = null; activeGroup=null; activeToken=null }
    fun invalidate(groupId: String, communityId: String, token: String?) {
        if (activeGroup==groupId && activeToken==token && snapshot?.communityId==communityId) clear()
    }
    suspend fun refresh(api: CommunityHttpClient, token: String?, groupId: String): SharedHomeworkRefresh {
        val ticket = ++serial
        activeGroup=groupId; activeToken=token
        if (snapshot?.groupId != groupId) snapshot = null
        fun finish(next: SharedHomeworkSnapshot?, revoked: Boolean): SharedHomeworkRefresh {
            if (revoked && activeGroup==groupId && activeToken==token) {
                serial++; snapshot=null
                return SharedHomeworkRefresh(null,true)
            }
            if (ticket != serial) return SharedHomeworkRefresh(null, false, applied = false)
            snapshot = next
            return SharedHomeworkRefresh(next, revoked)
        }
        if (token == null) return finish(null, true)
        try {
            val community = api.list(token, groupId).firstOrNull { !it.role.isNullOrBlank() } ?: return finish(null, true)
            val rows = api.listHomework(token, community.communityId)
            val done = api.homeworkCompletions(token, community.communityId, rows)
            return finish(SharedHomeworkSnapshot(groupId, community.communityId, rows, done), false)
        } catch (e: CancellationException) { throw e }
        catch (e: CommunityClientException) {
            if (e.failure in setOf(CommunityClientFailure.InvalidSession, CommunityClientFailure.Forbidden, CommunityClientFailure.NotFound)) return finish(null, true)
        } catch (_: Exception) { }
        return finish(snapshot, false)
    }
}
