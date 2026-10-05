package ru.bgtu_voenmeh.zapara.data

// Wire shape must stay compatible with Windows SyncPayload (v1).

data class SyncPayload(
    val Version: Int = 1,
    val ExportedAt: String, // ISO-8601
    val Overrides: List<OverridePayload> = emptyList(),
    val Homework: List<HomeworkPayload> = emptyList(),
    val Friends: List<FriendPayload> = emptyList(),
    val Settings: SettingsPayload = SettingsPayload()
)

data class OverridePayload(
    val SubjectRawNormalized: String,
    val Scope: String,
    val DisplayName: String,
    val Note: String? = null,
    val CreatedAt: String
)

data class HomeworkPayload(
    val SubjectRawNormalized: String,
    val Text: String,
    val CreatedAt: String,
    val TargetNthOccurrence: Int,
    val DueDateComputed: String? = null,
    val Status: String = "pending",
    val DoneAt: String? = null
)

data class FriendPayload(
    val GroupName: String,
    val ColorHex: String,
    val Enabled: Boolean = true,
    val MemberNames: String = ""
)

data class SettingsPayload(
    val MyGroupId: String? = null,
    val ParityInvert: Boolean = false,
    val NotifyTime1: String? = null,
    val NotifyTime2: String? = null,
    val IntersectionStrictness: Int = 25,
    val Language: String = "ru",
    val LastSyncAt: String? = null,
    val LastFetchedAt: String? = null,
    val LastAutoCheckAt: String? = null,
    val WeekCount: Int = 2,
    val PeriodTitle: String? = null,
    val PeriodStart: String? = null,
    val MapPanelWidth: Int = 300,
    val AlwaysShowAllTrafficLights: Boolean = false
)
