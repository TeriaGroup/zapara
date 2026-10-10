using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Zapara.Server.Accounts;

internal static class AccountRateLimits
{
    internal static void Add(IServiceCollection services)
    {
        // Host-local random salt. Partition keys are 64-bit salted hashes of the client network
        // (IPv4 address or IPv6 /64), so addresses are not kept in plain text and unrelated clients do not share buckets.
        // Idle partitions are dropped by the limiter itself.
        var salt = RandomNumberGenerator.GetBytes(32);
        services.AddRateLimiter(options =>
        {
            AddPolicy(options, "account-register", 5, TimeSpan.FromHours(1), salt);
            AddPolicy(options, "account-login", 10, TimeSpan.FromMinutes(1), salt);
            AddPolicy(options, "account-refresh", 60, TimeSpan.FromMinutes(1), salt);
            AddPolicy(options, "account-other", 120, TimeSpan.FromMinutes(1), salt);
            options.OnRejected = async (context, _) =>
            {
                // Fixed-window leases supply metadata; one hour is a conservative bounded fallback.
                var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
                    ? Math.Clamp((int)Math.Ceiling(retry.TotalSeconds), 1, 3600) : 3600;
                context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                await AccountErrors.Write(context.HttpContext, 429, "rate_limited");
            };
        });
    }

    private static void AddPolicy(RateLimiterOptions options, string name, int limit, TimeSpan window, byte[] salt)
        => options.AddPolicy(name, context =>
        {
            var network = LoginThrottle.NetworkKey(context.Connection.RemoteIpAddress);
            var hash = HMACSHA256.HashData(salt, System.Text.Encoding.ASCII.GetBytes(network));
            var bucket = BinaryPrimitives.ReadUInt64LittleEndian(hash);
            return RateLimitPartition.GetFixedWindowLimiter(bucket, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit, Window = window, QueueLimit = 0, AutoReplenishment = true
            });
        });
}
