package ru.bgtu_voenmeh.zapara

import org.json.JSONArray
import org.json.JSONObject

/** Runs inside the live host's use scope. Capture failures never replace the body failure. */
internal fun captureFrameEvidence(metadata: JSONObject, observe: () -> Unit,
    png: () -> Unit, xml: () -> Unit, writeMetadata: (JSONObject) -> Unit) {
    var failure: Throwable? = null
    val errors = JSONArray()
    metadata.put("status", "pending").put("accepted", false)
    fun attempt(kind: String, action: () -> Unit) {
        try { action() } catch (error: Throwable) {
            errors.put(JSONObject().put("stage", kind).put("error", error.stackTraceToString()))
            if (failure == null) failure = error else failure!!.addSuppressed(error)
        }
    }
    try { observe() } catch (error: Throwable) {
        failure = error
        metadata.put("primaryError", error.stackTraceToString())
    } finally {
        attempt("png", png)
        attempt("xml", xml)
        metadata.put("status", if (failure == null) "validated-frame" else "failed")
            .put("scope", if (failure == null) "frame-only; runner/teardown acceptance required" else "diagnostic-only")
            .put("captureErrors", errors)
        attempt("metadata") { writeMetadata(metadata) }
    }
    failure?.let { throw it }
}
