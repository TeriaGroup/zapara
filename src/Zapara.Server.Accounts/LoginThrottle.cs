using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Zapara.Server.Accounts;

/// <summary>Why a login attempt has to wait.</summary>
public enum LoginBlock
{
    None,
    /// <summary>The client network (or its wider IPv6 block, or the account on that network) has too many failures.
    /// Lasts until the window that triggered it ends.</summary>
    Network,
    /// <summary>The account has many recent failures from any networks: a short progressive delay since the last
    /// failure, capped at <see cref="LoginThrottle.MaxAccountDelay"/>. Never a full denial.</summary>
    AccountDelay
}

public readonly record struct LoginDecision(LoginBlock Block, TimeSpan Wait)
{
    public static LoginDecision Allowed => new(LoginBlock.None, TimeSpan.Zero);
    public bool IsAllowed => Block == LoginBlock.None;
}

/// <summary>
/// Failed-login throttling keyed by client network instead of a per-account lock. Failures are counted per client
/// network (IPv4 address, IPv6 /64), per wider IPv6 block (/48), per (account, network) pair and per account.
/// Repeated failures block the network that produces them. Failures of one account, from any number of networks,
/// only add a progressive delay between attempts (at most <see cref="MaxAccountDelay"/>), so no set of networks can
/// lock an account; the delay still bounds how fast guesses can be spread over many fresh networks.
/// Devices that have logged in to the account before are exempt from both (decided by the caller).
/// State is in memory: one server process.
/// </summary>
public sealed class LoginThrottle
{
    /// <summary>Failure counting window; a block ends when the window that triggered it ends.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);
    /// <summary>Failed logins one network (IPv4 address or IPv6 /64) may make, any accounts, per window.</summary>
    public const int NetworkFailureLimit = 20;
    /// <summary>Failed logins one IPv6 /48 may make, across all its /64 networks, per window.</summary>
    public const int WideNetworkFailureLimit = 60;
    /// <summary>Prefix length of the wider IPv6 tier.</summary>
    public const int WidePrefixLength = 48;
    /// <summary>Failed logins one network may make against one account per window.</summary>
    public const int PairFailureLimit = 5;
    /// <summary>Recent failures of one account (all networks) from which every attempt waits a progressive delay
    /// after the last failure.</summary>
    public const int AccountDelayThreshold = 10;
    public static readonly TimeSpan MaxAccountDelay = TimeSpan.FromSeconds(60);
    public const int DefaultCapacity = 100_000;

    private readonly TimeProvider clock;
    private readonly int capacity;
    private readonly int hardCapacity;
    private int trimAt;
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);
    private readonly Dictionary<UInt128, Entry> entries = new();
    private readonly object gate = new();

    public LoginThrottle(TimeProvider clock, int capacity = DefaultCapacity)
    {
        this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
        if (capacity < 16) throw new ArgumentOutOfRangeException(nameof(capacity));
        this.capacity = capacity;
        hardCapacity = capacity * 2;
        trimAt = capacity;
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

    /// <summary>The IPv6 /48 that contains a /64 network key, or null for IPv4 and unknown networks.</summary>
    public static string? WideNetworkKey(string network)
    {
        const int hexDigits = WidePrefixLength / 4;
        if (!network.StartsWith("6:", StringComparison.Ordinal) || network.Length < 2 + hexDigits) return null;
        return "6:" + network.Substring(2, hexDigits) + "::/" + WidePrefixLength;
    }

    /// <summary>Decides whether an attempt may be checked now and, if not, why and for how long.</summary>
    public LoginDecision Evaluate(string account, string network)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            var networkEntry = Live(Key("n", network), now);
            if (networkEntry is { Count: >= NetworkFailureLimit })
                return new(LoginBlock.Network, Until(networkEntry.Started + Window, now));
            if (WideNetworkKey(network) is { } wide && Live(Key("w", wide), now) is { Count: >= WideNetworkFailureLimit } wideEntry)
                return new(LoginBlock.Network, Until(wideEntry.Started + Window, now));
            var pair = Live(Key("p", account + "\n" + network), now);
            if (pair is { Count: >= PairFailureLimit })
                return new(LoginBlock.Network, Until(pair.Started + Window, now));
            // Applies to every network, including ones without failures, so many fresh IPv6 networks cannot add up
            // to unlimited fast guesses. It only spaces attempts out; it never refuses the account outright.
            var accountEntry = Live(Key("a", account), now);
            if (accountEntry is null || accountEntry.Count < AccountDelayThreshold) return LoginDecision.Allowed;
            var ready = accountEntry.Last + AccountDelay(accountEntry.Count);
            return ready > now ? new(LoginBlock.AccountDelay, Until(ready, now)) : LoginDecision.Allowed;
        }
    }

    /// <summary>Returns how long the caller must wait before this attempt may be checked, or null when it may proceed.</summary>
    /// <param name="knownDevice">The attempt comes from a device that has logged in to this account before:
    /// it is exempt from network blocks and from the account delay.</param>
    public TimeSpan? Check(string account, string network, bool knownDevice = false)
    {
        if (knownDevice) return null;
        var decision = Evaluate(account, network);
        return decision.IsAllowed ? null : decision.Wait;
    }

    public void Failed(string account, string network)
    {
        var now = clock.GetUtcNow();
        lock (gate)
        {
            Count(Key("n", network), NetworkFailureLimit, now);
            if (WideNetworkKey(network) is { } wide) Count(Key("w", wide), WideNetworkFailureLimit, now);
            Count(Key("p", account + "\n" + network), PairFailureLimit, now);
            Count(Key("a", account), AccountDelayThreshold, now);
        }
    }

    /// <summary>A correct password clears the account and pair counters. Network counters stay: a valid login
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

    private void Count(UInt128 key, int activeAt, DateTimeOffset now)
    {
        var entry = Live(key, now);
        if (entry is null)
        {
            if (entries.Count >= trimAt) Trim(now);
            entries[key] = new Entry { Started = now, Last = now, Count = 1, ActiveAt = activeAt };
            return;
        }
        entry.Count = entry.Count == int.MaxValue ? int.MaxValue : entry.Count + 1;
        entry.Last = now;
    }

    /// <summary>
    /// Makes room when the table is full. Expired entries go first, then the least recently used entries that do not
    /// block or delay anyone (the newest quarter of those stays). Active blocks are kept, so flooding the table cannot
    /// lift them; the table may then grow up to twice its capacity, and only past that are the least recently used
    /// entries of any kind dropped to bound memory.
    /// </summary>
    private void Trim(DateTimeOffset now)
    {
        var target = capacity - capacity / 4;
        foreach (var (key, entry) in entries.ToList())
            if (now >= entry.Started + Window) entries.Remove(key);
        if (entries.Count > target)
        {
            // The newest idle entries are kept even when the table is crowded with active blocks: they are counters
            // that are still building up, and dropping them would let a new source fail without ever being counted.
            var idle = entries.Where(pair => !pair.Value.Active).OrderBy(pair => pair.Value.Last).Select(pair => pair.Key).ToList();
            var removable = Math.Max(0, idle.Count - capacity / 4);
            foreach (var key in idle.Take(Math.Min(removable, entries.Count - target)))
                entries.Remove(key);
        }
        if (entries.Count >= hardCapacity)
        {
            foreach (var key in entries.OrderBy(pair => pair.Value.Last).Take(entries.Count - (hardCapacity - capacity / 4))
                         .Select(pair => pair.Key).ToList())
                entries.Remove(key);
        }
        // When only active entries are left, wait for some growth before scanning again instead of on every insert.
        trimAt = entries.Count < capacity ? capacity : Math.Min(hardCapacity, entries.Count + Math.Max(1, capacity / 8));
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
        /// <summary>Count from which this entry blocks or delays logins.</summary>
        public int ActiveAt;
        public bool Active => Count >= ActiveAt;
    }
}
