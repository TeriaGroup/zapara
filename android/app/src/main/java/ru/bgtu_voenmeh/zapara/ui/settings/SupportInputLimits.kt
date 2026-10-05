package ru.bgtu_voenmeh.zapara.ui.settings

internal data class SupportInputStatus(
    val subjectScalars: Int,
    val bodyScalars: Int,
    val subjectWireUnits: Int,
    val bodyWireUnits: Int,
    val subjectOverLimit: Boolean,
    val bodyOverLimit: Boolean,
    val invalidUnicode: Boolean,
    val canSend: Boolean
)

internal object SupportInputLimits {
    private fun validUnicode(value: String): Boolean {
        var index = 0
        while (index < value.length) {
            val c = value[index]
            if (c.isHighSurrogate()) {
                if (index + 1 >= value.length || !value[index + 1].isLowSurrogate()) return false
                index += 2
            } else {
                if (c.isLowSurrogate()) return false
                index++
            }
        }
        return true
    }

    fun evaluate(subject: String, body: String, reply: Boolean): SupportInputStatus {
        val cleanSubject = subject.trim()
        val cleanBody = body.trim()
        val subjectScalars = cleanSubject.codePointCount(0, cleanSubject.length)
        val bodyScalars = cleanBody.codePointCount(0, cleanBody.length)
        val subjectOverLimit = cleanSubject.length > 120 || subjectScalars > 120
        val bodyOverLimit = cleanBody.length > 4000 || bodyScalars > 4000
        val invalidUnicode = !validUnicode(cleanSubject) || !validUnicode(cleanBody)
        return SupportInputStatus(subjectScalars, bodyScalars, cleanSubject.length, cleanBody.length,
            subjectOverLimit, bodyOverLimit,
            invalidUnicode, !invalidUnicode && !subjectOverLimit && !bodyOverLimit &&
                cleanBody.length >= 3 &&
                (reply || cleanSubject.length >= 3))
    }
}
