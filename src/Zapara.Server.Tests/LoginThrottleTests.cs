using System.Net;
using Xunit;
using Zapara.Server.Accounts;
using Zapara.Server.Admin;

namespace Zapara.Server.Tests;

public sealed class LoginThrottleTests
{
    [Theory]
    [InlineData("203.0.113.9", "4:203.0.113.9")]
    [InlineData("::ffff:203.0.113.9", "4:203.0.113.9")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "6:20010db800010002::/64")]
    [InlineData("2001:db8:1:2::1", "6:20010db800010002::/64")]
    [InlineData("2001:db8:1:3::1", "6:20010db800010003::/64")]
    public void Network_key_uses_full_ipv4_and_ipv6_slash_64(string address, string expected)
        => Assert.Equal(expected, LoginThrottle.NetworkKey(IPAddress.Parse(address)));

    [Fact]
    public void Missing_address_has_one_shared_key()
        => Assert.Equal(LoginThrottle.NetworkKey(null), LoginThrottle.NetworkKey(null));

    [Fact]
    public void Pair_limit_blocks_only_that_network_for_that_account()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++)
        {
            Assert.Null(throttle.Check("victim", "4:198.51.100.1"));
            throttle.Failed("victim", "4:198.51.100.1");
        }
        Assert.NotNull(throttle.Check("victim", "4:198.51.100.1"));
        Assert.Null(throttle.Check("victim", "4:198.51.100.2"));
        Assert.Null(throttle.Check("someone.else", "4:198.51.100.1"));
        clock.Now += LoginThrottle.Window;
        Assert.Null(throttle.Check("victim", "4:198.51.100.1"));
    }

    [Fact]
    public void Network_limit_blocks_a_network_spraying_many_accounts()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.NetworkFailureLimit; i++) throttle.Failed("user" + i, "6:20010db8000000aa::/64");
        var wait = throttle.Check("fresh.user", "6:20010db8000000aa::/64");
        Assert.Equal(LoginThrottle.Window, wait);
        Assert.Null(throttle.Check("fresh.user", "6:20010db8000000ab::/64"));
    }

    [Fact]
    public void Account_under_distributed_attack_gets_a_capped_delay_on_every_network()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < 30; i++) throttle.Failed("platform.admin", "4:192.0.2." + i);
        // Every network waits the progressive delay after the last failure, capped at one minute,
        // including a network that never failed, so fresh networks do not give extra fast guesses.
        var failing = throttle.Evaluate("platform.admin", "4:192.0.2.1");
        Assert.Equal(new LoginDecision(LoginBlock.AccountDelay, LoginThrottle.MaxAccountDelay), failing);
        Assert.Equal(LoginThrottle.MaxAccountDelay, throttle.Check("platform.admin", "4:203.0.113.50"));
        clock.Now += LoginThrottle.MaxAccountDelay;
        Assert.Null(throttle.Check("platform.admin", "4:192.0.2.1"));
        Assert.Null(throttle.Check("platform.admin", "4:203.0.113.50"));
    }

    [Fact]
    public void Success_clears_the_account_but_not_the_network_counter()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < LoginThrottle.NetworkFailureLimit - 1; i++) throttle.Failed("other" + i, "4:198.51.100.9");
        throttle.Failed("mine", "4:198.51.100.9");
        throttle.Succeeded("mine", "4:198.51.100.9");
        Assert.NotNull(throttle.Check("mine", "4:198.51.100.9"));
    }

    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    [InlineData(13, 8)]
    [InlineData(16, 60)]
    [InlineData(1000, 60)]
    public void Account_delay_is_progressive_and_capped(int failures, int seconds)
        => Assert.Equal(TimeSpan.FromSeconds(seconds), LoginThrottle.AccountDelay(failures));

    [Fact]
    public void Memory_stays_bounded()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock, capacity: 64);
        for (var i = 0; i < 1000; i++) throttle.Failed("user" + i, "4:10.0." + (i / 256) + "." + (i % 256));
        Assert.InRange(throttle.Count(), 1, 64);
    }

    [Theory]
    [InlineData("6:20010db8000100aa::/64", "6:20010db80001::/48")]
    [InlineData("6:20010db8000100ff::/64", "6:20010db80001::/48")]
    [InlineData("4:203.0.113.9", null)]
    [InlineData("none", null)]
    public void Wide_key_is_the_ipv6_slash_48(string network, string? expected)
        => Assert.Equal(expected, LoginThrottle.WideNetworkKey(network));

    [Fact]
    public void Wide_tier_blocks_a_slash_48_that_rotates_slash_64_networks()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        // A fresh /64 for every attempt, all inside 2001:db8:1::/48, against different accounts.
        for (var i = 0; i < LoginThrottle.WideNetworkFailureLimit; i++)
        {
            var network = LoginThrottle.NetworkKey(IPAddress.Parse($"2001:db8:1:{i:x}::1"));
            Assert.Null(throttle.Check("user" + i, network));
            throttle.Failed("user" + i, network);
        }
        var untouched = LoginThrottle.NetworkKey(IPAddress.Parse("2001:db8:1:ffff::1"));
        var decision = throttle.Evaluate("fresh.user", untouched);
        Assert.Equal(LoginBlock.Network, decision.Block);
        Assert.Equal(LoginThrottle.Window, decision.Wait);
        // Another /48 and IPv4 clients are unaffected.
        Assert.Null(throttle.Check("fresh.user", LoginThrottle.NetworkKey(IPAddress.Parse("2001:db8:2::1"))));
        Assert.Null(throttle.Check("fresh.user", "4:198.51.100.1"));
        clock.Now += LoginThrottle.Window;
        Assert.Null(throttle.Check("fresh.user", untouched));
    }

    [Fact]
    public void Many_networks_cannot_lock_an_account_only_space_out_attempts()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        var fresh = LoginThrottle.NetworkKey(IPAddress.Parse("2001:db8:ffff::1"));
        var guesses = 0;
        var start = clock.Now;
        // Attackers on 1000 different /48s keep failing as fast as the throttle lets them for 30 minutes.
        for (var i = 0; i < 1000 && clock.Now < start + TimeSpan.FromMinutes(30); i++)
        {
            var network = LoginThrottle.NetworkKey(IPAddress.Parse($"2001:db8:{i:x}::1"));
            if (throttle.Evaluate("platform.admin", network) is { IsAllowed: false } waiting) clock.Now += waiting.Wait;
            Assert.True(throttle.Evaluate("platform.admin", network).IsAllowed);
            throttle.Failed("platform.admin", network);
            guesses++;
            // At any moment the owner on a clean network waits at most the capped delay; never a block.
            var owner = throttle.Evaluate("platform.admin", fresh);
            Assert.NotEqual(LoginBlock.Network, owner.Block);
            Assert.InRange(owner.Wait, TimeSpan.Zero, LoginThrottle.MaxAccountDelay);
        }
        // The delay still bounds guesses spread over fresh networks: about one per minute once it is at its cap.
        Assert.InRange(guesses, LoginThrottle.AccountDelayThreshold, 60);
        // The owner gets in after waiting the delay, and other accounts are not delayed at all.
        var wait = throttle.Check("platform.admin", fresh);
        if (wait is { } w) clock.Now += w;
        Assert.Null(throttle.Check("platform.admin", fresh));
        Assert.Null(throttle.Check("other.user", fresh));
    }

    [Fact]
    public void Known_device_is_exempt_from_network_blocks_and_the_account_delay()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        const string shared = "4:198.51.100.20";
        for (var i = 0; i < LoginThrottle.NetworkFailureLimit; i++) throttle.Failed("user" + i, shared);
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++) throttle.Failed("owner", shared);
        Assert.Equal(LoginBlock.Network, throttle.Evaluate("owner", shared).Block);
        Assert.NotNull(throttle.Check("owner", shared));
        Assert.Null(throttle.Check("owner", shared, knownDevice: true));
        for (var i = 0; i < 40; i++) throttle.Failed("owner", "4:192.0.2." + i);
        Assert.Equal(LoginBlock.AccountDelay, throttle.Evaluate("owner", "4:203.0.113.9").Block);
        Assert.Null(throttle.Check("owner", "4:203.0.113.9", knownDevice: true));
    }

    [Fact]
    public void Eviction_drops_expired_and_idle_entries_but_keeps_active_blocks()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock, capacity: 64);
        // Old entries that have already expired by the time the table fills up.
        for (var i = 0; i < 15; i++) throttle.Failed("old" + i, "4:192.0.2." + i);
        clock.Now += LoginThrottle.Window;
        // An active block: one network over the pair limit for one account.
        const string blocked = "4:198.51.100.66";
        for (var i = 0; i < LoginThrottle.PairFailureLimit; i++) throttle.Failed("victim", blocked);
        Assert.NotNull(throttle.Check("victim", blocked));
        // Flood with one-off failures that would previously push the block out of the table.
        for (var i = 0; i < 5000; i++)
        {
            clock.Now += TimeSpan.FromMilliseconds(1);
            throttle.Failed("flood" + i, "4:10." + (i / 65536) + "." + (i / 256 % 256) + "." + (i % 256));
        }
        Assert.InRange(throttle.Count(), 1, 64);
        Assert.Equal(LoginBlock.Network, throttle.Evaluate("victim", blocked).Block);
    }

    [Fact]
    public void Table_of_only_active_blocks_still_has_a_hard_memory_bound()
    {
        var clock = new AccountClock();
        const int capacity = 64;
        var throttle = new LoginThrottle(clock, capacity);
        for (var n = 0; n < 200; n++)
            for (var i = 0; i < LoginThrottle.PairFailureLimit; i++)
            {
                clock.Now += TimeSpan.FromMilliseconds(1);
                throttle.Failed("victim" + n, "4:10.1." + (n / 256) + "." + (n % 256));
            }
        Assert.InRange(throttle.Count(), capacity, 2 * capacity);
        // The newest blocks survive.
        Assert.NotNull(throttle.Check("victim199", "4:10.1.0.199"));
    }

    [Fact]
    public void Admin_reauth_keys_are_per_admin_so_one_admin_cannot_block_another()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        var first = AdminAuthService.ReauthThrottleKeys(Guid.Parse("11111111-1111-4111-8111-111111111111"));
        var second = AdminAuthService.ReauthThrottleKeys(Guid.Parse("22222222-2222-4222-8222-222222222222"));
        Assert.NotEqual(first.Network, second.Network);
        Assert.NotEqual(first.Account, second.Account);
        // Far more failures than the network limit from the first admin.
        for (var i = 0; i < LoginThrottle.NetworkFailureLimit * 2; i++) throttle.Failed(first.Account, first.Network);
        Assert.NotNull(throttle.Check(first.Account, first.Network));
        Assert.Null(throttle.Check(second.Account, second.Network));
    }
}
