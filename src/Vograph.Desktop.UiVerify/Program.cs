using Vograph.Desktop.UiVerify;

Options o;
try { o = Options.Parse(args); }
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("usage: Vograph.Desktop.UiVerify --exe <Vograph.exe> [--out <dir>] [--data <dir>] [--timeout <sec>] [--keep] [--account-api <loopback URL> [--registration-disabled | --logout online|offline]]");
    return 2;
}
Directory.CreateDirectory(o.Out);
var report = new Report();
try { Seed.Create(o.Data); }
catch (Exception ex)
{
    // The guard refused the folder (or the fixture could not be written): nothing was deleted, nothing launched.
    Console.Error.WriteLine($"seed failed: {ex.Message}");
    report.Fail("Фикстура", ex.Message);
    report.Write(o.Out, o, 0, "(фикстура не создана, приложение не запускалось)");
    return 2;
}
Ui ui;
try { ui = new Ui(o); }
catch (Exception ex)
{
    // The driver kills only the process it launched itself, and that kill can fail (or find it already gone):
    // the report says which of the three happened rather than announcing a stop that may not have taken place.
    var fate = ex is LaunchFailedException lf ? lf.EndText : "процесс не был запущен";
    Console.Error.WriteLine($"launch failed: {ex.Message} — {fate}");
    report.Fail("Запуск", $"{ex.Message} — {fate}");
    report.Write(o.Out, o, 0, $"(приложение не запустилось; {fate})");
    return 2;
}
using (ui)
{
    Console.WriteLine($"Vograph.exe pid {ui.Pid}, data {o.Data}, out {o.Out}");
    if (o.AccountAccessibility) AccessibilityScenario.Run(ui, report, o);
    else if (o.AccountApi is not null) AccountScenario.Run(ui, report, o);
    else Scenarios.Run(ui, report, o);
}
var logs = Directory.Exists(Path.Combine(o.Data, "logs")) ? Directory.GetFiles(Path.Combine(o.Data, "logs"), "desktop-*.log") : Array.Empty<string>();
var lines = logs.SelectMany(File.ReadAllLines).ToList();
var errors = lines.Count(l => l.Contains(" ERROR "));
var warnings = lines.Count(l => l.Contains(" WARN "));
// Our own warnings count, not just ERROR: 18 «Cannot find the appropriate transform» lines per run were the
// appear cascade silently refusing to slide, and a gate that only reads ERROR would have passed them forever
// (T12-R5). Named rather than «any WARN»: a warning from the outside world (a busy port, a missing plan) is
// news about the environment, and the step's own assertion is what judges that.
var transform = lines.Where(l => l.Contains("Cannot find the appropriate transform")).ToList();
if (errors > 0 || transform.Count > 0)
    report.Fail("Лог: без ERROR и без «Cannot find the appropriate transform»",
        $"ERROR {errors}, WARN {warnings}, из них transform-предупреждений {transform.Count}{(transform.Count > 0 ? $": {transform[0].Trim()}" : "")}");
else
    report.Pass("Лог: без ERROR и без «Cannot find the appropriate transform»", $"{lines.Count} строк, ERROR {errors}, WARN {warnings}");
report.Write(o.Out, o, ui.Pid, o.AccountAccessibility
    ? $"строк {lines.Count}, ERROR {errors}, WARN {warnings}; raw logs omitted for password probe"
    : $"строк {lines.Count}, ERROR {errors}, WARN {warnings}, transform-предупреждений {transform.Count}\n\n```\n{string.Join('\n', lines.TakeLast(40))}\n```");
Console.WriteLine($"report: {Path.Combine(o.Out, "report.md")} — FAIL {report.Failures}, ERROR {errors}, WARN {warnings} (transform {transform.Count})");
return report.Failures == 0 ? 0 : 1;
