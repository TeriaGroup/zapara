package ru.bgtu_voenmeh.zapara

import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.runtime.*
import androidx.compose.ui.Modifier
import androidx.compose.ui.ExperimentalComposeUiApi
import androidx.compose.ui.semantics.semantics
import androidx.compose.ui.semantics.testTagsAsResourceId
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.runBlocking
import ru.bgtu_voenmeh.zapara.data.MapResolve
import ru.bgtu_voenmeh.zapara.data.MapStore
import ru.bgtu_voenmeh.zapara.ui.maps.*
import ru.bgtu_voenmeh.zapara.ui.theme.*
import androidx.compose.material3.Text
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.ui.semantics.contentDescription
import ru.bgtu_voenmeh.zapara.ui.components.ZChip
import ru.bgtu_voenmeh.zapara.ui.components.ZSwitch
import androidx.compose.ui.platform.LocalContext
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.shell.LocalShellChrome
import ru.bgtu_voenmeh.zapara.ui.shell.ShellChrome
import ru.bgtu_voenmeh.zapara.ui.summary.SummarySection
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryEvent
import ru.bgtu_voenmeh.zapara.ui.summary.SummaryUiState
import ru.bgtu_voenmeh.zapara.ui.homework.*

/** Unsupported matrix cells FAIL, never render a substitute. */
internal class UiMapsCaptureFixtures : AutoCloseable {
    private val context = MapTestContext()
    private val catalog = runBlocking(Dispatchers.IO) {
        FloorRasterLoader(MapStore(context)).load(MapResolve.MAP_FILES.keys.map { FloorKey(it.first, it.second) }.toSet())
    }
    var current by mutableStateOf(MapsUiState())
        private set
    var chip by mutableStateOf(false)
    var switched by mutableStateOf(false)
    var clicks by mutableIntStateOf(0)
    var summary by mutableStateOf(SummaryUiState())
        private set
    var homework by mutableStateOf(HomeworkUiState())
        private set
    var homeworkSaveCallbacks = 0
        private set

    fun state(scenario: String): MapsUiState {
        check(scenario == "maps-no-route") { "Task12 scenario not implemented in this checkpoint: $scenario" }
        check(catalog.size == 9) { "All nine real bundled rasters are required" }
        return MapsUiState(loaded = true, rasterCatalog = catalog,
            planFile = catalog.getValue(FloorKey("ГК", 1)).file,
            floorFiles = catalog.filterKeys { it.building == "ГК" }.mapKeys { it.key.floor }.mapValues { it.value.file })
    }

    fun prepare(scenario: String) {
        when (scenario) {
            "primitives" -> Unit
            "summary" -> summary = SummaryCaptureModel.state(2, AndroidUiCopy(context))
            "homework" -> homework = HomeworkCaptureModel.state(AndroidUiCopy(context))
            else -> current = state(scenario)
        }
    }

    @OptIn(ExperimentalComposeUiApi::class)
    @Composable fun content(scenario: String, theme: ThemeChoice) {
        check(scenario in listOf("maps-no-route", "primitives", "summary", "homework"))
        ZaparaTheme(theme, MotionSettings.Off) {
            Box(Modifier.fillMaxSize().background(Zapara.colors.canvas).semantics { testTagsAsResourceId = true }) {
                if (scenario == "primitives") {
                    AccessibilityShowcase {
                        Column(verticalArrangement = Arrangement.spacedBy(Zapara.space.l)) {
                            ZCard {
                                Text("Примитивы интерфейса", style = Zapara.typography.section)
                                Text("Учебная группа и расписание занятий", style = Zapara.typography.body)
                                Text("Состояния элементов управления", style = Zapara.typography.caption)
                            }
                            ZChip(if (chip) "Группа выбрана" else "Выбрать группу", selected = chip,
                                onClick = { chip = !chip }, tag = "Capture.Chip")
                            ZButton("Сохранить изменения", { clicks++ }, tag = "Capture.Button")
                            ZButton("Отмена", { clicks++ }, ghost = true, tag = "Capture.Ghost")
                            ZButton("Недоступное действие", {}, enabled = false, tag = "Capture.Disabled")
                            ZSwitch(switched, { switched = it }, "Capture.Switch",
                                Modifier.semantics { contentDescription = "Показывать все группы" })
                            ZIconButton(R.drawable.ic_plus, "Добавить элемент", { clicks++ }, "Capture.BottomIcon")
                        }
                    }
                } else if (scenario == "summary") {
                    val copy = AndroidUiCopy(LocalContext.current)
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                        SummarySection(summary) { event ->
                            when (event) {
                                is SummaryEvent.Segment -> summary = SummaryCaptureModel.state(event.index, copy)
                            }
                        }
                    }
                } else if (scenario == "homework") {
                    val copy = AndroidUiCopy(LocalContext.current)
                    CompositionLocalProvider(LocalShellChrome provides ShellChrome("Тестовая группа", false, true) {}) {
                        HomeworkSection(homework) { event ->
                            if (event is HomeworkEvent.ToggleDone) {
                                // UI-only toggle on a fixed snapshot, NOT service unmark/recompute semantics.
                                val items = homework.groups.flatMap { it.items }.map { item ->
                                    if (item.id != event.id) item else {
                                        val original = HomeworkCaptureModel.records.single { it.id == item.id }
                                        val done = !item.done
                                        val status = if (done) "done" else original.status
                                        item.copy(done = done, status = status, statusLabel = HomeworkGroups.statusLabel(status, done, copy))
                                    }
                                }
                                homework = homework.copy(groups = HomeworkGroups.group(items, copy).map { it.copy(collapsed = false) })
                            } else {
                                homework = HomeworkCaptureModel.reduce(homework, event)
                                if (event == HomeworkEvent.Save) homeworkSaveCallbacks++
                            }
                        }
                    }
                } else {
                MapsSection(current) { event ->
                    current = when (event) {
                        MapsEvent.OpenFrom -> current.copy(picker = RoutePickerUi(RouteField.From, "", emptyList()))
                        MapsEvent.OpenTo -> current.copy(picker = RoutePickerUi(RouteField.To, "", emptyList()))
                        MapsEvent.ClosePicker -> current.copy(picker = null)
                        is MapsEvent.QueryPlaces -> current.copy(picker = current.picker?.copy(query = event.value))
                        else -> throw AssertionError("Unimplemented capture event: $event")
                    }
                }
                }
            }
        }
    }

    override fun close() = context.close()
}
