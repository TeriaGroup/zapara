using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class LocRemainingKeysTests
{
    public static readonly string[] RemainingKeys =
    {
        "communityTitle", "communityJoin", "communityPending", "communityMembers", "communityStaff",
        "communityHomework", "communityAnnouncements", "communityPolls", "communityVote", "communityResults",
        "communityAccept", "communityReject", "communityEmpty", "communityNeedAccount", "communityForbidden",
        "accountExport", "accountExportDownload", "accountDelete", "accountDeleteConfirm", "accountProof",
        "accountRecoveryEmail", "accountResetPassword", "accountVk", "accountYandex", "accountLink",
        "accountUnlink", "accountIdentities",
        "syncConflictTitle", "syncKeepLocal", "syncKeepServer", "syncConflictBody", "syncExpired", "syncOffline",
    };

    [Theory]
    [MemberData(nameof(KeyCases))]
    public void Remaining_Key_Exists_And_Contains_Cyrillic(string key)
    {
        var i18n = new I18nService("ru");
        var value = i18n.T(key);
        Assert.NotEqual(key, value);
        Assert.Contains(value, c => c is >= '\u0400' and <= '\u04FF');
    }

    [Fact]
    public void Remaining_Keys_Stay_Russian_When_Stored_Language_Is_English()
    {
        var i18n = new I18nService("en");
        Assert.Equal("ru", i18n.Language);
        foreach (var key in RemainingKeys)
        {
            var value = i18n.T(key);
            Assert.NotEqual(key, value);
            Assert.Contains(value, c => c is >= '\u0400' and <= '\u04FF');
        }
    }

    public static TheoryData<string> KeyCases()
    {
        var data = new TheoryData<string>();
        foreach (var key in RemainingKeys)
            data.Add(key);
        return data;
    }
}
