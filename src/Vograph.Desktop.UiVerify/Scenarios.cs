using System.Text;
using System.Text.Json;
using FlaUI.Core.WindowsAPI;
using Microsoft.Data.Sqlite;

namespace Vograph.Desktop.UiVerify;

/// <summary>
/// The scenario list of the task brief. Every step reads a consequence back — a title, a toggle state, a row
/// count, a label, the window's own pixels — and fails when it is wrong: a step that only clicks and screenshots
/// passes just as happily with a dead command behind the button (T12-R4).
/// </summary>
public static class Scenarios
{
    /// <summary>How different two luminance signatures of the same region must be to count as «it redrew».</summary>
    private const double Redrawn = 0.02;

    public static void Run(Ui ui, Report report, Options o)
    {
        Step(report, ui, "Запуск", () =>
        {
            if (!ui.Window.Title.Contains("Военмех")) throw new Exception($"title «{ui.Window.Title}»");
            ui.Find("Nav.Schedule");
            ui.Find("Schedule.Title");
            // The frame really is the window: PrintWindow gives us its own rendering, and a shell with a
            // sidebar is never one flat colour — which is what a refused render (or an empty DC) would be.
            var spread = ui.Signature(ui.RegionOf("Nav.Schedule"));
            if (spread.Max() - spread.Min() < 0.01) throw new Exception($"кадр однотонный ({spread.Min():0.###}–{spread.Max():0.###}) — окно не отрисовалось в снимок");
            return ($"окно и сайдбар на месте, снимок содержателен ({spread.Min():0.##}–{spread.Max():0.##})", ui.Shot("start"));
        });

        foreach (var (key, probe) in new[] { ("Week", "WeekSegment.0"), ("Summary", "Summary.Total"), ("Teachers", "Teachers.Search"), ("Maps", "Maps.ZoomIn"), ("Friends", "Friends.Add"), ("Homework", "Homework.Add"), ("Settings", "Settings.Refresh"), ("Schedule", "Schedule.Title") })
            Step(report, ui, $"Раздел {key}", () =>
            {
                ui.Click($"Nav.{key}");
                if (key == "Settings") OpenSettingsCategory(ui, "Settings.Category.Study");
                ui.Find(probe);
                return ($"{probe} найден", ui.Shot($"nav-{key.ToLowerInvariant()}"));
            });

        Step(report, ui, "Расписание: стрелки, сегмент, «К сегодня», клавиши", () =>
        {
            // Anchor on today first: SmartStart opens *tomorrow* once the last lesson of the day is over, so the
            // title on load is not the one «К сегодня» comes back to. Every title read waits for the recompose —
            // a day change is a gated Core call, longer than Click's own settle.
            ui.Click("ScheduleSegment.0");
            var title0 = ui.Text("Schedule.Title");
            var start = ui.Shot("schedule-today");
            ui.Click("Schedule.Next");
            if (!ui.WaitText("Schedule.Title", t => t != title0)) throw new Exception("title did not change after Next");
            var title1 = ui.Text("Schedule.Title");
            var next = ui.Shot("schedule-next");
            ui.Click("Schedule.Today");
            if (!ui.WaitText("Schedule.Title", t => t == title0)) throw new Exception($"«К сегодня» did not return to {title0} (showing {ui.Text("Schedule.Title")})");
            var back = ui.Shot("schedule-back-today");
            ui.Click("ScheduleSegment.2");
            if (!ui.WaitText("Schedule.Title", t => t != title0 && t != title1)) throw new Exception("segment «Послезавтра» did not change the day");
            var afterTomorrow = ui.Text("Schedule.Title");
            var after = ui.Shot("schedule-after-tomorrow");
            ui.Keys(VirtualKeyShort.HOME);
            if (!ui.WaitText("Schedule.Title", t => t == title0)) throw new Exception("Home did not return to today");
            var home = ui.Shot("schedule-home");
            ui.Keys(VirtualKeyShort.RIGHT);
            if (!ui.WaitText("Schedule.Title", t => t == title1)) throw new Exception($"Right did not step to {title1}");
            var afterKeys = ui.Text("Schedule.Title");
            var keys = ui.Shot("schedule-keys");
            return ($"{title0} → {title1} → {afterTomorrow} → {afterKeys}", string.Join(", ", start, next, back, after, home, keys));
        });

        Step(report, ui, "Тема и свёрнутый сайдбар", () =>
        {
            // The shell tells nobody its theme — no glyph name, no toggle state, nothing in the automation tree
            // — so the read-back is the window's own paint: the group card's mean luminance. PrintWindow renders
            // the window itself, so an app on top of ours cannot answer this question for it.
            var card = ui.RegionOf("Shell.GroupCard");
            var dark = ui.Luminance(card);
            ui.Click("Shell.ThemeToggle");
            var light = ui.Luminance(card);
            if (light - dark < 0.2) throw new Exception($"тема не переключилась: яркость карточки группы {dark:0.###} → {light:0.###}");
            var lightShot = ui.Shot("theme-light");

            // Ctrl+B: the toggle names its own next action («Свернуть панель» ↔ «Развернуть панель»), and the
            // nav items really shrink to the 64 px rail. Two independent reads of one keystroke.
            var expanded = ui.Find("Shell.SidebarToggle").Name;
            var wide = ui.Find("Nav.Schedule").BoundingRectangle.Width;
            ui.Keys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            if (!ui.WaitFor(() => ui.Find("Shell.SidebarToggle").Name != expanded))
                throw new Exception($"Ctrl+B не свернул сайдбар — кнопка всё ещё «{expanded}»");
            var railed = ui.Find("Shell.SidebarToggle").Name;
            var narrow = ui.Find("Nav.Schedule").BoundingRectangle.Width;
            if (narrow >= wide) throw new Exception($"сайдбар не сузился: Nav.Schedule {wide}px → {narrow}px");
            var rail = ui.Shot("sidebar-rail");
            ui.Keys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            if (!ui.WaitFor(() => ui.Find("Shell.SidebarToggle").Name == expanded))
                throw new Exception($"Ctrl+B не развернул сайдбар обратно — кнопка «{ui.Find("Shell.SidebarToggle").Name}»");

            ui.Click("Shell.ThemeToggle");
            var back = ui.Luminance(card);
            if (Math.Abs(back - dark) > 0.1) throw new Exception($"тема не вернулась: {dark:0.###} → {light:0.###} → {back:0.###}");
            return ($"яркость карточки {dark:0.##} → {light:0.##} → {back:0.##}; «{expanded}» → «{railed}» → «{expanded}», Nav.Schedule {wide} → {narrow} px", rail + ", " + lightShot);
        });

        Step(report, ui, "Диалог выбора группы", () =>
        {
            ui.Click("Shell.GroupCard");
            ui.Find("Dialog.Search");
            var all = Rows(ui);
            if (all.Length == 0) throw new Exception("список групп пуст до фильтра");
            ui.TypeText("09c");
            if (!ui.WaitFor(() => Rows(ui).Length is > 0 and var n && n < all.Length))
                throw new Exception($"латиница «09c» не отфильтровала список: было {all.Length}, стало {Rows(ui).Length} ({string.Join(", ", Rows(ui).Take(5))})");
            var kept = Rows(ui);
            var named = kept.Where(r => r.Length > 0).ToArray();
            if (named.Length > 0 && named.Any(r => !r.Contains("09С", StringComparison.OrdinalIgnoreCase)))
                throw new Exception($"в отфильтрованном списке не только 09С: {string.Join(", ", named)}");
            var shot = ui.Shot("dialog-group-picker");
            ui.Keys(VirtualKeyShort.ESCAPE);
            if (ui.IsShown("Dialog.Search")) throw new Exception("Escape did not close the picker");
            return ($"латиница «09c»: {all.Length} → {kept.Length} строк ({string.Join(", ", named.Take(4))}), Escape закрывает", shot);
        });

        Step(report, ui, "Карточка пары: переименование и переход к домашке", () =>
        {
            ui.Click("Nav.Schedule");
            // The fixture has lessons on Mon (both parities), Tue (odd), Wed and Sat only, and the renamed
            // lecture is Monday's: «press Home and there will be a lesson today» fails on a Thursday with a
            // message that reads like an app regression. Drive the arrows to the day instead (T12-R4).
            var (day, titles) = GoToLessonDay(ui, t => t.Contains("Матан"), "день с парой «Матан»");
            ui.Hover(ui.Find("Lesson.Title"));
            ui.Click("Lesson.Rename");
            ui.Find("Dialog.Name");
            // The dialog is really about that lesson: both fields carry the seeded override back.
            var name = ui.Text("Dialog.Name");
            var note = ui.Text("Dialog.Note");
            if (name != "Матан") throw new Exception($"в «Новое название» — «{name}», а карточка показывает «Матан»");
            if (!note.Contains("493")) throw new Exception($"в «Примечание» — «{note}», ожидалась сноска фикстуры «лекции — в 493»");
            var rename = ui.Shot("dialog-rename");
            ui.Keys(VirtualKeyShort.ESCAPE);
            if (ui.IsShown("Dialog.Name")) throw new Exception("Escape не закрыл диалог переименования");

            ui.Hover(ui.Find("Lesson.Title"));
            ui.Click("Lesson.Homework");
            ui.Find("Homework.Add");
            var filtered = ui.Window.FindAllDescendants().Any(element => element.Name == "Все предметы");
            if (!filtered) throw new Exception("переход из пары не выбрал предмет в списке домашки");
            var hw = ui.Shot("lesson-homework-filter");
            return ($"{day} ({string.Join(" / ", titles)}): переименование показывает «{name}» / «{note}», домашка открыла фильтр предмета", rename + ", " + hw);
        });

        Step(report, ui, "Домашка: выбор предмета и отмена удаления", () =>
        {
            ui.Click("Nav.Homework");
            var before = ui.FindAll("Homework.Delete").Length;
            if (before == 0) throw new Exception("в разделе нет ни одной домашки, отменять нечего");
            ui.Click("Homework.Add");
            ui.Find("Dialog.Search");
            var picker = ui.Shot("dialog-subject-picker");
            ui.Keys(VirtualKeyShort.ESCAPE);
            if (ui.IsShown("Dialog.Search")) throw new Exception("Escape не закрыл выбор предмета");
            ui.Click("Homework.Delete");
            ui.Find("Dialog.Confirm");
            var confirm = ui.Shot("dialog-confirm");
            ui.Click("Dialog.Cancel");
            if (ui.IsShown("Dialog.Confirm")) throw new Exception("«Отмена» не закрыла подтверждение");
            // «Отмена» means cancel: the row it was aimed at is still there. Cancel was the last action of this
            // step before, so a Cancel that deleted the row passed just as well.
            var after = ui.FindAll("Homework.Delete").Length;
            if (after != before) throw new Exception($"«Отмена» изменила список: было {before} строк, стало {after}");
            return ($"SubjectPicker и Confirm показаны, «Отмена» оставила все {after} строк", picker + ", " + confirm);
        });

        Step(report, ui, "Карты: зум, этаж, полноэкран", () =>
        {
            ui.Click("Nav.Maps");
            ui.Find("Maps.Plan");
            ui.Click("MapsBuilding.0");
            ui.Click("Maps.Floor");
            var plan = ui.RegionOf("Maps.Plan");
            var fitted = ui.Signature(plan);
            var physical = ui.Shot("maps-physical-floor");
            ui.Click("Maps.ZoomIn");
            ui.Click("Maps.ZoomIn");
            var zoomed = ui.Signature(plan);
            if (Ui.Difference(fitted, zoomed) < Redrawn) throw new Exception($"зум ×2 не изменил план (разница {Ui.Difference(fitted, zoomed):0.####})");
            var zoomShot = ui.Shot("maps-zoomed");
            Console.WriteLine("MAP QA stage: fit");
            ui.Click("Maps.Fit");
            var floors = ui.FindAll("Maps.Floor");
            if (floors.Length < 4) throw new Exception($"{floors.Length} пилюль этажей: {ui.Dump("Maps.Floor")}");
            var refit = ui.Signature(plan);

            // Which floor is selected is not in the automation tree (the pill wears a CSS class), and the next
            // lesson's floor is already chosen — so the read-back is «some other floor draws another plan»,
            // tried in order until one does. Named by index as well: the pills expose their caption as Content
            // rather than as a Name, and a failure message has to say which one was pressed.
            var picked = -1;
            for (var i = 0; i < floors.Length && picked < 0; i++)
            {
                var freshFloors = ui.FindAll("Maps.Floor");
                if (i >= freshFloors.Length) break;
                var floor = freshFloors[i];
                Console.WriteLine($"MAP QA stage: floor {i}, {floor.Name}, offscreen={floor.IsOffscreen}");
                if (floor.IsOffscreen && floor.Patterns.ScrollItem.IsSupported)
                { floor.Patterns.ScrollItem.Pattern.ScrollIntoView(); Thread.Sleep(150); }
                ui.Invoke(floor);
                Thread.Sleep(150); // the plan is decoded off the UI thread after the pill is pressed
                if (Ui.Difference(refit, ui.Signature(plan)) >= Redrawn) picked = i;
            }
            if (picked < 0) throw new Exception($"ни одна из {floors.Length} пилюль этажей не сменила план: {ui.Dump("Maps.Floor")}");
            var floorShot = ui.Shot("maps-floor-2");
            // A hand-picked floor stops the tracking, which is exactly what «К следующей паре» is for.
            if (!ui.IsShown("Maps.ToNext")) throw new Exception($"после выбора этажа #{picked} пилюля «К следующей паре» не появилась");

            Console.WriteLine("MAP QA stage: fullscreen");
            ui.Click("Maps.Fullscreen");
            ui.Find("MapsFull.Close");
            var full = ui.Shot("maps-fullscreen");
            ui.Keys(VirtualKeyShort.ESCAPE);
            if (!ui.WaitFor(() => !ui.IsShown("MapsFull.Close"))) throw new Exception("Esc не закрыл полноэкранный план");

            Console.WriteLine("MAP QA stage: back to next lesson");
            ui.Click("Maps.ToNext");
            if (!ui.WaitFor(() => !ui.IsShown("Maps.ToNext")))
                throw new Exception("«К следующей паре» осталась на экране — слежение за парой не вернулось");
            return ($"зум ×2 (разница {Ui.Difference(fitted, zoomed):0.###}), этаж #{picked} из {floors.Length} перерисовал план, полноэкран открылся и закрылся по Esc, «К следующей паре» вернула слежение", string.Join(", ", physical, zoomShot, floorShot, full));
        });

        Step(report, ui, "Настройки: язык отсутствует, тест уведомления, анимации", () =>
        {
            ui.Click("Nav.Settings");
            if (ui.TryFind("SettingsLanguage", TimeSpan.FromMilliseconds(200)) is not null)
                throw new Exception("language selector remains");
            if (ui.TryFind("SettingsLanguage.0", TimeSpan.FromMilliseconds(200)) is not null)
                throw new Exception("SettingsLanguage.0 remains");
            if (ui.TryFind("SettingsLanguage.1", TimeSpan.FromMilliseconds(200)) is not null)
                throw new Exception("SettingsLanguage.1 remains");
            var settingsLabel = ui.Text("Nav.Settings");
            if (settingsLabel != "Настройки") throw new Exception($"chrome not Russian: {ui.Dump("Nav.Settings")}");
            OpenSettingsCategory(ui, "Settings.Category.Study");
            var refresh = ui.Find("Settings.Refresh").Name;
            if (refresh != "Обновить расписание") throw new Exception($"refresh chrome not Russian: «{refresh}»");
            var study = ui.Shot("settings-study");
            ui.Click("Settings.Back");
            OpenSettingsCategory(ui, "Settings.Category.Notifications");
            ui.Click("Settings.PreviewNotification");
            var preview = ui.Shot("settings-notification-preview");
            ui.Click("Settings.TestNotification");
            if (ui.TryFind("Toast", TimeSpan.FromSeconds(3)) is null) throw new Exception("no toast after «Тест уведомления»");
            var toast = ui.Shot("settings-toast");
            ui.Click("Settings.Back");
            OpenSettingsCategory(ui, "Settings.Category.Appearance");
            // Two toggles, read back twice: the switch's own state through UIA, and the preference it is
            // supposed to write, out of this run's own scratch ui.json. The first alone is not enough — a
            // ToggleButton whose IsChecked is bound to nothing still flips itself, which a deliberately
            // unbound build proved by passing this step.
            var on = ui.IsOn("Settings.Animations");
            ui.Toggle("Settings.Animations");
            if (!ui.WaitFor(() => ui.IsOn("Settings.Animations") != on)) throw new Exception($"тумблер анимаций не изменил состояние (был {on})");
            if (!WaitPref(o, "Animations", !on)) throw new Exception($"тумблер анимаций не записал Animations={!on} в ui.json: {Pref(o, "Animations")}");
            ui.Toggle("Settings.Animations");
            if (!ui.WaitFor(() => ui.IsOn("Settings.Animations") == on)) throw new Exception($"тумблер анимаций не вернулся в {on}");
            if (!WaitPref(o, "Animations", on)) throw new Exception($"тумблер анимаций не вернул Animations={on} в ui.json: {Pref(o, "Animations")}");
            var appearance = ui.Shot("settings-appearance");
            return ($"селектора языка нет, «Настройки» / «Обновить расписание», тост уведомления, анимации {on} → {!on} → {on} (и в ui.json тоже)", string.Join(", ", study, preview, toast, appearance));
        });

        Step(report, ui, "UX300: обзор зачётов и экзаменов на фактические даты", () =>
        {
            ui.Click("Nav.Week");
            ui.ClickNamedButton("Показать 28 дней");
            if (!ui.Window.FindAllDescendants().Any(element =>
                    element.Name.StartsWith("Оценок в известных датах:", StringComparison.Ordinal)))
                throw new Exception("обзор не показал результат для сохранённого расписания");
            return ("28-дневный обзор открыл результат из локальной копии", ui.Shot("ux300-assessments"));
        });

        Step(report, ui, "UX300: массовая отметка личной домашки с предпросмотром", () =>
        {
            ui.Click("Nav.Homework");
            var doneBefore = ui.Find("Homework.Done").Name;
            ui.ClickNamedButton("Выбрать несколько");
            var selecting = ui.Shot("ux300-bulk-select");
            ui.ToggleNamedCheckBox("Выбрать это задание");
            ui.ClickNamedButton("Отметить выбранные готовыми");
            var preview = ui.Shot("ux300-bulk-preview");
            ui.ClickNamedButton("Отмена");
            if (ui.Find("Homework.Done").Name != doneBefore)
                throw new Exception("отмена предпросмотра изменила отметку домашки");
            return ("одна личная задача выбрана, предпросмотр открыт и отменён без записи", selecting + ", " + preview);
        });

        Step(report, ui, "UX300: безопасный предпросмотр технической сводки", () =>
        {
            ui.Click("Nav.Settings");
            OpenSettingsCategory(ui, "Settings.Category.Help");
            ui.ClickNamedButton("Проверить сводку");
            if (!ui.Text("Settings.DiagnosticsPreview").Contains("Платформа: Windows", StringComparison.Ordinal))
                throw new Exception("техническая сводка не открылась после явного запроса");
            return ("локальная сводка видна; текст в буфер не копировался", ui.Shot("ux300-safe-diagnostics"));
        });

        if (Environment.GetEnvironmentVariable("VOGRAPH_UIVERIFY_SKIP_LAN") != "1")
        Step(report, ui, "LAN-импорт меняет расписание на экране", () =>
        {
            // Pin the day the payload renames *before* the import, so the assertion is that this card changed
            // its own name — not that some day somewhere happens to have one.
            ui.Click("Nav.Schedule");
            var (day, _) = GoToLessonDay(ui, t => t.Contains("Матан"), "день с парой «Матан»");
            ui.Click("Nav.Settings");
            OpenSettingsCategory(ui, "Settings.Category.Data");
            ui.Toggle("Settings.LanSync");
            if (!ui.WaitText("Settings.LanAddress", t => t.Contains("http://"), TimeSpan.FromSeconds(5))) throw new Exception("LAN address not shown (port 8765 busy?)");
            var address = ui.Text("Settings.LanAddress");
            var url = address[address.IndexOf("http://", StringComparison.Ordinal)..].Trim();
            var loopback = "http://127.0.0.1" + url[url.IndexOf(':', 7)..]; // same port, loopback host
            var stored = DateTime.Now;
            var payload = "{\"Version\":1,\"ExportedAt\":\"" + DateTime.UtcNow.ToString("o") + "\",\"Overrides\":[{\"SubjectRawNormalized\":\"лек высш. математ\",\"Scope\":\"global\",\"DisplayName\":\"Математика\",\"Note\":null,\"CreatedAt\":\"" + stored.AddDays(30).ToString("o") + "\"}],\"Homework\":[],\"Friends\":[],\"Settings\":{}}";
            var backup = BackupBeforeLanImport(o);
            using var http = new HttpClient();
            var resp = http.PostAsync(loopback, new StringContent(payload, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) throw new Exception($"POST {loopback} → {(int)resp.StatusCode}");
            ui.Click("Nav.Schedule");
            if (!ui.WaitText("Lesson.Title", t => t == "Математика", TimeSpan.FromSeconds(5)))
                throw new Exception($"импорт не переименовал пару в «{day}»: {ui.Dump("Lesson.Title")}");
            return ($"POST {loopback} → 200, карточка в «{day}» показывает «Математика»; копия тестовой БД: {backup}", ui.Shot("lan-import"));
        });

        Step(report, ui, "Закрытие", () =>
        {
            if (o.Keep) return ("--keep: приложение оставлено", null);
            if (!ui.CloseGracefully()) throw new Exception("the process had to be killed");
            return ("процесс завершился по ✕", null);
        });
    }

    private static void OpenSettingsCategory(Ui ui, string categoryId)
    {
        if (ui.TryFind(categoryId, TimeSpan.FromMilliseconds(250)) is null && ui.IsShown("Settings.Back"))
            ui.Click("Settings.Back");
        ui.Click(categoryId);
    }

    private static string BackupBeforeLanImport(Options o)
    {
        var sourcePath = Path.Combine(o.Data, "vograph.db");
        var backupPath = Path.Combine(o.Out, "pre-lan-import-vograph.db");
        if (!File.Exists(sourcePath) || File.Exists(backupPath))
            throw new InvalidOperationException("Тестовую базу нельзя безопасно сохранить перед импортом.");
        using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = sourcePath, Mode = SqliteOpenMode.ReadOnly }.ToString()))
        using (var backup = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = backupPath, Mode = SqliteOpenMode.ReadWriteCreate }.ToString()))
        {
            source.Open(); backup.Open(); source.BackupDatabase(backup);
        }
        if (new FileInfo(backupPath).Length < 4096)
            throw new InvalidOperationException("Копия тестовой базы перед импортом слишком мала.");
        using var check = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = backupPath, Mode = SqliteOpenMode.ReadOnly }.ToString());
        check.Open();
        using var command = check.CreateCommand();
        command.CommandText = "PRAGMA quick_check";
        if (command.ExecuteScalar() as string != "ok")
            throw new InvalidOperationException("Копия тестовой базы перед импортом не прошла quick_check.");
        return backupPath;
    }

    /// <summary>What this run's own scratch ui.json says about one boolean preference, or a note about why it
    /// could not be read. Never the real profile: <see cref="Options.Data"/> is the folder Seed built.</summary>
    private static string Pref(Options o, string key)
    {
        var path = Path.Combine(o.Data, "ui.json");
        if (!File.Exists(path)) return "ui.json ещё не записан";
        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            return json.RootElement.TryGetProperty(key, out var value) ? value.ToString() : $"нет ключа {key}";
        }
        catch (Exception ex) { return $"{ex.GetType().Name}: {ex.Message}"; } // a half-written save, or no file yet
    }

    /// <summary>The preference is saved by the app on its own schedule, so it is waited for, not sampled once.</summary>
    private static bool WaitPref(Options o, string key, bool expected)
    {
        for (var waited = 0; waited < 4000; waited += 200)
        {
            if (string.Equals(Pref(o, key), expected ? "True" : "False", StringComparison.OrdinalIgnoreCase)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>Names of the rows in the group picker's list.</summary>
    private static string[] Rows(Ui ui) =>
        ui.Find("Dialog.List").FindAllDescendants().Select(e => e.Name ?? "").Where(n => n.Length is > 0 and < 12).ToArray();

    /// <summary>
    /// Walks the schedule forward from today until a day matches, driving the segment and the arrow rather than
    /// trusting the calendar: the seed fixture carries lessons on Mon (both parities), Tue (odd), Wed and Sat
    /// only, so any step that presses HOME and expects a lesson «today» fails on a Thursday, a Friday, a Sunday
    /// or an even-week Tuesday — with a message that reads like an app regression (T12-R4). The trace it throws
    /// says what every day it looked at actually showed.
    /// </summary>
    private static (string Day, string[] Titles) GoToLessonDay(Ui ui, Func<string[], bool> want, string what)
    {
        ui.Click("ScheduleSegment.0"); // today, wherever SmartStart opened
        var trace = new List<string>();
        for (var step = 0; step <= 7; step++)
        {
            if (step > 0)
            {
                var before = ui.Text("Schedule.Title");
                ui.Click("Schedule.Next");
                if (!ui.WaitText("Schedule.Title", t => t != before)) throw new Exception($"{what}: «Вперёд» не сменил день с «{before}»");
            }
            Thread.Sleep(250); // the cards recompose right after the title
            var day = ui.Text("Schedule.Title");
            var titles = ui.FindAll("Lesson.Title").Select(e => e.Name ?? "").ToArray();
            trace.Add($"{day} — {(titles.Length == 0 ? "пар нет" : string.Join(" / ", titles))}");
            if (want(titles)) return (day, titles);
        }
        throw new Exception($"{what} не найден за неделю вперёд: {string.Join(" · ", trace)}");
    }

    private static void Step(Report report, Ui ui, string name, Func<(string Detail, string? Frame)> body)
    {
        try
        {
            var (detail, frame) = body();
            report.Pass(name, detail, frame);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            string? frame = null;
            try { frame = ui.Shot("error-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-ffff")); }
            catch (Exception capture) { Console.Error.WriteLine($"error frame unavailable: {capture.Message}"); }
            report.Fail(name, ex.Message, frame);
        }
    }
}
