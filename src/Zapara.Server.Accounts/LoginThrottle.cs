using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Zapara.Server.Accounts;

/// <summary>
/// Failed-login throttling that never locks an account for everyone. Limits are kept per client network
/// (IPv4 address, IPv6 /64) and per (account, network) pair, so repeated failures block the network that
/// produces them. Failures against one account from many networks only add a bounded, progressive delay,
/// and that delay is skipped for networks with no recent failures. State is in memory: one server process.
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>Failure counting window; a block ends when the window that triggered it ends.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    /// <summary>Failed logins one network may make (any accounts) per window.</summary>
    public const int NetworkFailureLimit = 20;
    /// <summary>Failed logins one network may make against one account per window.</summary>
    public const int PairFailureLimit = 5;
    /// <summary>Failures of one account (all networks) after which networks with failures must wait between attempts.</summary>
    public const int AccountDelayThreshold = 10;
    public static readonly TimeSpan MaxAccountDelay = TimeSpan.FromSeconds(60);
    public const int DefaultCapacity = 100_000;

    private readonly TimeProvider clock;
    private readonly int capacity;
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<UInt128, Entry> entries = new();
    private readonly object gate = new();

    public LoginThrottle(TimeProvider clock, int capacity = DefaultCapacity)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (capacity < 16) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
    }

    /// <summary>Network key for throttling: the full IPv4 address, or the /64 prefix of an IPv6 address.</summary>
    public static string NetworkKey(IPAddress? address)
    {
        if (address is null) return "none";
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetwork) return "4:" + address;
        if (address.AddressFamily != AddressFamily.InterNetworkV6) return "none";
        var bytes = address.GetAddressBytes();
        return "6:" + Convert.ToHexString(bytes, 0, 8).ToLowerInvariant() + "::/64";
    }

    /// <summary>Returns how long the caller must wait before this attempt may be checked, or null when it may proceed.</summary>
    public TimeSpan? Check(string account, string network)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            var networkEntry = Live(Key("n", network), now);
            if (networkEntry is { Count: >= NetworkFailureLimit }) return Until(networkEntry.Started + Window, now);
            var pair = Live(Key("p", account + "\n" + network), now);
            if (pair is { Count: >= PairFailureLimit }) return Until(pair.Started + Window, now);
            // A network without recent failures is not delayed by failures that other networks caused.
            if (networkEntry is null) return null;
            var accountEntry = Live(Key("a", account), now);
            if (accountEntry is null || accountEntry.Count < AccountDelayThreshold) return null;
            var delay = AccountDelay(accountEntry.Count);
            var ready = accountEntry.Last + delay;
            return ready > now ? Until(ready, now) : null;
        }
    }

    public void Failed(string account, string network)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Count(Key("n", network), now);
            Count(Key("p", account + "\n" + network), now);
            Count(Key("a", account), now);
        }
    }

    /// <summary>A correct password clears the account and pair counters. The network counter stays: a valid login
    /// to one account must not reset failures the same network made against others.</summary>
    public void Succeeded(string account, string network)
    {
        lock (gate)
        {
            entries.Remove(Key("p", account + "\n" + network));
            entries.Remove(Key("a", account));
        }
    }

    /// <summary>1 s at the threshold, doubling per further failure, capped at <see cref="MaxAccountDelay"/>.</summary>
    public static TimeSpan AccountDelay(int failures)
    {
        if (failures < AccountDelayThreshold) return TimeSpan.Zero;
        var exponent = Math.Min(failures - AccountDelayThreshold, 16);
        var seconds = Math.Min(1L << exponent, (long)MaxAccountDelay.TotalSeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    internal int Count() { lock (gate) return entries.Count; }

    private static TimeSpan Until(DateTimeOffset end, DateTimeOffset now)
    {
        var wait = end - now;
        return wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : wait;
    }

    private Entry? Live(UInt128 key, DateTimeOffset now)
    {
        if (!entries.TryGetValue(key, out var entry)) return null;
        if (now < entry.Started + Window) return entry;
        entries.Remove(key);
        return null;
    }

    private void Count(UInt128 key, DateTimeOffset now)
    {
        var entry = Live(key, now);
        if (entry is null)
        {
            if (entries.Count >= capacity) Trim(now);
            entries[key] = new Entry { Started = now, Last = now, Count = 1 };
            return;
        }
        entry.Count = entry.Count == int.MaxValue ? int.MaxValue : entry.Count + 1;
        entry.Last = now;
    }

    /// <summary>Drops expired entries; if the table is still full, the oldest quarter goes.</summary>
    private void Trim(DateTimeOffset now)
    {
        foreach (var (key, entry) in entries.ToList())
            if (now >= entry.Started + Window) entries.Remove(key);
        if (entries.Count < capacity) return;
        foreach (var key in entries.OrderBy(pair => pair.Value.Last).Take(Math.Max(1, capacity / 4)).Select(pair => pair.Key).ToList())
            entries.Remove(key);
    }

    /// <summary>Keys are salted hashes: neither usernames nor addresses are kept in memory as plain text.</summary>
    private UInt128 Key(string kind, string value)
    {
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(salt, Encoding.UTF8.GetBytes(kind + "\0" + value), hash);
        return new UInt128(BinaryPrimitives.ReadUInt64LittleEndian(hash), BinaryPrimitives.ReadUInt64LittleEndian(hash[8..]));
    }

    private sealed class Entry
    {
        public DateTimeOffset Started;
        public DateTimeOffset Last;
        public int Count;
    }
}
