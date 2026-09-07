using System.Text;
using FlaUI.Core.WindowsAPI;

namespace Vograph.Desktop.UiVerify;

public static class Scenarios
{
    public static void Run(Ui ui, Report report, Options o)
    {
        Step(report, "Запуск", () =>
        {
            if (!ui.Window.Title.Contains("Военмех")) throw new Exception($"title «{ui.Window.Title}»");
            ui.Find("Nav.Schedule");
            return ("окно и сайдбар на месте", ui.Shot("start"));
        });

        foreach (var (key, probe) in new[] { ("Week", "WeekSegment.0"), ("Summary", "Summary.Total"), ("Teachers", "Teachers.Search"), ("Maps", "Maps.ZoomIn"), ("Friends", "Friends.Add"), ("Homework", "Homework.Add"), ("Settings", "Settings.Refresh"), ("Schedule", "Schedule.Title") })
            Step(report, $"Раздел {key}", () =>
            {
                ui.Click($"Nav.{key}");
                ui.Find(probe);
                return ($"{probe} найден", ui.Shot($"nav-{key.ToLowerInvariant()}"));
            });

        Step(report, "Расписание: стрелки, сегмент, «К сегодня», клавиши", () =>
        {
            // Anchor on today first: SmartStart opens *tomorrow* once the last lesson of the day is over, so the
            // title on load is not the one «К сегодня» comes back to. Every title read waits for the recompose —
            // a day change is a gated Core call, longer than Click's own settle.
            ui.Click("ScheduleSegment.1");
            var title0 = ui.Text("Schedule.Title");
            ui.Click("Schedule.Next");
            if (!ui.WaitText("Schedule.Title", t => t != title0)) throw new Exception("title did not change after Next");
            var title1 = ui.Text("Schedule.Title");
            ui.Click("Schedule.Today");
            if (!ui.WaitText("Schedule.Title", t => t == title0)) throw new Exception($"«К сегодня» did not return to {title0} (showing {ui.Text("Schedule.Title")})");
            ui.Click("ScheduleSegment.0");
            if (!ui.WaitText("Schedule.Title", t => t != title0)) throw new Exception("segment «Вчера» did not change the day");
            var yesterday = ui.Text("Schedule.Title");
            ui.Keys(VirtualKeyShort.HOME);
            if (!ui.WaitText("Schedule.Title", t => t == title0)) throw new Exception("Home did not return to today");
            ui.Keys(VirtualKeyShort.RIGHT);
            if (!ui.WaitText("Schedule.Title", t => t == title1)) throw new Exception($"Right did not step to {title1}");
            var afterKeys = ui.Text("Schedule.Title");
            return ($"{title0} → {title1} → {yesterday} → {afterKeys}", ui.Shot("schedule-keys"));
        });

        Step(report, "Тема и свёрнутый сайдбар", () =>
        {
            ui.Click("Shell.ThemeToggle");
            var light = ui.Shot("theme-light");
            ui.Keys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            var rail = ui.Shot("sidebar-rail");
            ui.Keys(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_B);
            ui.Click("Shell.ThemeToggle");
            return ("тема переключена туда и обратно, сайдбар свёрнут и развёрнут", rail + ", " + light);
        });

        Step(report, "Диалог выбора группы", () =>
        {
            ui.Click("Shell.GroupCard");
            ui.Find("Dialog.Search");
            ui.TypeText("09c");
            var shot = ui.Shot("dialog-group-picker");
            ui.Keys(VirtualKeyShort.ESCAPE);
            if (ui.TryFind("Dialog.Search", TimeSpan.FromSeconds(1)) is not null) throw new Exception("Escape did not close the picker");
            return ("поиск с латиницей, Escape закрывает", shot);
        });

        Step(report, "Карточка пары: переименование и домашка", () =>
        {
            ui.Click("Nav.Schedule");
            ui.Keys(VirtualKeyShort.HOME);
            var card = ui.Find("Lesson.Title");
            ui.Hover(card);
            ui.Click("Lesson.Rename");
            ui.Find("Dialog.Name");
            var rename = ui.Shot("dialog-rename");
            ui.Keys(VirtualKeyShort.ESCAPE);
            ui.Hover(ui.Find("Lesson.Title"));
            ui.Click("Lesson.Homework");
            ui.Find("Dialog.Text");
            var hw = ui.Shot("dialog-homework");
            ui.Keys(VirtualKeyShort.ESCAPE);
            return ("оба диалога открываются с hover-действий", rename + ", " + hw);
        });

        Step(report, "Домашка: выбор предмета и подтверждение удаления", () =>
        {
            ui.Click("Nav.Homework");
            ui.Click("Homework.Add");
            ui.Find("Dialog.Search");
            var picker = ui.Shot("dialog-subject-picker");
            ui.Keys(VirtualKeyShort.ESCAPE);
            ui.Click("Homework.Delete");
            ui.Find("Dialog.Confirm");
            var confirm = ui.Shot("dialog-confirm");
            ui.Click("Dialog.Cancel");
            return ("SubjectPicker и Confirm показаны, отмена работает", picker + ", " + confirm);
        });

        Step(report, "Карты: зум, этаж, полноэкран", () =>
        {
            ui.Click("Nav.Maps");
            ui.Click("Maps.ZoomIn");
            ui.Click("Maps.ZoomIn");
            var zoomed = ui.Shot("maps-zoomed");
            ui.Click("Maps.Fit");
            var floors = ui.FindAll("Maps.Floor");
            if (floors.Length < 4) throw new Exception($"{floors.Length} floor pills");
            floors[1].Click();
            Thread.Sleep(400);
            var floor = ui.Shot("maps-floor-2");
            ui.Click("Maps.Fullscreen");
            ui.Find("MapsFull.Close");
            var full = ui.Shot("maps-fullscreen");
            ui.Keys(VirtualKeyShort.ESCAPE);
            ui.Click("Maps.ToNext");
            return ("зум ×2, этаж 2, полноэкран + Esc, «К следующей паре»", string.Join(", ", zoomed, floor, full));
        });

        Step(report, "Настройки: язык, тест уведомления, анимации", () =>
        {
            ui.Click("Nav.Settings");
            ui.Click("SettingsLanguage.1");
            if (!ui.WaitText("Nav.Settings", t => t == "Settings")) throw new Exception("labels did not switch to English");
            var en = ui.Shot("settings-english");
            ui.Click("SettingsLanguage.0");
            ui.Click("Settings.TestNotification");
            if (ui.TryFind("Toast", TimeSpan.FromSeconds(3)) is null) throw new Exception("no toast after «Тест уведомления»");
            var toast = ui.Shot("settings-toast");
            ui.Toggle("Settings.Animations");
            ui.Toggle("Settings.Animations");
            return ("английские подписи, тост уведомления, тумблер анимаций", en + ", " + toast);
        });

        Step(report, "LAN-импорт меняет расписание на экране", () =>
        {
            ui.Toggle("Settings.LanSync");
            if (!ui.WaitText("Settings.LanAddress", t => t.Contains("http://"), TimeSpan.FromSeconds(5))) throw new Exception("LAN address not shown (port 8765 busy?)");
            var address = ui.Text("Settings.LanAddress");
            var url = address[address.IndexOf("http://", StringComparison.Ordinal)..].Trim();
            var loopback = "http://127.0.0.1" + url[url.IndexOf(':', 7)..]; // same port, loopback host
            var stored = DateTime.Now;
            var payload = "{\"Version\":1,\"ExportedAt\":\"" + DateTime.UtcNow.ToString("o") + "\",\"Overrides\":[{\"SubjectRawNormalized\":\"лек высш. математ\",\"Scope\":\"global\",\"DisplayName\":\"Математика\",\"Note\":null,\"CreatedAt\":\"" + stored.AddDays(30).ToString("o") + "\"}],\"Homework\":[],\"Friends\":[],\"Settings\":{}}";
            using var http = new HttpClient();
            var resp = http.PostAsync(loopback, new StringContent(payload, Encoding.UTF8, "application/json")).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) throw new Exception($"POST {loopback} → {(int)resp.StatusCode}");
            ui.Click("Nav.Schedule");
            ui.Keys(VirtualKeyShort.HOME);
            if (!ui.WaitText("Lesson.Title", t => t == "Математика", TimeSpan.FromSeconds(5))) throw new Exception("the renamed lesson did not appear");
            return ($"POST {loopback} → 200, карточка показывает «Математика»", ui.Shot("lan-import"));
        });

        Step(report, "Закрытие", () =>
        {
            if (o.Keep) return ("--keep: приложение оставлено", null);
            if (!ui.CloseGracefully()) throw new Exception("the process had to be killed");
            return ("процесс завершился по ✕", null);
        });
    }

    private static void Step(Report report, string name, Func<(string Detail, string? Frame)> body)
    {
        try
        {
            var (detail, frame) = body();
            report.Pass(name, detail, frame);
        }
        catch (Exception ex)
        {
            report.Fail(name, ex.Message);
        }
    }
}
