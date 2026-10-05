using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SettingsUx300Tests : UiTest
{
    [Fact]
    public async Task Notification_preset_changes_draft_only_until_saved()
    {
        using var db = TestDb.Create();
        var vm = new SettingsViewModel(db.Services, new ShellViewModel(db.Services));
        await vm.LoadAsync();
        var before = db.Services.Db.GetSettings();
        var saved = (NotifyTime1: vm.NotifyTime1, NotifyTime2: vm.NotifyTime2);

        vm.UseEarlyNotifyPresetCommand.Execute(null);

        Assert.Equal(("19:00", "07:00"), (vm.NotifyTime1, vm.NotifyTime2));
        Assert.Equal((before.NotifyTime1, before.NotifyTime2),
            (db.Services.Db.GetSettings().NotifyTime1, db.Services.Db.GetSettings().NotifyTime2));
        vm.DiscardNotifyTimesCommand.Execute(null);
        Assert.Equal((saved.NotifyTime1, saved.NotifyTime2), (vm.NotifyTime1, vm.NotifyTime2));
    }

    [Fact]
    public void Support_thread_search_filters_local_subjects_without_mutating_threads()
    {
        using var db = TestDb.Create();
        var vm = new SettingsViewModel(db.Services, new ShellViewModel(db.Services));
        vm.ReportThreads.Add(new SupportThreadItem(Guid.NewGuid(), "Не открывается карта", []));
        vm.ReportThreads.Add(new SupportThreadItem(Guid.NewGuid(), "Ошибка расписания", []));

        vm.ReportThreadSearch = "КАРТА";

        Assert.Equal("Не открывается карта", Assert.Single(vm.FilteredReportThreads).Subject);
        Assert.Equal(2, vm.ReportThreads.Count);
    }

    [Fact]
    public void Discard_support_draft_requires_confirmation()
    {
        using var db = TestDb.Create();
        var vm = new SettingsViewModel(db.Services, new ShellViewModel(db.Services));
        vm.ReportSubject = "Карта";
        vm.ReportBody = "Не открывается план";

        vm.RequestDiscardReportCommand.Execute(null);
        Assert.True(vm.ConfirmDiscardReport);
        vm.CancelDiscardReportCommand.Execute(null);
        Assert.Equal("Карта", vm.ReportSubject);

        vm.RequestDiscardReportCommand.Execute(null);
        vm.DiscardReportCommand.Execute(null);
        Assert.Empty(vm.ReportSubject);
        Assert.Empty(vm.ReportBody);
    }

    [Fact]
    public void Search_result_opens_specific_setting_in_its_panel()
    {
        using var db = TestDb.Create();
        var vm = new SettingsViewModel(db.Services, new ShellViewModel(db.Services));
        vm.SettingsSearch = "время уведомления";
        var hit = Assert.Single(vm.SpecificSettingResults);

        vm.OpenSpecificSettingCommand.Execute(hit);

        Assert.Equal("notifications", vm.ActivePanel);
        Assert.Equal("NotifyTime1Control", vm.RequestedSettingAnchor);
    }
}
