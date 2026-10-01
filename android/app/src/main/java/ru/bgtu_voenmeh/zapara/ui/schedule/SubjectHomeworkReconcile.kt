package ru.bgtu_voenmeh.zapara.ui.schedule

internal fun reconcileSubjectSheetRows(localRows: List<HomeworkRowUi>, sharedRows: List<HomeworkRowUi>): List<HomeworkRowUi> =
    localRows.filter { it.sharedId == null } + sharedRows.filter { it.sharedId != null }.distinctBy { it.sharedId }
