package ru.bgtu_voenmeh.zapara.data.api

internal const val PIN = "11111111-1111-4111-8111-111111111111"
internal const val NEW_PIN = "22222222-2222-4222-8222-222222222222"
internal val SHA = "a".repeat(64)

internal fun envelope(pin: String = PIN): String = """
{"period":{"start":"2026-09-01","weekCount":2,"title":"Тестовый семестр","timeZone":"Europe/Moscow"},
 "meta":{"snapshotId":"$pin","fetchedAt":"2026-09-08T10:00:00+00:00",
 "publishedAt":"2026-09-08T10:01:00Z","sourceModifiedAt":null,"sourceKind":"file",
 "sourceUrl":null,"sourceSha256":"$SHA","stale":false},
 "refresh":{"lastAttemptId":null,"lastAttemptStatus":null,"lastSuccessAt":null,
 "lastFailureAt":null,"lastFailureCode":null,"abandoned":false}}
""".trimIndent()

internal fun catalogJson(pin: String = PIN, vararg ids: String): String {
    val wanted = if (ids.isEmpty()) arrayOf("a", "empty") else ids
    val groups = wanted.joinToString(",") { id ->
        val count = if (id == "empty") 0 else 1
        """{"id":"$id","name":"ТЕСТ-ГРУППА","lessonCount":$count}"""
    }
    return envelope(pin).dropLast(1) + ""","groups":[$groups]}"""
}

internal fun scheduleJson(id: String = "a", pin: String = PIN): String {
    val groupCount = if (id == "empty") 0 else 1
    val lessons = if (id == "empty") "[]" else """
[{"dayOfWeek":1,"parity":0,"index":1,"timeStart":"09:00","timeEnd":"10:35",
  "subjectRaw":"ЛЕК Ёлка  Тест","subjectNormalized":"лек елка тест","typeRaw":"лек",
  "teacherRaw":"Тестовый преподаватель","classroomRaw":"493*","roomRaw":"493","buildingRaw":"*"}]
""".trimIndent()
    return envelope(pin).dropLast(1) +
        ""","group":{"id":"$id","name":"ТЕСТ-ГРУППА","lessonCount":$groupCount},"lessons":$lessons}"""
}

internal fun missingPin(): HttpReply = HttpReply(
    404,
    """{"title":"Снимок не найден","status":404,"code":"snapshot_not_found"}""".toByteArray()
)

internal fun jsonReply(body: String): HttpReply = HttpReply(200, body.toByteArray())

internal class FakeHttp(var handler: suspend (HttpCall) -> HttpReply) : HttpExchange {
    val requests = mutableListOf<HttpCall>()
    override suspend fun exchange(call: HttpCall): HttpReply {
        synchronized(requests) { requests += call }
        return handler(call)
    }
}
