package ru.bgtu_voenmeh.zapara.design

// Рендеры экранов для ревью дизайна: DESIGN_OUT=/путь ./gradlew testGithubDebugUnitTest --tests '*design.ScreensTest*'
// PNG — в DESIGN_OUT, аудит семантики (неподписанные элементы, касания < 48 dp, латиница) — в DESIGN_OUT/audit.

import android.app.Application
import androidx.compose.runtime.Composable
import androidx.compose.ui.semantics.SemanticsActions
import androidx.compose.ui.semantics.SemanticsNode
import androidx.compose.ui.semantics.SemanticsProperties
import androidx.compose.ui.semantics.getOrNull
import androidx.compose.ui.test.isRoot
import androidx.compose.ui.test.junit4.createComposeRule
import androidx.test.core.app.ApplicationProvider
import androidx.test.ext.junit.runners.AndroidJUnit4
import com.github.takahirom.roborazzi.ExperimentalRoborazziApi
import com.github.takahirom.roborazzi.captureScreenRoboImage
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import ru.bgtu_voenmeh.zapara.data.GroupInfo
import ru.bgtu_voenmeh.zapara.data.communities.GroupTopic
import ru.bgtu_voenmeh.zapara.data.social.InboxRow
import ru.bgtu_voenmeh.zapara.data.social.SocialMessage
import ru.bgtu_voenmeh.zapara.data.social.SocialReaction
import ru.bgtu_voenmeh.zapara.ui.AndroidUiCopy
import ru.bgtu_voenmeh.zapara.ui.account.AccountUiState
import ru.bgtu_voenmeh.zapara.ui.communities.CommunitiesSection
import ru.bgtu_voenmeh.zapara.ui.communities.CommunitiesUiState
import ru.bgtu_voenmeh.zapara.ui.communities.CommunityListItemUi
import ru.bgtu_voenmeh.zapara.ui.communities.CommunityPane
import ru.bgtu_voenmeh.zapara.ui.groups.GroupMessageUi
import ru.bgtu_voenmeh.zapara.ui.groups.GroupSection
import ru.bgtu_voenmeh.zapara.ui.groups.GroupUiState
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkGroups
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkSection
import ru.bgtu_voenmeh.zapara.ui.homework.HomeworkUiState
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxSection
import ru.bgtu_voenmeh.zapara.ui.inbox.InboxUiState
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleSection
import ru.bgtu_voenmeh.zapara.ui.schedule.ScheduleUiState
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsSection
import ru.bgtu_voenmeh.zapara.ui.settings.SettingsUiState
import ru.bgtu_voenmeh.zapara.ui.settings.UpdateUiState
import ru.bgtu_voenmeh.zapara.ui.shell.GroupPickerSheet
import ru.bgtu_voenmeh.zapara.ui.shell.Section
import ru.bgtu_voenmeh.zapara.ui.shell.SectionsSheet
import ru.bgtu_voenmeh.zapara.ui.shell.ShellLogic
import ru.bgtu_voenmeh.zapara.ui.week.WeekComposer
import ru.bgtu_voenmeh.zapara.ui.week.WeekHomeworkUi
import ru.bgtu_voenmeh.zapara.ui.week.WeekNavigation
import ru.bgtu_voenmeh.zapara.ui.week.WeekSection
import ru.bgtu_voenmeh.zapara.ui.week.WeekUiState
import java.io.File
import java.time.Instant

@OptIn(ExperimentalRoborazziApi::class)
@RunWith(AndroidJUnit4::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34], qualifiers = "w390dp-h844dp-xxhdpi", application = Application::class)
class ScreensTest {
    @get:Rule val rule = createComposeRule()
    private val out = File(System.getProperty("design.out") ?: "build/design").also { it.mkdirs() }
    private val auditDir = File(out, "audit").also { it.mkdirs() }
    private val ctx get() = ApplicationProvider.getApplicationContext<Application>()
    private val copy get() = AndroidUiCopy(ctx)
    private val chipOdd get() = ShellLogic.chip(Fx.GROUP, false, copy)

    private fun shot(name: String, content: @Composable () -> Unit) {
        rule.mainClock.autoAdvance = true
        rule.setContent(content)
        rule.mainClock.advanceTimeBy(2500)
        rule.waitForIdle()
        captureScreenRoboImage(File(out, "$name.png").path)
        audit(name)
    }

    /** План этажа декодируется в Dispatchers.IO — даём ему реальное время, иначе на рендере «Загружаем план». */
    private fun shotSettled(name: String, content: @Composable () -> Unit) {
        rule.mainClock.autoAdvance = true
        rule.setContent(content)
        repeat(8) { Thread.sleep(250); rule.mainClock.advanceTimeBy(500); rule.waitForIdle() }
        captureScreenRoboImage(File(out, "$name.png").path)
        audit(name)
    }

    /** Масштаб и сдвиг для аудитории пары: MapFocus (#103), если он есть в ветке; иначе как на develop — весь этаж. */
    private fun lessonFocus(rect: ru.bgtu_voenmeh.zapara.data.CoordsRect): Triple<Float, Float, Float> = try {
        val cls = Class.forName("ru.bgtu_voenmeh.zapara.ui.maps.MapFocus")
        val f = cls.getMethod("focus", ru.bgtu_voenmeh.zapara.data.CoordsRect::class.java).invoke(cls.getField("INSTANCE").get(null), rect)
        Triple(f.javaClass.getMethod("getZoom").invoke(f) as Float, f.javaClass.getMethod("getPanX").invoke(f) as Float,
            f.javaClass.getMethod("getPanY").invoke(f) as Float)
    } catch (_: ClassNotFoundException) { Triple(1f, 0f, 0f) }

    private fun mapsLesson(room: String, line: String): ru.bgtu_voenmeh.zapara.ui.maps.MapsUiState {
        val plan = File(ctx.cacheDir, "gk2.jpg").also { f -> f.outputStream().use { o -> File("src/main/assets/maps/karta-glavnyj-korpus-2-etazh-2022.jpg").inputStream().use { it.copyTo(o) } } }
        val size = android.graphics.BitmapFactory.Options().apply { inJustDecodeBounds = true }.also { android.graphics.BitmapFactory.decodeFile(plan.path, it) }
        val key = ru.bgtu_voenmeh.zapara.ui.maps.FloorKey("ГК", 2)
        val rect = ru.bgtu_voenmeh.zapara.data.MapResolve.findCoords(ru.bgtu_voenmeh.zapara.data.MapStore(ctx).coords(), "ГК", 2, room)
        val (zoom, panX, panY) = rect?.let { lessonFocus(it) } ?: Triple(1f, 0f, 0f)
        val st = ru.bgtu_voenmeh.zapara.ui.maps.MapsUiState(loaded = true, hasGroup = true, building = "ГК", floor = 2, planFile = plan,
            mode = ru.bgtu_voenmeh.zapara.ui.maps.MapMode.Lesson, contextLine = line, unmarked = false,
            rasterCatalog = mapOf(key to ru.bgtu_voenmeh.zapara.ui.maps.FloorRaster(plan, ru.bgtu_voenmeh.zapara.ui.maps.RasterSize(maxOf(1, size.outWidth), maxOf(1, size.outHeight)))),
            floorFiles = mapOf(2 to plan), highlight = rect?.let { ru.bgtu_voenmeh.zapara.ui.maps.HighlightUi(it, room) },
            roomUnmarked = rect == null, zoom = zoom, panX = panX, panY = panY)
        if (rect == null) try { // поле из #103; на develop его нет
            ru.bgtu_voenmeh.zapara.ui.maps.MapsUiState::class.java.getDeclaredField("unmarkedRoom").apply { isAccessible = true }.set(st, room)
        } catch (_: NoSuchFieldException) {}
        return st
    }

    @Test fun s38() { val st = mapsLesson("213", "Физика · 10:50–12:20 · 213 ГК")
        shotSettled("38-maps-lesson-room-on-plan-light") { Shell(false, Section.Maps, chipOdd) { ru.bgtu_voenmeh.zapara.ui.maps.MapsSection(st, {}) } } }
    @Test fun s39() { val st = mapsLesson("229", "Физика · 10:50–12:20 · 229 ГК")
        shotSettled("39-maps-lesson-room-not-on-plan-light") { Shell(false, Section.Maps, chipOdd) { ru.bgtu_voenmeh.zapara.ui.maps.MapsSection(st, {}) } } }

    /** Карточка сообщества у старосты: участники, персонал, заявка. Имена (#104) — из поля people. */
    @Test fun s40() {
        val u1 = "3f6c2a9e-1b7d-4c8e-9a51-2d0e7b4f8c11"; val u2 = "8a1e4d27-6c3b-4f90-b2a8-5e7d1c9f0a32"
        val u3 = "c5b9e0f4-2a6d-4e1b-8f73-9d4c2b6a1e53"; val u4 = "e27d8c1a-9f4b-4a6e-b3c5-0f1e8d2a7b94"
        var snap = ru.bgtu_voenmeh.zapara.ui.communities.CommunitySnapshot(guest = false,
            communities = listOf(ru.bgtu_voenmeh.zapara.data.communities.Community("c1", "И831Б", "Учебная группа, 1 курс", 1, "headman")),
            selectedId = "c1",
            members = listOf(ru.bgtu_voenmeh.zapara.data.communities.CommunityMember(u1, "member"),
                ru.bgtu_voenmeh.zapara.data.communities.CommunityMember(u2, "member"),
                ru.bgtu_voenmeh.zapara.data.communities.CommunityMember(u4, "member")),
            staff = listOf(ru.bgtu_voenmeh.zapara.data.communities.CommunityMember(u3, "headman")),
            joinRequests = listOf(ru.bgtu_voenmeh.zapara.data.communities.JoinRequest("r1", "c1", u2, "pending", java.time.Instant.parse("2026-10-09T09:00:00Z"))))
        val people = mapOf(
            u1 to ru.bgtu_voenmeh.zapara.data.communities.Classmate(u1, "sokolova.a", "Соколова Анна", "member", false),
            u2 to ru.bgtu_voenmeh.zapara.data.communities.Classmate(u2, "ivanov_i", "Иванов Иван", "member", false),
            u3 to ru.bgtu_voenmeh.zapara.data.communities.Classmate(u3, "petrov", null, "headman", true))
        snap = snap.copy(people = people) // #110 follow-up: #115 в develop, без reflection
        val st = ru.bgtu_voenmeh.zapara.ui.communities.CommunitiesComposer.compose(snap)
        shot("40-community-moderator-light") { Shell(false, Section.Community, chipOdd) { CommunitiesSection(st, {}) } }
    }

    private fun audit(name: String) {
        val lines = mutableListOf<String>()
        val density = ctx.resources.displayMetrics.density
        runCatching {
            rule.onAllNodes(isRoot(), useUnmergedTree = false).fetchSemanticsNodes().forEach { root -> walk(root, density, lines) }
        }.onFailure { lines += "audit failed: $it" }
        File(auditDir, "$name.txt").writeText(lines.joinToString("\n"))
    }

    private fun walk(n: SemanticsNode, d: Float, out: MutableList<String>) {
        val cfg = n.config
        val text = cfg.getOrNull(SemanticsProperties.Text)?.joinToString(" ") { it.text }.orEmpty()
        val desc = cfg.getOrNull(SemanticsProperties.ContentDescription)?.joinToString(" ").orEmpty()
        val tag = cfg.getOrNull(SemanticsProperties.TestTag).orEmpty()
        val w = n.boundsInRoot.width / d; val h = n.boundsInRoot.height / d
        val clickable = cfg.contains(SemanticsActions.OnClick) || cfg.contains(SemanticsActions.OnLongClick)
        val visible = w > 0.5f && h > 0.5f
        if (clickable && visible) {
            if (text.isBlank() && desc.isBlank()) out += "UNLABELED clickable tag=$tag size=${w.toInt()}x${h.toInt()}dp"
            if (w < 47.5f || h < 47.5f) out += "SMALL_TARGET ${w.toInt()}x${h.toInt()}dp tag=$tag label='${(text + " " + desc).trim().take(50)}'"
        }
        if (visible && text.isNotBlank() && Regex("^[a-z][a-zA-Z_]{2,}$").matches(text.trim())) out += "RAW_IDENTIFIER_TEXT '$text' tag=$tag"
        if (visible && text.isNotBlank() && Regex("[A-Za-z]{4,}").containsMatchIn(text) && !Regex("[А-Яа-яЁё]").containsMatchIn(text))
            out += "LATIN_TEXT '$text' tag=$tag"
        n.children.forEach { walk(it, d, out) }
    }

    // ---------- fixtures ----------
    private fun today(selected: java.time.LocalDate = Fx.today) = ScheduleUiState(loaded = true, hasGroup = true, today = Fx.today,
        selected = selected, pages = Fx.pages(ctx, from = Fx.today.minusDays(3), days = 10), now = Fx.now,
        groupId = Fx.GROUP, profileName = "default")

    private fun week(): WeekUiState {
        val parity = WeekNavigation.parity(Fx.today, Fx.ctx)
        val days = WeekComposer.compose(parity, Fx.lessons, { _, _ -> "" }, Fx.ctx, Fx.today, copy, Fx.today)
        return WeekUiState(loaded = true, hasGroup = true, parity = parity, currentParity = parity, days = days,
            selectedDate = Fx.today, groupId = Fx.GROUP, profileName = "default",
            deadlines = listOf(
                WeekHomeworkUi(1, "Физика", "Задачи 3.14–3.20", Fx.today, false),
                WeekHomeworkUi(2, "Программирование на C++", "Лабораторная №3", Fx.today.plusDays(7), false),
                WeekHomeworkUi(4, "Математический анализ", "Типовой расчёт №2", Fx.today.plusDays(4), false)),
            undatedHomework = 1)
    }

    private fun homework(): HomeworkUiState {
        val hw = Fx.homework() + listOf(
            ru.bgtu_voenmeh.zapara.data.Homework(5, "лекисторияроссии", "Конспект параграфа 12, подготовить доклад", Fx.today.minusDays(10), 1, Fx.today.minusDays(1), "overdue", false),
            ru.bgtu_voenmeh.zapara.data.Homework(6, "пр инженерная графика", "Чертёж А3: разрез детали, лист 4", Fx.today.minusDays(2), 2, Fx.today.plusDays(1), "approaching", false))
        val subj = mapOf(1L to "Физика", 2L to "Программирование на C++", 3L to "Иностранный язык", 4L to "Математический анализ", 5L to "История России", 6L to "Инженерная графика")
        val items = hw.map { HomeworkGroups.toItem(it, subj.getValue(it.id), Fx.today, copy) }
        return HomeworkUiState(loaded = true, hasGroup = true, groups = HomeworkGroups.group(items, copy), groupId = Fx.GROUP, profileName = "default")
    }

    private val t0 = Instant.parse("2026-10-08T08:05:00Z")
    private fun inbox() = InboxUiState(inboxLoaded = true, userId = "u-me", profileDatabaseName = "default", code = "ZAP-7K2Q",
        rows = listOf(
            InboxRow("g1", "И831Б", communityId = "c1", lastBody = "Аня: кто-нибудь знает, физика будет в 229?", lastAt = t0, unread = 5),
            InboxRow("d1", "Миша Орлов", lastBody = "Скинь, пожалуйста, лабу по плюсам", lastAt = t0.minusSeconds(3600), unread = 1, peerUserId = "u-misha"),
            InboxRow("d2", "Аня Белова", lastBody = "Спасибо!", lastAt = t0.minusSeconds(86400), peerUserId = "u-anya"),
            InboxRow("d3", "Староста Дима", lastBody = "Завтра собрание после второй пары", lastAt = t0.minusSeconds(2 * 86400), peerUserId = "u-dima")))

    private fun msg(id: String, me: Boolean, body: String, minAgo: Long, reactions: List<SocialReaction> = emptyList()) =
        SocialMessage(id, if (me) "u-me" else "u-misha", if (me) "Я" else "Миша Орлов", body, "text", t0.minusSeconds(minAgo * 60),
            null, null, false, false, true, reactions, null, null)

    private fun topic(id: String?, title: String, icon: String, kind: String, last: String?, author: String?, unread: Int, ballots: Int = 0, pinned: Boolean = false) =
        GroupTopic(id, title, icon, kind, last, author, t0, unread, false, ballots, pinned = pinned)

    private fun group(showChannels: Boolean) = GroupUiState(hasHome = true, communityId = "c1", title = "И831Б", myRole = "member",
        showChannels = showChannels, activeChannelKind = "chat", chatTitle = "Общий чат", activeConversationId = "g1",
        channels = listOf(
            topic(null, "Общий чат", "💬", "chat", "Кто-нибудь знает, физика будет в 229?", "Аня", 5),
            topic("t1", "Важное", "megaphone", "chat", "Перенос пары по истории на пятницу", "Дима", 1, pinned = true),
            topic("t2", "Опросы", "vote", "ballots", null, null, 0, ballots = 1),
            topic("t3", "Задания", "book", "homework", "Лабораторная №3 до 15.10", "Дима", 0)),
        messages = listOf(
            GroupMessageUi("m1", "Дима", "Всем привет! Завтра собрание после второй пары в 322", "09:12", false, day = "Сегодня", senderId = "u-dima"),
            GroupMessageUi("m2", "Аня", "Кто-нибудь знает, физика будет в 229?", "10:41", false, day = "Сегодня", senderId = "u-anya"),
            GroupMessageUi("m3", "Я", "Да, в 229, Петров писал в чат кафедры", "10:43", true, day = "Сегодня", senderId = "u-me"),
            GroupMessageUi("m4", "Миша", "Спасибо 🙌", "10:44", false, day = "Сегодня", senderId = "u-misha")))

    private fun settings() = SettingsUiState(loaded = true, groupName = Fx.GROUP, groupId = Fx.GROUP, profileName = "default",
        groupUpdated = "сегодня, 08:40", version = "2.1.42", apiConfigured = true)
    private fun guestAccount(registration: Boolean = false) = AccountUiState(ready = true, configured = true, guest = true,
        registration = registration, registrationAvailable = true, vkAvailable = true, yandexAvailable = true, recoveryAvailable = true,
        username = if (registration) "ivan.petrov" else "", password = if (registration) "Zap4ra!2026" else "", status = "")

    // ---------- screens ----------
    @Test fun s01() = shot("01-today-light") { Shell(false, Section.Schedule, chipOdd, homeworkBadge = 2) { ScheduleSection(today(), {}) {} } }
    @Test fun s02() = shot("02-today-dark") { Shell(true, Section.Schedule, chipOdd, homeworkBadge = 2) { ScheduleSection(today(), {}) {} } }
    @Test fun s03() = shot("03-today-fontscale130-light") { Shell(false, Section.Schedule, chipOdd, homeworkBadge = 2, fontScale = 1.3f) { ScheduleSection(today(), {}) {} } }
    @Test fun s04() = shot("04-today-day-off-sunday-light") { Shell(false, Section.Schedule, chipOdd) { ScheduleSection(today(Fx.today.plusDays(3)), {}) {} } }
    @Test fun s05() = shot("05-schedule-no-group-light") { Shell(false, Section.Schedule, null, hasGroup = false) { ScheduleSection(ScheduleUiState(loaded = true, hasGroup = false, today = Fx.today, now = Fx.now), {}) {} } }
    @Test fun s06() = shot("06-schedule-load-error-dark") { Shell(true, Section.Schedule, chipOdd, stale = true) { ScheduleSection(ScheduleUiState(loaded = true, hasGroup = true, today = Fx.today, now = Fx.now, error = "Нет соединения с сервером. Показать сохранённое расписание не удалось."), {}) {} } }
    @Test fun s07() = shot("07-group-picker-sheet-light") { Shell(false, Section.Schedule, chipOdd) {
        ScheduleSection(today(), {}) {}
        GroupPickerSheet(listOf("И831Б", "И832Б", "И833Б", "О711Б", "Е411", "А191").map { GroupInfo(it, it) }, Fx.GROUP, {}, {})
    } }
    @Test fun s08() = shot("08-lesson-actions-sheet-light") { Shell(false, Section.Schedule, chipOdd) {
        val st = today(); ScheduleSection(st.copy(actionsFor = st.pages.getValue(Fx.today).lessons[1]), {}) {}
    } }
    @Test fun s09() = shot("09-week-light") { Shell(false, Section.Week, chipOdd) { WeekSection(week(), {}, {}) } }
    @Test fun s10() = shot("10-week-dark") { Shell(true, Section.Week, chipOdd) { WeekSection(week(), {}, {}) } }
    @Test fun s11() = shot("11-homework-list-light") { Shell(false, Section.Homework, chipOdd, homeworkBadge = 2) { HomeworkSection(homework(), {}) } }
    @Test fun s12() = shot("12-homework-empty-light") { Shell(false, Section.Homework, chipOdd) { HomeworkSection(HomeworkUiState(loaded = true, hasGroup = true, groupId = Fx.GROUP), {}) } }
    @Test fun s13() = shot("13-homework-error-dark") { Shell(true, Section.Homework, chipOdd) { HomeworkSection(HomeworkUiState(loaded = true, hasGroup = true, groupId = Fx.GROUP, loadError = "Не удалось открыть базу домашки"), {}) } }
    @Test fun s14() = shot("14-chats-inbox-light") { Shell(false, Section.Chat, chipOdd) { InboxSection(inbox(), {}, { _, _ -> }) } }
    @Test fun s15() = shot("15-chats-signed-out-light") { Shell(false, Section.Chat, chipOdd) { InboxSection(InboxUiState(guest = true), {}, { _, _ -> }) } }
    @Test fun s16() = shot("16-personal-chat-light") { Shell(false, Section.Chat, chipOdd) {
        val st = inbox(); InboxSection(st.copy(active = st.rows[1], historyLoaded = true, messages = listOf(
            msg("p1", false, "Привет! Ты сделал лабу по плюсам?", 70),
            msg("p2", true, "Да, почти. Осталось оформить отчёт", 65),
            msg("p3", false, "Скинь, пожалуйста, лабу по плюсам", 60, listOf(SocialReaction("👍", 1, true))))), {}, { _, _ -> })
    } }
    @Test fun s17() = shot("17-group-channels-light") { Shell(false, Section.Group, chipOdd) { GroupSection(group(showChannels = true), {}) } }
    @Test fun s18() = shot("18-group-chat-dark") { Shell(true, Section.Group, chipOdd) { GroupSection(group(showChannels = false), {}) } }
    @Test fun s19() = shot("19-communities-catalog-light") { Shell(false, Section.Community, chipOdd) { CommunitiesSection(CommunitiesUiState(CommunityPane.Catalog, communities = listOf(
        CommunityListItemUi("c1", "И831Б", "Учебная группа, 1 курс, факультет И", "member", null, false, true),
        CommunityListItemUi("c2", "Студсовет Военмеха", "Новости, мероприятия и волонтёрство", null, "pending", false, false),
        CommunityListItemUi("c3", "Робототехника", "Кружок при кафедре И2, встречи по средам", null, null, true, false),
        CommunityListItemUi("c4", "Шахматный клуб", "Турниры каждую пятницу в 18:00", "curator", null, false, true))), {}) } }
    @Test fun s20() = shot("20-communities-signed-out-dark") { Shell(true, Section.Community, chipOdd) { CommunitiesSection(CommunitiesUiState(CommunityPane.Guest), {}) } }
    @Test fun s21() = shot("21-settings-overview-light") { Shell(false, Section.Settings, chipOdd) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount()) } }
    @Test fun s22() = shot("22-settings-overview-dark") { Shell(true, Section.Settings, chipOdd) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount()) } }
    @Test fun s23() = shot("23-login-light") { Shell(false, Section.Settings, chipOdd) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount(), initialSection = "account") } }
    @Test fun s24() = shot("24-registration-light") { Shell(false, Section.Settings, chipOdd) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount(registration = true), initialSection = "account") } }
    @Test fun s25() = shot("25-sections-sheet-dark") { Shell(true, Section.Schedule, chipOdd) {
        ScheduleSection(today(), {}) {}
        SectionsSheet(Section.Schedule, {}, {})
    } }

    @Test fun s26() {
        val plan = File(ctx.cacheDir, "gk2.jpg").also { f -> f.outputStream().use { o -> File("src/main/assets/maps/karta-glavnyj-korpus-2-etazh-2022.jpg").inputStream().use { it.copyTo(o) } } }
        val size = android.graphics.BitmapFactory.Options().apply { inJustDecodeBounds = true }.also { android.graphics.BitmapFactory.decodeFile(plan.path, it) }
        val key = ru.bgtu_voenmeh.zapara.ui.maps.FloorKey("ГК", 2)
        val st = ru.bgtu_voenmeh.zapara.ui.maps.MapsUiState(loaded = true, hasGroup = true, building = "ГК", floor = 2, planFile = plan,
            mode = ru.bgtu_voenmeh.zapara.ui.maps.MapMode.Lesson, contextLine = "Физика · 10:50–12:20 · 229 ГК", unmarked = false,
            rasterCatalog = mapOf(key to ru.bgtu_voenmeh.zapara.ui.maps.FloorRaster(plan, ru.bgtu_voenmeh.zapara.ui.maps.RasterSize(maxOf(1, size.outWidth), maxOf(1, size.outHeight)))),
            floorFiles = mapOf(2 to plan))
        shot("26-maps-lesson-room-light") { Shell(false, Section.Maps, chipOdd) { ru.bgtu_voenmeh.zapara.ui.maps.MapsSection(st, {}) } }
    }

    // ---------- round 2: font scale ----------
    @Composable private fun pchat(fs: Float) = Shell(false, Section.Chat, chipOdd, fontScale = fs) {
        val st = inbox(); InboxSection(st.copy(active = st.rows[1], historyLoaded = true, messages = listOf(
            msg("p1", false, "Привет! Ты сделал лабу по плюсам?", 70),
            msg("p2", true, "Да, почти. Осталось оформить отчёт", 65),
            msg("p3", false, "Скинь, пожалуйста, лабу по плюсам", 60, listOf(SocialReaction("👍", 1, true))))), {}, { _, _ -> })
    }
    @Test fun f01() = shot("27-today-fontscale200-light") { Shell(false, Section.Schedule, chipOdd, homeworkBadge = 2, fontScale = 2.0f) { ScheduleSection(today(), {}) {} } }
    @Test fun f02() = shot("28-week-fontscale130-light") { Shell(false, Section.Week, chipOdd, fontScale = 1.3f) { WeekSection(week(), {}, {}) } }
    @Test fun f03() = shot("29-week-fontscale200-light") { Shell(false, Section.Week, chipOdd, fontScale = 2.0f) { WeekSection(week(), {}, {}) } }
    @Test fun f04() = shot("30-homework-list-fontscale130-light") { Shell(false, Section.Homework, chipOdd, homeworkBadge = 2, fontScale = 1.3f) { HomeworkSection(homework(), {}) } }
    @Test fun f05() = shot("31-homework-list-fontscale200-light") { Shell(false, Section.Homework, chipOdd, homeworkBadge = 2, fontScale = 2.0f) { HomeworkSection(homework(), {}) } }
    @Test fun f06() = shot("32-personal-chat-fontscale130-light") { pchat(1.3f) }
    @Test fun f07() = shot("33-personal-chat-fontscale200-light") { pchat(2.0f) }
    @Test fun f08() = shot("34-group-chat-fontscale130-light") { Shell(false, Section.Group, chipOdd, fontScale = 1.3f) { GroupSection(group(showChannels = false), {}) } }
    @Test fun f09() = shot("35-group-chat-fontscale200-light") { Shell(false, Section.Group, chipOdd, fontScale = 2.0f) { GroupSection(group(showChannels = false), {}) } }
    @Test fun f10() = shot("36-login-fontscale130-light") { Shell(false, Section.Settings, chipOdd, fontScale = 1.3f) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount(), initialSection = "account") } }
    @Test fun f11() = shot("37-login-fontscale200-light") { Shell(false, Section.Settings, chipOdd, fontScale = 2.0f) { SettingsSection(settings(), {}, UpdateUiState(), {}, guestAccount(), initialSection = "account") } }
}
