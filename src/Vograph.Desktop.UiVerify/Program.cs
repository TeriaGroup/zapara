using Vograph.Desktop.UiVerify;

Options o;
try { o = Options.Parse(args); }
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine("usage: Vograph.Desktop.UiVerify --exe <Vograph.exe> [--out <dir>] [--data <dir>] [--timeout <sec>] [--keep]");
    return 2;
}
Directory.CreateDirectory(o.Out);
Seed.Create(o.Data);
var report = new Report();
Ui ui;
try { ui = new Ui(o); }
catch (Exception ex)
{
    Console.Error.WriteLine($"launch failed: {ex.Message}");
    report.Fail("Запуск", ex.Message);
    report.Write(o.Out, o, 0, "(приложение не запустилось)");
    return 2;
}
using (ui)
{
    Console.WriteLine($"Vograph.exe pid {ui.Pid}, data {o.Data}, out {o.Out}");
    Scenarios.Run(ui, report, o);
}
var logs = Directory.Exists(Path.Combine(o.Data, "logs")) ? Directory.GetFiles(Path.Combine(o.Data, "logs"), "desktop-*.log") : Array.Empty<string>();
var lines = logs.SelectMany(File.ReadAllLines).ToList();
var errors = lines.Count(l => l.Contains(" ERROR "));
var warnings = lines.Count(l => l.Contains(" WARN "));
if (errors > 0) report.Fail("Лог без ERROR", $"{errors} строк ERROR");
else report.Pass("Лог без ERROR", $"{lines.Count} строк, WARN {warnings}");
report.Write(o.Out, o, ui.Pid, $"строк {lines.Count}, ERROR {errors}, WARN {warnings}\n\n```\n{string.Join('\n', lines.TakeLast(40))}\n```");
Console.WriteLine($"report: {Path.Combine(o.Out, "report.md")} — FAIL {report.Failures}");
return report.Failures == 0 ? 0 : 1;
