using Microsoft.AspNetCore.WebUtilities;
using System.Security.Cryptography;
using System.Text;

namespace Zapara.Server.Accounts;

internal sealed class ExternalSecrets(TimeProvider clock, int capacity = 1024)
{
    private readonly object gate = new();
    private readonly Dictionary<Guid, Entry> entries = [];
    internal Guid Owner { get; } = Guid.NewGuid();
    internal void Add(Guid id, string state, string verifier, DateTimeOffset expires)
    {
        lock (gate)
        {
            Cleanup();
            if (entries.Count >= capacity) throw ExternalAuthException.Unavailable();
            entries.Add(id, new(state, verifier, expires));
        }
    }
    internal Entry Take(Guid id)
    {
        lock (gate)
        {
            Cleanup();
            if (!entries.Remove(id, out var entry)) throw ExternalAuthException.Gone();
            return entry;
        }
    }
    internal void Remove(Guid id) { lock (gate) entries.Remove(id); }
    internal void Cleanup()
    {
        lock (gate)
            foreach (var id in entries.Where(p => p.Value.Expires <= clock.GetUtcNow()).Select(p => p.Key).ToArray())
                entries.Remove(id);
    }
    internal static string Random() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
    internal static byte[] Hash(string value) => SHA256.HashData(Encoding.ASCII.GetBytes(value));
    internal static byte[] Challenge(string value)
    {
        if (value is null || value.Length != 43) throw ExternalAuthException.Invalid();
        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value);
            if (bytes.Length != 32 || WebEncoders.Base64UrlEncode(bytes) != value) throw ExternalAuthException.Invalid();
            return bytes;
        }
        catch (FormatException) { throw ExternalAuthException.Invalid(); }
    }
    internal static void Token(string? value, int min = 43, int max = 128)
    {
        if (value is null || value.Length < min || value.Length > max ||
            value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))) throw ExternalAuthException.Invalid();
    }
    internal sealed record Entry(string State, string Verifier, DateTimeOffset Expires)
    {
        public override string ToString() => "Entry { [REDACTED] }";
    }
}
