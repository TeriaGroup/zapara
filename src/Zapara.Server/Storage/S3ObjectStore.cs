using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Zapara.Server.Social;

namespace Zapara.Server.Storage;

public sealed record S3Target(string Endpoint, string Region, string Bucket, string AccessKey, string Secret);

public sealed class S3ObjectStore : IObjectStore
{
    private readonly S3Target target;
    private readonly HttpClient http;

    public S3ObjectStore(S3Target target, HttpClient? http = null)
    {
        this.target = target;
        this.http = http ?? new HttpClient();
    }

    public void Put(string key, byte[] bytes) => Send(HttpMethod.Put, key, bytes);
    public byte[]? Get(string key)
    {
        var response = Send(HttpMethod.Get, key, null);
        if (response is null) return null;
        return response;
    }
    public void Delete(string key) => Send(HttpMethod.Delete, key, null);

    private byte[]? Send(HttpMethod method, string key, byte[]? body)
    {
        var endpoint = new Uri(target.Endpoint.TrimEnd('/') + "/");
        var path = "/" + target.Bucket + "/" + Uri.EscapeDataString(key);
        var uri = new Uri(endpoint, path.TrimStart('/'));
        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var day = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var payload = body ?? [];
        var hash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
        var host = SignedHost(endpoint);
        var canonical = string.Join('\n', method.Method, path, "", "host:" + host, "x-amz-content-sha256:" + hash, "x-amz-date:" + amzDate, "", "host;x-amz-content-sha256;x-amz-date", hash);
        var scope = day + "/" + target.Region + "/s3/aws4_request";
        var toSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + scope + "\n" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
        var signing = Hmac(Hmac(Hmac(Hmac(Encoding.UTF8.GetBytes("AWS4" + target.Secret), day), target.Region), "s3"), "aws4_request");
        var signature = Convert.ToHexString(Hmac(signing, toSign)).ToLowerInvariant();
        using var request = new HttpRequestMessage(method, uri);
        request.Headers.Host = host;
        request.Headers.TryAddWithoutValidation("x-amz-date", amzDate);
        request.Headers.TryAddWithoutValidation("x-amz-content-sha256", hash);
        request.Headers.TryAddWithoutValidation("Authorization", "AWS4-HMAC-SHA256 Credential=" + target.AccessKey + "/" + scope + ", SignedHeaders=host;x-amz-content-sha256;x-amz-date, Signature=" + signature);
        if (body is not null) request.Content = new ByteArrayContent(body);
        using var response = http.Send(request);
        if (method == HttpMethod.Get && response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Хранилище S3 не приняло файл.");
        return method == HttpMethod.Get ? response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult() : null;
    }

    public static string SignedHost(Uri endpoint)
        => endpoint.IsDefaultPort ? endpoint.Host : endpoint.Host + ":" + endpoint.Port.ToString(CultureInfo.InvariantCulture);

    private static byte[] Hmac(byte[] key, string data)
    {
        using var hmac = new HMACSHA256(key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }
}
