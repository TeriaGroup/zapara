using Vograph.Core.Services.Sync;
using Vograph.Desktop.Services.Profiles;
using Zapara.Contracts.Sync;
using Xunit;
using static Vograph.Desktop.Tests.PrivateSyncOutboxTests;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncProjectionTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);

    [Fact]
    public void Remote_records_create_all_five_user_visible_projections_without_echoing_to_outbox()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var homework = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var over = Guid.NewGuid();
        app.Outbox.ApplyLive(new("homework", homework, 1, false, Created,
            new HomeworkValue("пр МАТЕМАТИКА", "пр математика", "С другого устройства\nВторая строка", 2, Created, new DateOnly(2026, 9, 19))));
        app.Outbox.ApplyLive(new("completion", homework, 2, false, Created, new CompletionValue(true, Created)));
        app.Outbox.ApplyLive(new("friend", friend, 3, false, Created, new FriendValue("42", "О732Б", "Два друга", 2, true)));
        app.Outbox.ApplyLive(new("override", over, 4, false, Created,
            new OverrideValue("пр МАТЕМАТИКА", "пр математика", "global", "Математика", "Примечание", Created)));
        app.Outbox.ApplyLive(new("settings", SyncValidation.SettingsId, 5, false, Created,
            new SettingsValue("42", true, "19:11", "07:30", 75, true)));

        var hw = Assert.Single(app.Homework.GetAll());
        Assert.Equal(homework, hw.EntityId);
        Assert.Equal("пр математика", hw.SubjectRawNormalized);
        Assert.Equal("С другого устройства\nВторая строка", hw.Text);
        Assert.Equal("done", hw.Status);
        Assert.Equal(new DateTime(2026, 9, 19), hw.CreatedAt.Date);
        Assert.Equal(Created, hw.CreatedAtUtc);
        Assert.Equal(1, hw.Revision);
        var fr = Assert.Single(app.Db.GetFriends());
        Assert.Equal(friend, fr.EntityId);
        Assert.Equal("Два друга", fr.MemberNames);
        Assert.Equal(3, fr.Revision);
        var ov = Assert.Single(app.Db.GetOverrides());
        Assert.Equal("Математика", ov.DisplayName);
        Assert.Equal(over, ov.EntityId);
        Assert.Equal("42", app.Settings.MyGroupId);
        Assert.Equal("19:11", app.Settings.NotifyTime1);
        Assert.Equal(5, app.Settings.Revision);
        Assert.Empty(app.Outbox.Pending());
    }

    [Fact]
    public void Remote_edits_preserve_local_row_identity_creation_and_device_only_preferences()
    {
        using var dir = new ProfileTestDirectory();
        using var app = OpenAccount(dir.Root);
        var settings = app.Settings;
        settings.MapPanelWidth = 515;
        settings.Language = "ru";
        app.Db.SaveSettings(settings);
        var id = Guid.NewGuid();
        var value = new HomeworkValue("Математика", "математика", "Первая", 1, Created, null);
        app.Outbox.ApplyLive(new("homework", id, 1, false, Created, value));
        var local = Assert.Single(app.Homework.GetAll());
        app.Outbox.ApplyLive(new("homework", id, 2, false, Created,
            new HomeworkValue("Математика", "математика", "Вторая", 3, Created, null)));
        Assert.Equal(local.Id, Assert.Single(app.Homework.GetAll()).Id);
        Assert.Equal("Вторая", app.Homework.GetAll()[0].Text);
        app.Outbox.ApplyLive(new("settings", SyncValidation.SettingsId, 1, false, Created,
            new SettingsValue("О3313", false, null, null, 25, false)));
        Assert.Equal(515, app.Settings.MapPanelWidth);
        Assert.Empty(app.Outbox.Pending());
        app.Outbox.ApplyLive(new("homework", id, 3, true, Created, null));
        Assert.Empty(app.Homework.GetAll());
    }
}
