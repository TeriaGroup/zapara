using System.Text;

namespace Vograph.Desktop.UiVerify;

public sealed class Report
{
    private readonly List<(int N, string Step, bool Pass, string Detail, string? Frame)> _rows = new();
    public int Failures => _rows.Count(r => !r.Pass);

    public void Pass(string step, string detail = "", string? frame = null) => _rows.Add((_rows.Count + 1, step, true, detail, frame));
    public void Fail(string step, string detail, string? frame = null)
    {
        _rows.Add((_rows.Count + 1, step, false, detail, frame));
        Console.Error.WriteLine($"FAIL {step}: {detail}");
    }

    public void Write(string outDir, Options o, int pid, string logSummary)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# UiVerify — {DateTime.Now:yyyy-MM-dd HH:mm}");
        sb.AppendLine();
        sb.AppendLine($"exe: `{o.Exe}`  ·  data: `{o.Data}`  ·  pid: {pid}  ·  PASS {_rows.Count - Failures} / FAIL {Failures}");
        sb.AppendLine();
        sb.AppendLine("| # | Шаг | Результат | Детали | Кадр |");
        sb.AppendLine("|---|---|---|---|---|");
        foreach (var r in _rows) sb.AppendLine($"| {r.N} | {r.Step} | {(r.Pass ? "PASS" : "FAIL")} | {r.Detail.Replace("|", "/")} | {(r.Frame is null ? "" : $"`frames/{r.Frame}`")} |");
        sb.AppendLine();
        sb.AppendLine("## Лог приложения");
        sb.AppendLine();
        sb.AppendLine(logSummary);
        File.WriteAllText(Path.Combine(outDir, "report.md"), sb.ToString(), Encoding.UTF8);
    }
}
