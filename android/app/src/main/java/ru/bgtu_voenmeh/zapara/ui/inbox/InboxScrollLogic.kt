package ru.bgtu_voenmeh.zapara.ui.inbox

internal fun shouldAutoScroll(lastId: String?, nextId: String?, nearBottom: Boolean): Boolean =
    nextId != null && nextId != lastId && (lastId == null || nearBottom)
