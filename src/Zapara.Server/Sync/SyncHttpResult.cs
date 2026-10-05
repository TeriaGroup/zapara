using Zapara.Contracts.Sync;

namespace Zapara.Server.Sync;

internal static class SyncHttpResult
{
    internal static IResult Json<T>(T value, int status = 200) => new FrozenJson(SyncJson.Serialize(value), status);
    internal static IResult Error(SyncError error) => Json(error, error.Status);
    internal static IResult Read<T>(SyncReadResult<T> result) where T : class
        => result.Error is { } error ? Error(error) : Json(result.Value!);

    // Serialize with the receipt codec, not ASP.NET's different escaping/date defaults.
    private sealed class FrozenJson(byte[] body, int status) : IResult
    {
        public async Task ExecuteAsync(HttpContext context)
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            context.Response.ContentLength = body.Length;
            await context.Response.Body.WriteAsync(body, context.RequestAborted);
        }
    }
}
