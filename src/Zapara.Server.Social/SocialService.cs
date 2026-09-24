using System.Text;
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

public sealed class SocialService(IAccountUnitOfWork trustedAccounts, SocialConfiguration configuration, MediaStore media, QuotaLedger ledger, IObjectStore objects, StudentUpload uploads)
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

    public async Task<SocialMessageResponse> SendTextAsync(string token, Guid conversationId, string body, Guid? replyTo = null, CancellationToken ct = default)
    {
        var message = await Run(token, db => db.SendTextAsync(conversationId, body, replyTo), ct);
        return WriteText(message);
    }

    public Task<SocialMessageResponse> SendCardAsync(string token, Guid conversationId, string body, Guid? replyTo = null, CancellationToken ct = default)
        => Run(token, db => db.SendCardAsync(conversationId, body, replyTo), ct);

    public Task<SocialMessageResponse> SendStickerAsync(string token, Guid conversationId, string sticker, Guid? replyTo = null, CancellationToken ct = default)
        => Run(token, db => db.SendStickerAsync(conversationId, sticker, replyTo), ct);

    public async Task<SocialMessageResponse> EditAsync(string token, Guid conversationId, Guid messageId, string body, CancellationToken ct = default)
    {
        var message = await Run(token, db => db.EditAsync(conversationId, messageId, body), ct);
        return WriteText(message);
    }

    public async Task<SocialMessageResponse> DeleteAsync(string token, Guid conversationId, Guid messageId, CancellationToken ct = default)
    {
        var names = await Run(token, db => db.AttachmentNamesAsync(conversationId, messageId), ct);
        var message = await Run(token, db => db.DeleteAsync(conversationId, messageId), ct);
        foreach (var name in names)
        {
            try { media.Delete(name); }
            catch (SocialException) { /* A name the store rejects is already absent. */ }
        }
        try { objects.Delete(ContentNames.Text(messageId)); }
        catch (Exception) { /* Removing the text object is best-effort after the message is deleted. */ }
        return message;
    }

    public Task<SocialMessageResponse> ReactAsync(string token, Guid conversationId, Guid messageId, string emoji, CancellationToken ct = default)
        => Run(token, db => db.ReactAsync(conversationId, messageId, emoji), ct);

    public async Task<SocialMessageResponse> SendImageAsync(string token, Guid conversationId, byte[] input, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (bytes, width, height) = PhotoCompressor.Compress(input);
        var stored = Guid.NewGuid().ToString("N") + ".webp";
        return await Keep(token, bytes.LongLength, null, stored, bytes, db => db.SendFileAsync(conversationId, "image", stored, "Фото.webp", "image/webp", bytes.Length, width, height, replyTo), ct);
    }

    public async Task<SocialMessageResponse> SendDocumentAsync(string token, Guid conversationId, string? fileName, byte[] input, Guid? replyTo = null, CancellationToken ct = default)
    {
        var clean = DocumentPolicy.CleanName(fileName, input.LongLength);
        var stored = Guid.NewGuid().ToString("N") + Path.GetExtension(clean).ToLowerInvariant();
        return await Keep(token, input.LongLength, null, stored, input, db => db.SendFileAsync(conversationId, "file", stored, clean, DocumentPolicy.ContentType(clean), input.LongLength, null, null, replyTo), ct);
    }

    public async Task<SocialMessageResponse> SendVoiceAsync(string token, Guid conversationId, byte[] input, int? durationMs, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (type, extension) = VoicePolicy.Inspect(input);
        durationMs = VoicePolicy.Duration(durationMs);
        var stored = Guid.NewGuid().ToString("N") + extension;
        return await Keep(token, input.LongLength, null, stored, input, db => db.SendFileAsync(conversationId, "voice", stored, "Голосовое" + extension, type, input.LongLength, null, null, replyTo, durationMs), ct);
    }

    public async Task<SocialMessageResponse> SendCircleAsync(string token, Guid conversationId, byte[] input, int? durationMs, Guid? replyTo = null, CancellationToken ct = default)
    {
        var (type, extension) = CirclePolicy.Inspect(input);
        durationMs = CirclePolicy.Duration(durationMs);
        var stored = Guid.NewGuid().ToString("N") + extension;
        return await Keep(token, input.LongLength, null, stored, input, db => db.SendFileAsync(conversationId, "circle", stored, "Кружок" + extension, type, input.LongLength, null, null, replyTo, durationMs), ct);
    }

    public async Task<SocialDownload> OpenAsync(string token, Guid attachmentId, CancellationToken ct = default)
    {
        var meta = await Run(token, db => db.OpenAsync(attachmentId), ct);
        var inline = meta.Type.StartsWith("image/", StringComparison.Ordinal) || meta.Type.StartsWith("audio/", StringComparison.Ordinal) || meta.Type.StartsWith("video/", StringComparison.Ordinal);
        return new SocialDownload(media.Open(meta.Stored), meta.Name, meta.Type, inline);
    }

    private async Task<T> Keep<T>(string token, long bytes, string? groupId, string stored, byte[] payload, Func<SocialRepository, Task<T>> write, CancellationToken ct)
    {
        _ = bytes;
        await uploads.Accept(trustedAccounts, token, groupId, stored, payload, ct);
        try { return await Run(token, write, ct); }
        catch
        {
            try { media.Delete(stored); } catch (SocialException) { }
            await ledger.Release(trustedAccounts, token, payload.LongLength, groupId, ct);
            throw;
        }
    }

    private SocialMessageResponse WriteText(SocialMessageResponse message)
    {
        if (message.Deleted || message.Kind != "text" || string.IsNullOrEmpty(message.Body)) return message;
        var bytes = Encoding.UTF8.GetBytes(message.Body);
        try { objects.Put(ContentNames.Text(message.MessageId), bytes); }
        catch (Exception) { /* The committed database row is the source of truth. */ }
        return message;
    }

    private Task<T> Run<T>(string token, Func<SocialRepository, Task<T>> operation, CancellationToken ct)
        => trustedAccounts.ExecuteAsync(token, (context, token) => operation(new(context, configuration.QuotedSchema, configuration.QuotedAccounts, token)), ct);
}
