package ru.bgtu_voenmeh.zapara.ui.schedule

import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.update
import ru.bgtu_voenmeh.zapara.data.communities.CommunityHomework
import ru.bgtu_voenmeh.zapara.data.communities.HomeworkCompletion
import ru.bgtu_voenmeh.zapara.data.communities.CommunityClientFailure
import ru.bgtu_voenmeh.zapara.data.profiles.ProfileDescriptor
import java.time.LocalDate

/** The application seam for IO-built pages and private-source lifecycle. Called on Main. */
internal class ScheduleProjectionController(private val state: MutableStateFlow<ScheduleUiState>) {
    data class CompletionScope(val profile: ProfileDescriptor, val groupId: String?, val communityId: String?)
    fun completionDenied(operation: CompletionScope, current: CompletionScope, failure: CommunityClientFailure, invalidateCache: ()->Unit, purgeCurrent: ()->Unit): Boolean {
        if (failure !in setOf(CommunityClientFailure.InvalidSession,CommunityClientFailure.Forbidden,CommunityClientFailure.NotFound)) return false
        invalidateCache()
        // Epoch is deliberately absent: a later snapshot in the SAME scope does
        // not cancel authoritative denial. InvalidSession applies to the account.
        val applies = if(failure==CommunityClientFailure.InvalidSession) operation.profile==current.profile else operation==current
        if(applies) purgeCurrent()
        return applies
    }
    data class Shared(val epoch: Long, val communityId: String?, val rows: List<CommunityHomework>, val completions: Map<String, HomeworkCompletion>)
    var shared = Shared(0,null,emptyList(),emptyMap())
        private set
    fun isCurrent(snapshot: Shared): Boolean = shared.epoch == snapshot.epoch
    fun assign(communityId: String?, rows: List<CommunityHomework>, completions: Map<String, HomeworkCompletion>): Shared {
        shared=Shared(shared.epoch+1,communityId,rows.toList(),completions.toMap())
        return shared
    }
    fun purge() {
        assign(null,emptyList(),emptyMap())
        state.update(ScheduleComposer::purgeShared)
    }
    fun complete(completion: HomeworkCompletion): Shared = assign(shared.communityId,shared.rows,shared.completions+(completion.homeworkId to completion))
    suspend fun ensure(date: LocalDate, force: Boolean, contextCurrent: ()->Boolean, compose: suspend (Shared)->DayPage) {
        if(!force && state.value.pages[date]!=null) return
        while(true) {
            val snapshot=shared
            val page=compose(snapshot)
            if(!contextCurrent()) return
            // A purge, assignment or completion can occur while IO builds the page.
            // Retry the actual projection with current immutable data rather than leaving it unloaded.
            if(!isCurrent(snapshot)) continue
            state.update { it.copy(pages=it.pages+(date to page)) }
            return
        }
    }
}
