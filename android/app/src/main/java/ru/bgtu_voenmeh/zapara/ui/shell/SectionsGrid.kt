package ru.bgtu_voenmeh.zapara.ui.shell

/** #108 / AN-18: шторка «Разделы» — DESIGN.md §4: сетка 2 колонки, затем «Настройки» на всю ширину; без вкладок нижней панели. */
object SectionsGrid {
    val tiles = listOf(Section.Week, Section.Summary, Section.Teachers, Section.Friends, Section.Community, Section.Group)

    fun rows(sections: List<Section>, columns: Int): List<List<Section>> {
        val grid = sections.filter { it != Section.Settings && !it.inBar }
        return grid.chunked(columns.coerceAtLeast(1)) + (if (Section.Settings in sections) listOf(listOf(Section.Settings)) else emptyList())
    }
}
