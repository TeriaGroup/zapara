using System.Net;
using System.Text;
using Xunit;
using static Zapara.Server.Accounts.ExternalProviders.ExternalProviderTestsSupport;

namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class ExternalProviderTestsGuards
{
    [Theory]
    [InlineData(true, false, 16)]
    [InlineData(true, false, 17)]
    [InlineData(true, true, 16)]
    [InlineData(true, true, 17)]
    [InlineData(false, false, 16)]
    [InlineData(false, false, 17)]
    [InlineData(false, true, 16)]
    [InlineData(false, true, 17)]
    public async Task Depth_guard_distinguishes_valid_object_payloads(bool vk, bool userInfo, int depth)
    {
        using var handler = ValidHandler(vk);
        var nested = "0";
        // The provider object contributes one level; only this ignored object adds depth.
        for (var level = 1; level < depth; level++) nested = "{\"nested\":" + nested + "}";
        var original = userInfo ? handler.UserJson : handler.TokenJson;
        var specimen = original[..^1] + ",\"ignored\":" + nested + "}";
        if (userInfo) handler.UserJson = specimen; else handler.TokenJson = specimen;
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http);
        if (depth == 16)
        {
            var identity = await Exchange(adapter, vk);
            AssertIdentity(identity, vk);
            Assert.Equal(2, handler.Requests.Count);
        }
        else
        {
            var error = await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, vk));
            Assert.Equal(ExternalProviderFailure.InvalidResponse, error.Failure);
            Assert.Equal(userInfo ? 2 : 1, handler.Requests.Count);
        }
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    public async Task Utf8_guard_rejects_invalid_bytes_only_inside_ignored_string(bool vk, bool userInfo, bool invalidUtf8)
    {
        using var handler = ValidHandler(vk);
        var original = userInfo ? handler.UserJson : handler.TokenJson;
        byte[] specimen = [.. Encoding.UTF8.GetBytes(original[..^1] + ",\"ignored\":\""),
            0xc3, (byte)(invalidUtf8 ? 0x28 : 0xa9), .. Encoding.UTF8.GetBytes("\"}")];
        // Only the ignored string differs: valid e-acute, or an invalid continuation byte.
        // Replacement decoding yields valid JSON and leaves every identity field untouched.
        using var content = new ExternalProviderTestsContent(specimen);
        handler.Respond = (number, _) => Task.FromResult(number == (userInfo ? 2 : 1)
            ? new HttpResponseMessage(HttpStatusCode.OK) { Content = content }
            : handler.Response(number == 1 ? handler.TokenJson : handler.UserJson));
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http);
        if (invalidUtf8)
        {
            var error = await Assert.ThrowsAsync<ExternalProviderException>(async () =>
            {
                var identity = await Exchange(adapter, vk);
                AssertIdentity(identity, vk);
                Assert.Equal(2, handler.Requests.Count);
            });
            Assert.Equal(ExternalProviderFailure.InvalidResponse, error.Failure);
            Assert.Equal(userInfo ? 2 : 1, handler.Requests.Count);
        }
        else
        {
            AssertIdentity(await Exchange(adapter, vk), vk);
            Assert.Equal(2, handler.Requests.Count);
        }
        Assert.True(content.Disposed);
    }

    private static ExternalProviderTestsHandler ValidHandler(bool vk) => new()
    {
        TokenJson = "{\"access_token\":\"ACCESS_CANARY\",\"user_id\":\"opaque-subject\"}",
        UserJson = vk
            ? "{\"user\":{\"user_id\":\"opaque-subject\",\"first_name\":\"Test\"}}"
            : "{\"id\":\"opaque-subject\",\"client_id\":\"example-id\",\"display_name\":\"Test\"}"
    };

    private static void AssertIdentity(VerifiedExternalIdentity identity, bool vk)
    {
        Assert.Equal(vk ? "vk" : "yandex", identity.Provider);
        Assert.Equal("opaque-subject", identity.Subject);
        Assert.Equal("Test", identity.DisplayName);
    }
}
