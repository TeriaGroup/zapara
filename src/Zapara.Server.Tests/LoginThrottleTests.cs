using System.Net;
using Xunit;
using Zapara.Server.Accounts;

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
    public void Account_under_distributed_attack_delays_failing_networks_but_not_clean_ones()
    {
        var clock = new AccountClock();
        var throttle = new LoginThrottle(clock);
        for (var i = 0; i < 30; i++) throttle.Failed("platform.admin", "4:192.0.2." + i);
        // A network that has failed before must wait the progressive delay, capped at one minute.
        var wait = throttle.Check("platform.admin", "4:192.0.2.1");
        Assert.Equal(LoginThrottle.MaxAccountDelay, wait);
        // The owner on a network without failures is not delayed at all.
        Assert.Null(throttle.Check("platform.admin", "4:203.0.113.50"));
        clock.Now += LoginThrottle.MaxAccountDelay;
        Assert.Null(throttle.Check("platform.admin", "4:192.0.2.1"));
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
}
