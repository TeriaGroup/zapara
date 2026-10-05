using System.Net;
using System.Text;
using Xunit;
using static Zapara.Server.Accounts.ExternalProviders.ExternalProviderTestsSupport;

namespace Zapara.Server.Accounts.ExternalProviders;

public sealed class ExternalProviderTestsBoundaries
{
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Oversize_actual_read_is_bounded_on_both_stages_even_with_false_length(bool vk, bool userInfo)
    {
        using var stream = new CountingStream(Encoding.UTF8.GetBytes(new string(' ', 200000)));
        using var handler = new ExternalProviderTestsHandler();
        handler.Respond = (number, _) =>
        {
            if (userInfo && number == 1) return Task.FromResult(handler.Response(handler.TokenJson));
            var content = new StreamContent(stream);
            content.Headers.ContentLength = 1;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(vk, http);
        Assert.Equal(ExternalProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, vk))).Failure);
        Assert.Equal(65537, stream.BytesRead);
        Assert.True(stream.Disposed);
        Assert.Equal(userInfo ? 2 : 1, handler.Requests.Count);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public async Task Exact_json_byte_boundary_is_enforced(int size, bool success)
    {
        var json = "{\"access_token\":\"ACCESS_CANARY\"}";
        using var handler = new ExternalProviderTestsHandler { TokenJson = json.PadRight(size) };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        if (success) Assert.Equal("opaque-subject", (await Exchange(adapter, false)).Subject);
        else Assert.Equal(ExternalProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false))).Failure);
    }

    [Theory]
    [InlineData("null", "\"123\"", "123")]
    [InlineData("123", "\"123\"", "123")]
    [InlineData("\"123\"", "123", "123")]
    [InlineData("123456789012345678901234567890", "123456789012345678901234567890", "123456789012345678901234567890")]
    public async Task Vk_nullable_token_id_and_decimal_subject_preserve_opaque_identity(string tokenId, string userId, string expected)
    {
        using var handler = new ExternalProviderTestsHandler
        {
            TokenJson = "{\"access_token\":\"ACCESS_CANARY\",\"user_id\":" + tokenId + "}",
            UserJson = "{\"user\":{\"user_id\":" + userId + "}}"
        };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(true, http);
        Assert.Equal(expected, (await Exchange(adapter, true)).Subject);
    }

    [Theory]
    [InlineData("1e2")]
    [InlineData("1.0")]
    [InlineData("-1")]
    [InlineData("true")]
    public async Task Vk_noncanonical_numeric_subject_is_rejected(string userId)
    {
        using var handler = new ExternalProviderTestsHandler { UserJson = "{\"user\":{\"user_id\":" + userId + "}}" };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(true, http);
        await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, true));
    }

    [Theory]
    [InlineData("{\"user\":")]
    [InlineData("<!DOCTYPE user><user/>")]
    [InlineData("{\"error\":\"SECRET_CANARY\"}")]
    [InlineData("{\"user\":{\"user_id\":\"a\",\"user_id\":\"b\"}}")]
    public async Task Userinfo_bad_payloads_are_not_identity(string json)
    {
        using var handler = new ExternalProviderTestsHandler { UserJson = json };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(true, http);
        var error = await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, true));
        Assert.DoesNotContain("CANARY", error.ToString());
        Assert.All(handler.Contents, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task Invalid_optional_unicode_name_is_dropped()
    {
        using var handler = new ExternalProviderTestsHandler
        {
            UserJson = "{\"id\":\"opaque\",\"client_id\":\"example-id\",\"display_name\":\"\\ud800\"}"
        };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        Assert.Null((await Exchange(adapter, false)).DisplayName);
    }

    [Fact]
    public async Task Compressed_payloads_are_rejected_without_decompression()
    {
        using var handler = new ExternalProviderTestsHandler();
        handler.Respond = (_, _) =>
        {
            var response = handler.Response(handler.TokenJson);
            response.Content.Headers.ContentEncoding.Add("gzip");
            return Task.FromResult(response);
        };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(false, http);
        Assert.Equal(ExternalProviderFailure.InvalidResponse, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, false))).Failure);
    }

    [Fact]
    public async Task Vk_token_state_mismatch_is_an_additional_consistency_check_not_callback_authentication()
    {
        using var handler = new ExternalProviderTestsHandler { TokenJson = "{\"access_token\":\"ACCESS_CANARY\",\"state\":\"wrong\"}" };
        using var http = new HttpClient(handler);
        using var adapter = Adapter(true, http);
        Assert.Equal(ExternalProviderFailure.IdentityMismatch, (await Assert.ThrowsAsync<ExternalProviderException>(() => Exchange(adapter, true))).Failure);
        Assert.Single(handler.Requests);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        internal int BytesRead { get; private set; }
        internal bool Disposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var count = await base.ReadAsync(buffer, ct);
            BytesRead += count;
            return count;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
