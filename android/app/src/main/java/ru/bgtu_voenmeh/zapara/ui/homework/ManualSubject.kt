package ru.bgtu_voenmeh.zapara.ui.homework

import ru.bgtu_voenmeh.zapara.data.Parity

internal fun manualSubjectAllowed(subjects: List<SubjectUi>, text: String): Boolean =
    subjects.isEmpty() && Parity.normalizeSubject(text).isNotBlank()
