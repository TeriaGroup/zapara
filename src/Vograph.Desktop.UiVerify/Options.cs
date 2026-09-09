namespace Vograph.Desktop.UiVerify;

public sealed record Options(string Exe, string Out, string Data, TimeSpan Timeout, bool Keep)
{
    public Uri? AccountApi { get; init; }
    public bool RegistrationDisabled { get; init; }
    public string? Logout { get; init; }
    public bool MinimumKeyboard { get; init; }
    public bool AccountAccessibility { get; init; }
    public static Options Parse(string[] args)
    {
        string? exe = null, out_ = null, data = null;
        var timeout = 20;
        var keep = false;
        var registrationDisabled = false;
        var minimumKeyboard = false;
        var accessibility = false;
        string? logout = null;
        Uri? accountApi = null;
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--exe": exe = args[++i]; break;
                case "--out": out_ = args[++i]; break;
                case "--data": data = args[++i]; break;
                case "--timeout": timeout = int.Parse(args[++i]); break;
                case "--keep": keep = true; break;
                case "--account-accessibility": accessibility = true; break;
                case "--minimum-keyboard": minimumKeyboard = true; break;
                case "--registration-disabled": registrationDisabled = true; break;
                case "--logout":
                    logout = ++i < args.Length ? args[i] : null;
                    if (logout is not ("online" or "offline")) throw new ArgumentException("--logout requires online or offline");
                    break;
                case "--account-api":
                    var candidate = new Uri(args[++i]);
                    if (candidate.Scheme != "http" || candidate.Host != "127.0.0.1" || candidate.Port < 1024 ||
                        candidate.UserInfo.Length != 0 || candidate.Query.Length != 0 || candidate.Fragment.Length != 0 || candidate.AbsolutePath != "/")
                        throw new ArgumentException("Account QA requires an explicit literal loopback HTTP endpoint.");
                    accountApi = candidate;
                    break;
                default: throw new ArgumentException($"unknown argument {args[i]}");
            }
        }
        if (registrationDisabled && accountApi is null) throw new ArgumentException("--registration-disabled requires --account-api");
        if (accessibility && accountApi is null) throw new ArgumentException("--account-accessibility requires --account-api");
        if (accessibility && (registrationDisabled || logout is not null || minimumKeyboard)) throw new ArgumentException("cannot combine accessibility with other modes");
        if (logout is not null && accountApi is null) throw new ArgumentException("--logout requires --account-api");
        if (logout is not null && registrationDisabled) throw new ArgumentException("cannot combine logout and registration-disabled");
        if (minimumKeyboard && logout != "online") throw new ArgumentException("--minimum-keyboard requires --logout online");
        if (exe is null || !File.Exists(exe)) throw new ArgumentException("--exe <path to Vograph.exe> is required and must exist");
        out_ ??= Path.Combine(Path.GetTempPath(), "vograph-uiverify", DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        data ??= Path.Combine(out_, "data");
        return new Options(Path.GetFullPath(exe), Path.GetFullPath(out_), Path.GetFullPath(data), TimeSpan.FromSeconds(timeout), keep)
            { AccountApi = accountApi, RegistrationDisabled = registrationDisabled, Logout = logout, MinimumKeyboard = minimumKeyboard, AccountAccessibility = accessibility };
    }
}
