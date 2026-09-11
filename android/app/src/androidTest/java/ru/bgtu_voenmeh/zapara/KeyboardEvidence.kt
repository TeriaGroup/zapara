package ru.bgtu_voenmeh.zapara

import android.view.WindowInsets
import android.view.inspector.WindowInspector
import androidx.test.platform.app.InstrumentationRegistry

internal object KeyboardEvidence {
    fun visible(): Boolean {
        var visible = false
        InstrumentationRegistry.getInstrumentation().runOnMainSync {
            visible = WindowInspector.getGlobalWindowViews().any {
                it.hasWindowFocus() && it.rootWindowInsets?.isVisible(WindowInsets.Type.ime()) == true
            }
        }
        return visible
    }

    fun requireVisible() {
        if (!visible()) throw TextEvidenceFailure(TextDefect.CONDITION, "IME must be visible at interaction and capture")
    }
}
