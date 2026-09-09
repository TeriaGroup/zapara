package ru.bgtu_voenmeh.zapara.data.sync

import java.time.Instant
import java.time.LocalDate
import java.util.UUID

sealed class SyncValue {
    override fun toString(): String = "SyncValue { [REDACTED] }"
}

data class HomeworkValue(
    val subjectRaw: String,
    val subjectKey: String,
    val text: String,
    val targetNthOccurrence: Int,
    val createdAtUtc: Instant,
    val legacyCreatedLocalDate: LocalDate?
) : SyncValue() {
    init {
        SyncValidation.text(subjectRaw, 256)
        SyncValidation.subjectKey(subjectRaw, subjectKey)
        SyncValidation.text(text, 4000)
        SyncValidation.range(targetNthOccurrence, 1, 10)
    }
    override fun toString(): String = "HomeworkValue { [REDACTED] }"
}

data class CompletionValue(
    val done: Boolean,
    val doneAtUtc: Instant?
) : SyncValue() {
    override fun toString(): String = "CompletionValue { [REDACTED] }"
}

data class OverrideValue(
    val subjectRaw: String,
    val subjectKey: String,
    val scope: String,
    val displayName: String,
    val note: String?,
    val createdAtUtc: Instant
) : SyncValue() {
    init {
        SyncValidation.text(subjectRaw, 256)
        SyncValidation.subjectKey(subjectRaw, subjectKey)
        SyncValidation.scope(scope)
        SyncValidation.text(displayName, 256)
        SyncValidation.optional(note, 4000)
    }
    override fun toString(): String = "OverrideValue { [REDACTED] }"
}

data class FriendValue(
    val groupId: String?,
    val groupName: String,
    val memberNames: String,
    val paletteIndex: Int,
    val enabled: Boolean
) : SyncValue() {
    init {
        SyncValidation.optional(groupId, 64)
        SyncValidation.text(groupName, 256)
        SyncValidation.text(memberNames, 4000)
        SyncValidation.range(paletteIndex, 1, 5)
    }
    override fun toString(): String = "FriendValue { [REDACTED] }"
}

data class SettingsValue(
    val selectedGroupId: String?,
    val parityInvert: Boolean,
    val notifyTime1: String?,
    val notifyTime2: String?,
    val strictness: Int,
    val alwaysShow: Boolean
) : SyncValue() {
    init {
        SyncValidation.optional(selectedGroupId, 64)
        SyncValidation.time(notifyTime1)
        SyncValidation.time(notifyTime2)
        SyncValidation.range(strictness, 0, 100)
    }
    override fun toString(): String = "SettingsValue { [REDACTED] }"
}

data class SyncMutation(
    val syncEpoch: UUID,
    val opId: UUID,
    val entityType: String,
    val entityId: UUID,
    val expectedRevision: Long,
    val action: String,
    val value: SyncValue?
) {
    init {
        SyncValidation.id(syncEpoch)
        SyncValidation.id(opId)
        SyncValidation.identity(entityType, entityId)
        SyncValidation.nonnegative(expectedRevision)
        if (action != "upsert" && action != "delete") SyncValidation.invalid()
        if (action == "delete" && expectedRevision == 0L) SyncValidation.invalid()
        SyncValidation.value(entityType, value, action == "delete")
    }
    override fun toString(): String = "SyncMutation { [REDACTED] }"
}

data class SyncRecord(
    val entityType: String,
    val entityId: UUID,
    val revision: Long,
    val tombstone: Boolean,
    val changedAt: Instant,
    val value: SyncValue?
) {
    init {
        SyncValidation.identity(entityType, entityId)
        if (revision <= 0) SyncValidation.invalid()
        SyncValidation.value(entityType, value, tombstone)
        if (SyncJson.serialize(this).size > SyncValidation.RECORD_BYTES) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncRecord { [REDACTED] }"
}

data class SyncMetadata(
    val syncEpoch: UUID,
    val currentSequence: Long,
    val minAfterSequence: Long
) {
    init {
        SyncValidation.id(syncEpoch)
        SyncValidation.nonnegative(currentSequence)
        if (minAfterSequence < 0 || minAfterSequence > currentSequence) SyncValidation.invalid()
    }
}

data class SyncMutationResult(
    val status: Int,
    val code: String,
    val metadata: SyncMetadata,
    val serverRecord: SyncRecord?
) {
    init {
        val valid = when (status to code) {
            200 to "applied" -> serverRecord != null
            409 to "revision_conflict" -> true
            409 to "creation_metadata_immutable" -> serverRecord != null && !serverRecord.tombstone
            409 to "op_id_reused", 409 to "friend_limit", 410 to "sync_reset" -> serverRecord == null
            else -> false
        }
        if (!valid) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncMutationResult { [REDACTED] }"
}

data class SyncChange(
    val sequence: Long,
    val opId: UUID,
    val record: SyncRecord
) {
    init {
        if (sequence <= 0) SyncValidation.invalid()
        SyncValidation.id(opId)
        if (record.revision > sequence) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncChange { [REDACTED] }"
}

data class SyncChangesPage(
    val metadata: SyncMetadata,
    val afterSequence: Long,
    val nextAfterSequence: Long,
    val hasMore: Boolean,
    val changes: List<SyncChange>
) {
    init {
        if (changes.size > SyncValidation.PAGE_RECORDS ||
            afterSequence < metadata.minAfterSequence ||
            afterSequence > metadata.currentSequence
        ) SyncValidation.invalid()
        var previous = afterSequence
        for (change in changes) {
            if (change.sequence <= previous || change.sequence > metadata.currentSequence) SyncValidation.invalid()
            previous = change.sequence
        }
        if (nextAfterSequence != previous ||
            (hasMore && (changes.isEmpty() || previous >= metadata.currentSequence))
        ) SyncValidation.invalid()
        if (SyncJson.serialize(this).size > SyncValidation.PAGE_BYTES) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncChangesPage { [REDACTED] }"
}

data class SyncResyncManifest(
    val manifestId: UUID,
    val syncEpoch: UUID,
    val highWater: Long,
    val createdAt: Instant,
    val expiresAt: Instant,
    val itemCount: Long
) {
    init {
        SyncValidation.id(manifestId)
        SyncValidation.id(syncEpoch)
        SyncValidation.nonnegative(highWater)
        SyncValidation.nonnegative(itemCount)
        if (expiresAt != createdAt.plusSeconds(600)) SyncValidation.invalid()
    }
}

data class SyncManifestItem(
    val ordinal: Long,
    val record: SyncRecord
) {
    init {
        if (ordinal <= 0) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncManifestItem { [REDACTED] }"
}

data class SyncResyncPage(
    val manifest: SyncResyncManifest,
    val afterOrdinal: Long,
    val nextAfterOrdinal: Long,
    val hasMore: Boolean,
    val items: List<SyncManifestItem>
) {
    init {
        if (items.size > SyncValidation.PAGE_RECORDS || afterOrdinal < 0 || afterOrdinal > manifest.itemCount) {
            SyncValidation.invalid()
        }
        var previous = afterOrdinal
        for (item in items) {
            if (previous == Long.MAX_VALUE || item.ordinal != previous + 1 ||
                item.ordinal > manifest.itemCount || item.record.revision > manifest.highWater
            ) SyncValidation.invalid()
            previous = item.ordinal
        }
        if (nextAfterOrdinal != previous || hasMore != (previous < manifest.itemCount) || (hasMore && items.isEmpty())) {
            SyncValidation.invalid()
        }
        if (SyncJson.serialize(this).size > SyncValidation.PAGE_BYTES) SyncValidation.invalid()
    }
    override fun toString(): String = "SyncResyncPage { [REDACTED] }"
}

data class SyncError(
    val status: Int,
    val code: String
) {
    init {
        val valid = when (status to code) {
            400 to "invalid_request", 400 to "invalid_cursor",
            401 to "invalid_session",
            410 to "sync_reset", 410 to "manifest_expired",
            413 to "payload_too_large",
            429 to "rate_limited",
            503 to "db_unavailable" -> true
            else -> false
        }
        if (!valid) SyncValidation.invalid()
    }
}
