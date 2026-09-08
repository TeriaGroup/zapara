using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Zapara.Server.Accounts;

internal static class AccountRateLimits
{
    internal static void Add(IServiceCollection services)
    {
        // Host-local random salt. Only integers 0..4095 can become partition keys.
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
            var ip = context.Connection.RemoteIpAddress ?? IPAddress.None;
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            var hash = HMACSHA256.HashData(salt, ip.GetAddressBytes());
            var bucket = BinaryPrimitives.ReadUInt32LittleEndian(hash) & 4095;
            return RateLimitPartition.GetFixedWindowLimiter(bucket, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit, Window = window, QueueLimit = 0, AutoReplenishment = true
            });
        });
}
