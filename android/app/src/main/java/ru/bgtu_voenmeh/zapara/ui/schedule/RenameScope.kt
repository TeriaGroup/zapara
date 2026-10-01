package ru.bgtu_voenmeh.zapara.ui.schedule

import ru.bgtu_voenmeh.zapara.data.db.OverrideEntity

internal fun renameScopeKey(scope: Int, dayOfWeek: Int): String =
    if (scope == 0) "global" else "weekday:$dayOfWeek"

internal fun renameResetIds(rows: List<OverrideEntity>, subjectNorm: String, scope: String): List<Long> =
    rows.filter { it.subjectRawNormalized == subjectNorm && it.scope == scope }.map { it.id }
