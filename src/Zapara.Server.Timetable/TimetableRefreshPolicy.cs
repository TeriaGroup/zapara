using Microsoft.Extensions.Configuration;

namespace Zapara.Server.Timetable;

public sealed record TimetableRefreshPolicy(bool Enabled, TimeSpan Interval, TimeSpan RetryBase, TimeSpan RetryMaximum)
{
    public static TimetableRefreshPolicy FromConfiguration(IConfiguration config)
    {
        var value = config["Timetable:Refresh:Enabled"];
        if (value is not null && !bool.TryParse(value, out _)) throw new ArgumentException("Недопустимый Timetable:Refresh:Enabled.");
        var enabled = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        if (!enabled) return new(false, TimeSpan.FromHours(1), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(15));
        var interval = Number("IntervalMinutes", 60, 15, 1440);
        var initial = Number("RetrySeconds", 30, 5, 300);
        var maximum = Number("MaximumRetrySeconds", 900, initial, 3600);
        return new(true, TimeSpan.FromMinutes(interval), TimeSpan.FromSeconds(initial), TimeSpan.FromSeconds(maximum));

        int Number(string key, int fallback, int minimum, int maximum)
        {
            var raw = config["Timetable:Refresh:" + key];
            if (raw is null) return fallback;
            if (!int.TryParse(raw, out var number) || number < minimum || number > maximum)
                throw new ArgumentException("Недопустимый Timetable:Refresh:" + key + ".");
            return number;
        }
    }

    public TimeSpan NextDelay(IngestResult result, int consecutiveFailures)
    {
        if (result.ExitCode == 0) return Interval;
        if (result.ExitCode == 3) return RetryBase;
        return TimeSpan.FromSeconds(Math.Min(RetryMaximum.TotalSeconds,
            RetryBase.TotalSeconds * Math.Pow(2, Math.Clamp(consecutiveFailures - 1, 0, 20))));
    }

    public async Task RunAsync(Func<CancellationToken, Task<IngestResult>> refresh,
        Func<TimeSpan, CancellationToken, Task> delay, Action<IngestResult> observed, CancellationToken ct)
    {
        if (!Enabled) return;
        var wait = TimeSpan.FromSeconds(5);
        var failures = 0;
        while (true)
        {
            await delay(wait, ct);
            ct.ThrowIfCancellationRequested();
            var result = await refresh(ct);
            observed(result);
            // Lost COMMIT acknowledgements require operator investigation, not a timer retry.
            if (result.FailureCode == FailureCode.PublicationUnknown) return;
            failures = result.ExitCode is 0 or 3 ? 0 : Math.Min(failures + 1, 21);
            wait = NextDelay(result, failures);
        }
    }
}
