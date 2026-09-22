using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Zapara.Contracts.Communities;

namespace Vograph.Desktop.Features.Homeworks;

/// <summary>Resolves the signed-in membership, then asks <see cref="GroupHomework.SaveEditorAsync"/> to store the
/// editor pair before any group call. A lookup or send failure still leaves the local row.</summary>
public static class HomeworkShare
{
    public static async Task<HomeworkShareOutcome> SaveNewAsync(
        AppServices app, string subject, string text, bool share, Func<Task<string?>> readGroupId, Func<string, string, Task> saveLocal)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(readGroupId);
        ArgumentNullException.ThrowIfNull(saveLocal);
        var signedIn = false;
        var communityId = "";
        string? token = null;
        if (share && app.Communities is not null && app.CommunityAccess is not null)
        {
            try
            {
                token = await app.CommunityAccess(CancellationToken.None).ConfigureAwait(false);
                signedIn = !string.IsNullOrWhiteSpace(token);
                if (signedIn)
                {
                    var groupId = await readGroupId().ConfigureAwait(false) ?? "";
                    if (groupId.Length > 0)
                    {
                        var list = await app.Communities.ListAsync(token!, groupId).ConfigureAwait(false);
                        communityId = list.FirstOrDefault(item => !string.IsNullOrEmpty(item.Role))?.CommunityId.ToString("D") ?? "";
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception)
            {
                if (string.IsNullOrWhiteSpace(token)) signedIn = false;
                communityId = "";
            }
        }
        return await GroupHomework.SaveEditorAsync(
            new HomeworkEditorShare(subject, text, share, true),
            signedIn,
            communityId,
            saveLocal,
            async (sentSubject, sentText) =>
            {
                if (app.Communities is null || string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(communityId))
                    throw new InvalidOperationException("group");
                await app.Communities.ShareHomeworkAsync(token, Guid.Parse(communityId), new HomeworkUpsert(sentSubject, sentText, 0)).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}

internal sealed class LocalHomeworkNotStoredException : Exception;
