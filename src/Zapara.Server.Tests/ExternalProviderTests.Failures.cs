using System.Net;
using Xunit;
using static Zapara.Server.Accounts.ExternalProviders.ExternalProviderTestsSupport;

namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class ExternalProviderTestsFailures
{
    [Theory]
    [InlineData("{")]
    [InlineData("<!DOCTYPE user [<!ENTITY x SYSTEM 'file:///secret'>]><user>&x;</user>")]
    [InlineData("[]")]
    [InlineData("{\"access_token\":null}")]
    [InlineData("{\"access_token\":\"\"}")]
    [InlineData("{\"error\":\"invalid_grant\",\"error_description\":\"SECRET_CANARY\"}")]
    [InlineData("{\"access_token\":\"ok\",\"access_token\":\"other\"}")]
    public async Task Invalid_token_payloads_fail_safely(string json)
    {
        using var handler = new ExternalProviderTestsHandler { TokenJson = json };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        var error = await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false));
        Assert.DoesNotContain("CANARY", error.ToString());
        Assert.Null(error.InnerException);
        Assert.Single(handler.Requests);
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData(302)]
    [InlineData(307)]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task Http_failures_stop_without_following_or_leaking(int status)
    {
        using var handler = new ExternalProviderTestsHandler();
        handler.Respond = (_, _) => Task.FromResult(handler.Response("SECRET_CANARY", (HttpStatusCode)status));
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        var error = await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false));
        Assert.Equal(ExternalProviderFailure.ProviderRejected, error.Failure);
        Assert.DoesNotContain("CANARY", error.ToString());
        Assert.Single(handler.Requests);
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(false, "{\"id\":\"subject\"}")]
    [InlineData(false, "{\"id\":\"subject\",\"client_id\":\"wrong\"}")]
    [InlineData(false, "{\"psuid\":\"subject\",\"client_id\":\"example-id\"}")]
    [InlineData(false, "{\"id\":12,\"client_id\":\"example-id\"}")]
    [InlineData(true, "{\"user\":{}}")]
    [InlineData(true, "{\"user\":{\"user_id\":\"different\"}}")]
    [InlineData(true, "{\"user\":{\"user_id\":null}}")]
    public async Task Missing_or_mismatched_identity_is_not_verified(bool vk, string json)
    {
        using var handler = new ExternalProviderTestsHandler { UserJson = json };
        if (vk) handler.TokenJson = "{\"access_token\":\"ACCESS_CANARY\",\"user_id\":\"expected\"}";
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http);
        await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, vk));
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task Malformed_payloads_and_token_bounds_are_rejected()
    {
        foreach (var json in new[] { new string(' ', 65537), new string('[', 17) + "0" + new string(']', 17),
            "{\"access_token\":\"" + new string('a', 8193) + "\"}", "{\"access_token\":\"a\\r\\nb\"}" })
        {
            using var handler = new ExternalProviderTestsHandler { TokenJson = json };
            using var http = new HttpClient(handler);
            using var adapter = Adapter(false, http);
            Assert.Equal(ExternalProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false))).Failure);
            Assert.All(handler.Contents, c => Assert.True(c.Disposed));
        }
        using var invalid = new ExternalProviderTestsContent([0xc3, 0x28]);
        using var utfHandler = new ExternalProviderTestsHandler { Respond = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = invalid }) };
        using var utfHttp = new HttpClient(utfHandler);
        using var utfAdapter = Adapter(false, utfHttp);
        Assert.Equal(ExternalProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(utfAdapter, false))).Failure);
        Assert.True(invalid.Disposed);
    }

    [Fact]
    public async Task Display_name_is_optional_untrusted_and_bounded_by_unicode_scalars()
    {
        using var handler = new ExternalProviderTestsHandler
        {
            UserJson = System.Text.Json.JsonSerializer.Serialize(new { id = "opaque", client_id = "example-id", display_name = "\u0000\u202e<>" + string.Concat(Enumerable.Repeat("\U0001f600", 90)) })
        };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        var identity = await Exchange(adapter, false);
        Assert.Equal(string.Concat(Enumerable.Repeat("\U0001f600", 80)), identity.DisplayName);
        handler.TokenJson = handler.UserJson = "{\"access_token\":\"ACCESS_CANARY\",\"id\":\"opaque\",\"client_id\":\"example-id\",\"display_name\":{\"unexpected\":true}}";
        Assert.Null((await Exchange(adapter, false)).DisplayName);
    }

    [Fact]
    public async Task Transport_exception_details_are_discarded_and_caller_cancellation_is_preserved()
    {
        using var handler = new ExternalProviderTestsHandler { Respond = (_, _) => throw new HttpRequestException("SECRET_CANARY " + Code) };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        var error = await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false));
        Assert.Equal(ExternalProviderFailure.TransportFailure, error.Failure);
        Assert.DoesNotContain("CANARY", error.ToString());
        Assert.Null(error.InnerException);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ExchangeWithCancellation(adapter, false, cancel.Token));
        Assert.Single(handler.Requests);
    }
}
