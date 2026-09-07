namespace Vograph.Desktop.UiVerify;

public sealed record Options(string Exe, string Out, string Data, TimeSpan Timeout, bool Keep)
{
    public static Options Parse(string[] args)
    {
        string? exe = null, out_ = null, data = null;
        var timeout = 20;
        var keep = false;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--exe": exe = args[++i]; break;
                case "--out": out_ = args[++i]; break;
                case "--data": data = args[++i]; break;
                case "--timeout": timeout = int.Parse(args[++i]); break;
                case "--keep": keep = true; break;
                default: throw new ArgumentException($"unknown argument {args[i]}");
            }
        }
        if (exe is null || !File.Exists(exe)) throw new ArgumentException("--exe <path to Vograph.exe> is required and must exist");
        out_ ??= Path.Combine(Path.GetTempPath(), "vograph-uiverify", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        data ??= Path.Combine(out_, "data");
        return new Options(Path.GetFullPath(exe), Path.GetFullPath(out_), Path.GetFullPath(data), TimeSpan.FromSeconds(timeout), keep);
    }
}
