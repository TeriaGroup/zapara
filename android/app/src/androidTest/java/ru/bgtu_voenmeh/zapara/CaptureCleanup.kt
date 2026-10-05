package ru.bgtu_voenmeh.zapara

/** Restore the cheap singleton before any potentially blocking font/main handoff. */
internal fun restoreCaptureState(bodyFailure: Throwable?, network: () -> Unit, font: () -> Unit) {
    var failure = bodyFailure
    try { network() } catch (error: Throwable) {
        if (failure == null) failure = error else failure.addSuppressed(error)
    }
    try { font() } catch (error: Throwable) {
        if (failure == null) failure = error else failure.addSuppressed(error)
    }
    if (bodyFailure == null) failure?.let { throw it }
}
