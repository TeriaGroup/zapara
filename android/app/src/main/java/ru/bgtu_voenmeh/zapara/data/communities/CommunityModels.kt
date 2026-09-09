package ru.bgtu_voenmeh.zapara.data.communities

import ru.bgtu_voenmeh.zapara.data.accounts.AccountValidation
import java.time.Instant
import java.time.ZoneOffset
import java.time.format.DateTimeFormatter

enum class CommunityClientFailure {
    InvalidRequest, InvalidSession, Forbidden, NotFound, Conflict,
    RevisionConflict, AlreadyVoted, AlreadyMember, AlreadyRequested, PollClosed,
    PayloadTooLarge, RateLimited, DbUnavailable, InternalError,
    InvalidPayload, BodyTooLarge, Transport, ServerUnavailable
}

class CommunityClientException(val failure: CommunityClientFailure) : Exception("Операция сообщества не выполнена.")

data class Community(
    val communityId: String,
    val name: String,
    val description: String,
    val revision: Long,
    val role: String?
)

data class JoinRequest(
    val requestId: String,
    val communityId: String,
    val userId: String,
    val status: String,
    val createdAt: Instant
)

data class CommunityMember(
    val userId: String,
    val role: String
)

data class CommunityHomework(
    val homeworkId: String,
    val communityId: String,
    val title: String,
    val body: String,
    val revision: Long,
    val createdAt: Instant,
    val updatedAt: Instant
)

data class HomeworkCompletion(
    val homeworkId: String,
    val completed: Boolean,
    val revision: Long,
    val updatedAt: Instant?
)

data class CommunityAnnouncement(
    val announcementId: String,
    val communityId: String,
    val title: String,
    val body: String,
    val revision: Long,
    val createdAt: Instant,
    val updatedAt: Instant
)

data class PollOption(
    val optionId: String,
    val label: String,
    val ordinal: Int
)

data class CommunityPoll(
    val pollId: String,
    val communityId: String,
    val question: String,
    val deadlineAt: Instant,
    val revision: Long,
    val options: List<PollOption>
)

data class PollVote(
    val pollId: String,
    val optionId: String,
    val createdAt: Instant
)

data class PollOptionTally(
    val optionId: String,
    val label: String,
    val votes: Int
)

data class PollResults(
    val pollId: String,
    val totalVotes: Int,
    val options: List<PollOptionTally>
)

object CommunityValidation {
    const val RequestBytes = 64 * 1024

    fun invalid(): IllegalArgumentException = IllegalArgumentException("Недопустимый контракт сообщества.")

    fun id(value: String): String = try {
        AccountValidation.id(value)
    } catch (_: IllegalArgumentException) {
        throw invalid()
    }

    fun revision(value: Long): Long = if (value >= 0) value else throw invalid()

    fun role(value: String?): String =
        if (value == "member" || value == "headman" || value == "curator") value else throw invalid()

    fun joinStatus(value: String?): String =
        if (value == "pending" || value == "accepted" || value == "rejected") value else throw invalid()

    fun groupId(value: String?): String = text(value, 64)

    fun name(value: String?): String = text(value, 80)

    fun description(value: String?): String = if (value == null) "" else text(value, 2000, true)

    fun title(value: String?): String = text(value, 200)

    fun body(value: String?): String = text(value, 8000)

    fun question(value: String?): String = text(value, 400)

    fun option(value: String?): String = text(value, 80)

    fun options(values: List<String>?): List<String> {
        if (values == null || values.size !in 2..16) throw invalid()
        val unique = HashSet<String>()
        return values.map { item ->
            val option = option(item)
            if (!unique.add(option)) throw invalid()
            option
        }
    }

    fun text(value: String?, maximum: Int, allowEmpty: Boolean = false): String {
        if (value == null || value.length > maximum * 2) throw invalid()
        var count = 0
        var i = 0
        while (i < value.length) {
            val cp = value.codePointAt(i)
            if (cp == 0 || cp in 0xD800..0xDFFF || Character.getType(cp) == Character.CONTROL.toInt()) throw invalid()
            count++
            i += Character.charCount(cp)
        }
        if (count > maximum || (count == 0 && !allowEmpty)) throw invalid()
        return value
    }
}

internal object CommunityUtc {
    private val head: DateTimeFormatter =
        DateTimeFormatter.ofPattern("yyyy-MM-dd'T'HH:mm:ss").withZone(ZoneOffset.UTC)
    private val shape = Regex("^([0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2})(\\.[0-9]{1,7})?Z$")

    fun format(value: Instant): String {
        val nano = (value.nano / 100) * 100
        val instant = Instant.ofEpochSecond(value.epochSecond, nano.toLong())
        val prefix = head.format(instant)
        val ticks = nano / 100
        if (ticks == 0) return prefix + "Z"
        return prefix + "." + ticks.toString().padStart(7, '0').trimEnd('0') + "Z"
    }

    fun parse(text: String): Instant {
        if (!shape.matches(text)) throw CommunityValidation.invalid()
        val instant = try {
            Instant.parse(text)
        } catch (_: Exception) {
            throw CommunityValidation.invalid()
        }
        if (format(instant) != text) throw CommunityValidation.invalid()
        return instant
    }
}
