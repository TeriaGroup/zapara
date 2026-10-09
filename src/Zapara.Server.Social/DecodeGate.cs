namespace Zapara.Server.Social;

/// <summary>Caps how many uploaded images are decoded at the same time. A decode can hold tens of megabytes
/// and a CPU core, so extra uploads wait asynchronously for a slot and get 503 if none frees up in time.</summary>
public sealed class DecodeGate(int maxConcurrent, TimeSpan queueTimeout)
{
    public const string BusyCode = "image_decoder_busy";

    /// <summary>Shared gate for photo and avatar uploads. Two slots keep peak decode memory well under the
    /// server container limit while still letting a second upload proceed during a slow one.</summary>
    public static DecodeGate Shared { get; } = new(2, TimeSpan.FromSeconds(20));

    private readonly SemaphoreSlim slots = new(maxConcurrent, maxConcurrent);

    public int MaxConcurrent { get; } = maxConcurrent;

    public int Available => slots.CurrentCount;

    public async Task<T> RunAsync<T>(Func<T> decode, CancellationToken ct = default)
    {
        if (!await slots.WaitAsync(queueTimeout, ct).ConfigureAwait(false)) throw new SocialException(503, BusyCode);
        try { return decode(); }
        finally { slots.Release(); }
    }
}
