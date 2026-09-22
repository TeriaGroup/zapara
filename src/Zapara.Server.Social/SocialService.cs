using Zapara.Contracts.Social;
using Zapara.Server.Accounts;

namespace Zapara.Server.Social;

public sealed class SocialDownload(Stream stream, string name, string type, bool image)
{
    public Stream Stream { get; } = stream;
    public string Name { get; } = name;
    public string Type { get; } = type;
    public bool Image { get; } = image;
}

public sealed class SocialService(IAccountUnitOfWork trustedAccounts, SocialConfiguration configuration, MediaStore media)
{
    public Task<SocialHomeResponse> HomeAsync(string token, CancellationToken ct = default)
        => Run(token, db => db.HomeAsync(), ct);

    public Task<SocialHomeResponse> InviteAsync(string token, string code, CancellationToken ct = default)
        => Run(token, db => db.InviteAsync(code), ct);

    public Task<SocialHomeResponse> AcceptAsync(string token, Guid friendshipId, CancellationToken ct = default)
        => Run(token, db => db.AcceptAsync(friendshipId), ct);

    public Task<SocialHomeResponse> DeclineAsync(string token, Guid friendshipId, CancellationToken ct = default)
        => Run(token, db => db.DeclineAsync(friendshipId), ct);

    public Task<SocialPageResponse> MessagesAsync(string token, Guid conversationId, Guid? before, CancellationToken ct = default)
        => Run(token, db => db.MessagesAsync(conversationId, before), ct);

    public Task<SocialMessageResponse> SendTextAsync(string token, Guid conversationId, string body, Guid? replyTo = null, CancellationToken ct = default)
        => Run(token, db => db.SendTextAsync(conversationId, body, replyTo), ct);

    public Task<SocialMessageResponse> SendCardAsync(string token, Guid conversationId, string body, Guid? replyTo = null, CancellationToken ct = default)
        => Run(token, db => db.SendCardAsync(conversationId, body, replyTo), ct);

    public Task<SocialMessageResponse> SendStickerAsync(string token, Guid conversationId, string sticker, Guid? replyTo = null, CancellationToken ct = default)
        => Run(token, db => db.SendStickerAsync(conversationId, sticker, replyTo), ct);

    public Task<SocialMessageResponse> EditAsync(string token, Guid conversationId, Guid messageId, string body, CancellationToken ct = default)
        => Run(token, db => db.EditAsync(conversationId, messageId, body), ct);

    public Task<SocialMessageResponse> DeleteAsync(string token, Guid conversationId, Guid messageId, CancellationToken ct = default)
        => Run(token, db => db.DeleteAsync(conversationId, messageId), ct);

    public Task<SocialMessageResponse> ReactAsync(string token, Guid conversationId, Guid messageId, string emoji, CancellationToken ct = default)
        => Run(token, db => db.ReactAsync(conversationId, messageId, emoji), ct);

    public async Task<SocialMessageResponse> SendImageAsync(string token, Guid conversationId, byte[] input, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (bytes, width, height) = PhotoCompressor.Compress(input);
        var stored = Guid.NewGuid().ToString("N") + ".webp";
        media.Save(stored, bytes);
        try
        {
            return await Run(token, db => db.SendFileAsync(conversationId, "image", stored, "Фото.webp", "image/webp", bytes.Length, width, height, replyTo), ct);
        }
        catch
        {
            media.Delete(stored);
            throw;
        }
    }

    public async Task<SocialMessageResponse> SendDocumentAsync(string token, Guid conversationId, string? fileName, byte[] input, Guid? replyTo = null, CancellationToken ct = default)
    {
        var clean = DocumentPolicy.CleanName(fileName, input.LongLength);
        var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(clean).ToLowerInvariant();
        media.Save(stored, input);
        try
        {
            return await Run(token, db => db.SendFileAsync(conversationId, "file", stored, clean, DocumentPolicy.ContentType(clean), input.LongLength, null, null, replyTo), ct);
        }
        catch
        {
            media.Delete(stored);
            throw;
        }
    }

    public async Task<SocialMessageResponse> SendVoiceAsync(string token, Guid conversationId, byte[] input, int? durationMs, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (type, extension) = VoicePolicy.Inspect(input);
        durationMs = VoicePolicy.Duration(durationMs);
        var stored = Guid.NewGuid().ToString("N") + extension;
        media.Save(stored, input);
        try
        {
            return await Run(token, db => db.SendFileAsync(conversationId, "voice", stored, "Голосовое" + extension, type, input.LongLength, null, null, replyTo, durationMs), ct);
        }
        catch
        {
            media.Delete(stored);
            throw;
        }
    }

    public async Task<SocialMessageResponse> SendCircleAsync(string token, Guid conversationId, byte[] input, int? durationMs, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (type, extension) = CirclePolicy.Inspect(input);
        durationMs = CirclePolicy.Duration(durationMs);
        var stored = Guid.NewGuid().ToString("N") + extension;
        media.Save(stored, input);
        try
        {
            return await Run(token, db => db.SendFileAsync(conversationId, "circle", stored, "Кружок" + extension, type, input.LongLength, null, null, replyTo, durationMs), ct);
        }
        catch
        {
            media.Delete(stored);
            throw;
        }
    }

    public async Task<SocialDownload> OpenAsync(string token, Guid attachmentId, CancellationToken ct = default)
    {
        var meta = await Run(token, db => db.OpenAsync(attachmentId), ct);
        var inline = meta.Type.StartsWith("image/", StringComparison.Ordinal) || meta.Type.StartsWith("audio/", StringComparison.Ordinal) || meta.Type.StartsWith("video/", StringComparison.Ordinal);
        return new SocialDownload(media.Open(meta.Stored), meta.Name, meta.Type, inline);
    }

    private Task<T> Run<T>(string token, Func<SocialRepository, Task<T>> operation, CancellationToken ct)
        => trustedAccounts.ExecuteAsync(token, (context, token) => operation(new(context, configuration.QuotedSchema, configuration.QuotedAccounts, token)), ct);
}
