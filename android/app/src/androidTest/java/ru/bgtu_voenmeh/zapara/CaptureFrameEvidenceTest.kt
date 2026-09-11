package ru.bgtu_voenmeh.zapara

import org.json.JSONObject
import org.junit.Assert.*
import org.junit.Test

class CaptureFrameEvidenceTest {
    @Test fun failedObservationStillWritesFailedMetadataBeforeTeardown() {
        val original = AssertionError("injected observation")
        val events = mutableListOf<String>()
        var saved: JSONObject? = null
        var actual: Throwable? = null
        val file = java.io.File.createTempFile("batch4-failed-", ".json",
            androidx.test.platform.app.InstrumentationRegistry.getInstrumentation().targetContext.cacheDir)
        try {
            captureFrameEvidence(JSONObject(), { throw original },
                { events += "png" }, { events += "xml" }, { file.writeText(it.toString()); events += "metadata" })
        } catch (error: Throwable) { actual = error }
        finally {
            saved = JSONObject(file.readText())
            check(file.delete())
            events += "teardown"
        }
        assertSame(original, actual)
        assertEquals(listOf("png", "xml", "metadata", "teardown"), events)
        assertEquals("failed", saved!!.getString("status"))
        assertEquals("diagnostic-only", saved!!.getString("scope"))
        assertFalse(saved!!.getBoolean("accepted"))
    }

    @Test fun independentCaptureErrorsAreRetainedWithOriginal() {
        val original = AssertionError("original")
        var saved: JSONObject? = null
        val actual = runCatching {
            captureFrameEvidence(JSONObject(), { throw original },
                { error("png failure") }, { error("xml failure") }, { saved = it; error("metadata failure") })
        }.exceptionOrNull()
        assertSame(original, actual)
        assertEquals(listOf("png failure", "xml failure", "metadata failure"), original.suppressed.map { it.message })
        assertEquals("failed", saved!!.getString("status"))
        assertEquals(3, saved!!.getJSONArray("captureErrors").length())
    }

    @Test fun captureOnlyFailureCannotCountAsSuccess() {
        var saved: JSONObject? = null
        val actual = runCatching {
            captureFrameEvidence(JSONObject(), {}, { error("capture") }, {}, { saved = it })
        }.exceptionOrNull()
        assertEquals("capture", actual!!.message)
        assertEquals("failed", saved!!.getString("status"))
        assertFalse(saved!!.getBoolean("accepted"))
    }
}
