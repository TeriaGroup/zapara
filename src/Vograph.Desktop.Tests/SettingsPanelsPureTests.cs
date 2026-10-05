using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Shell;
using Vograph.Core.Services.Accounts;
using Xunit;
namespace Vograph.Desktop.Tests;

public sealed class SettingsPanelsPureTests
{
    [Fact] public void Settings_search_matches_russian_aliases_and_recovers_from_zero()
    {
        using var db = TestDb.Create(); var shell = new ShellViewModel(db.Services); var settings = new SettingsViewModel(db.Services, shell);
        settings.SettingsSearch = "  ПАРОЛЬ  ";
        Assert.Equal(1, settings.SettingsSearchCount); Assert.True(settings.ShowAccountCategory); Assert.False(settings.ShowStudyCategory);
        settings.SettingsSearch = "несуществующий раздел";
        Assert.True(settings.NoSettingsSearchResults); Assert.Equal(0, settings.SettingsSearchCount);
        settings.ClearSettingsSearchCommand.Execute(null);
        Assert.False(settings.NoSettingsSearchResults); Assert.Equal(6, settings.SettingsSearchCount);
        settings.Detach(); shell.Detach();
    }
    [Fact] public void Switching_support_threads_preserves_new_message_draft_and_selects_exact_replies()
    {
        using var db = TestDb.Create(); var shell = new ShellViewModel(db.Services); var settings = new SettingsViewModel(db.Services, shell);
        var a = new SupportThreadResponse(Guid.NewGuid(), "Первое", [new SupportLineResponse("user", "Текст A", DateTimeOffset.UtcNow)]);
        var b = new SupportThreadResponse(Guid.NewGuid(), "Второе", [new SupportLineResponse("operator", "Ответ B", DateTimeOffset.UtcNow)]);
        settings.ReportSubject = "Новый черновик"; settings.ReportBody = "Не отправлять";
        settings.ApplyReportThreads([a, b]);
        settings.SelectReportThreadCommand.Execute(settings.ReportThreads.Single(thread => thread.Id == a.Id));
        Assert.Equal("Текст A", Assert.Single(settings.ReportMessages).Body);
        settings.ReportReplyBody = "Черновик ответа A";
        settings.SelectReportThreadCommand.Execute(settings.ReportThreads.Single(thread => thread.Id == b.Id));
        Assert.Equal("Ответ B", Assert.Single(settings.ReportMessages).Body);
        Assert.Equal("", settings.ReportReplyBody);
        settings.ReportReplyBody = "Черновик ответа B";
        settings.SelectReportThreadCommand.Execute(settings.ReportThreads.Single(thread => thread.Id == a.Id));
        Assert.Equal("Черновик ответа A", settings.ReportReplyBody);
        Assert.Equal("Новый черновик", settings.ReportSubject); Assert.Equal("Не отправлять", settings.ReportBody);
        settings.Detach(); shell.Detach();
    }
    [Fact] public void Accepted_support_reply_cannot_be_replaced_by_an_older_history_read()
    {
        using var db = TestDb.Create(); var shell = new ShellViewModel(db.Services); var settings = new SettingsViewModel(db.Services, shell);
        var id = Guid.NewGuid(); var old = new SupportThreadResponse(id, "Ошибка", [new SupportLineResponse("user", "Исходное", DateTimeOffset.UtcNow)]);
        settings.ApplyReportThreads([old]);
        var staleRead = settings.ReportHistoryVersion;
        var acknowledged = new SupportThreadResponse(id, "Ошибка", [new SupportLineResponse("user", "Исходное", DateTimeOffset.UtcNow),
            new SupportLineResponse("operator", "Ответ получен", DateTimeOffset.UtcNow)]);
        settings.ApplyAcceptedReportThread(acknowledged);
        Assert.False(settings.ApplyReportHistoryIfCurrent(staleRead, [old]));
        Assert.Equal("Ответ получен", settings.ReportMessages.Last().Body);
        settings.Detach(); shell.Detach();
    }
    [Fact] public async Task Study_subgroups_use_the_actual_saved_preferences()
    {
        using var db=TestDb.Create(false);
        foreach(var (teacher,index) in new[]{("Иванов",7),("Петров",8)})db.Services.Db.InsertLesson(new(){GroupId=TestDb.MyGroupId,DayOfWeek=1,Parity=0,Index=index,TimeStart="15:00",TimeEnd="16:35",SubjectRaw="пр ИН. ЯЗ.",SubjectNormalized="ин. яз.",TeacherRaw=teacher,ClassroomRaw="100"});
        var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);await settings.ActivateAsync();var stream=Assert.Single(settings.StudySubgroups);
        await stream.Options[0].SelectCommand.ExecuteAsync(null);Assert.Single(db.Services.Db.GetSubgroupChoices(TestDb.MyGroupId));settings.Detach();shell.Detach();
    }
    [Fact] public async Task Notification_preview_has_real_content_and_does_not_show_a_notification()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell,()=>new DateTime(2026,9,13));await settings.ActivateAsync();
        await settings.PreviewNotificationCommand.ExecuteAsync(null);Assert.True(settings.NotificationPreviewVisible);Assert.NotEmpty(settings.NotificationPreview);Assert.Contains("Матан",settings.NotificationPreview);Assert.Empty(db.Services.Toasts.Items);settings.Detach();shell.Detach();
    }
    [Fact] public async Task Overview_and_back_keep_the_real_preferences()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        await settings.ActivateAsync();Assert.True(settings.ShowOverview);
        settings.OpenPanelCommand.Execute("appearance");Assert.True(settings.ShowAppearancePanel);settings.ThemeIndex=1;
        settings.BackToOverviewCommand.Execute(null);Assert.True(settings.ShowOverview);
        settings.OpenPanelCommand.Execute("appearance");Assert.Equal(1,settings.ThemeIndex);Assert.Equal(Services.ThemeChoice.Light,db.Services.Prefs.Theme);
        settings.Detach();shell.Detach();
    }
    [Fact] public async Task Invalid_notification_times_cannot_overwrite_saved_values()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        await settings.ActivateAsync();var before=db.Services.Db.GetSettings();settings.OpenPanelCommand.Execute("notifications");settings.NotifyTime1="24:30";settings.NotifyTime2="утром";
        await settings.SaveTimesCommand.ExecuteAsync(null);var after=db.Services.Db.GetSettings();Assert.Equal(before.NotifyTime1,after.NotifyTime1);Assert.Equal(before.NotifyTime2,after.NotifyTime2);
        settings.Detach();shell.Detach();
    }
    [Fact] public async Task Notification_draft_keeps_saved_summary_until_explicit_valid_save()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        await settings.ActivateAsync();settings.NotificationsEnabled=true;
        var before=settings.NotificationSummary;
        settings.NotifyTime1="20:";
        Assert.Equal(before,settings.NotificationSummary);
        Assert.False(settings.CanSaveTimes);
        settings.NotifyTime1="21:15";
        Assert.True(settings.CanSaveTimes);
        await settings.SaveTimesCommand.ExecuteAsync(null);
        Assert.Contains("21:15",settings.NotificationSummary);
        Assert.Equal("21:15",db.Services.Db.GetSettings().NotifyTime1);
        settings.NotifyTime1="21:";
        Assert.True(settings.HasNotifyTimeDraft);
        settings.DiscardNotifyTimesCommand.Execute(null);
        Assert.Equal("21:15",settings.NotifyTime1);
        Assert.False(settings.HasNotifyTimeDraft);
        settings.Detach();shell.Detach();
    }
    [Fact] public async Task Removing_one_support_attachment_preserves_the_other_and_the_message_draft()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        var dialog=new FakeFileDialogs();db.Services.FileDialogs=dialog;
        var first=Path.Combine(db.Dir,"report-a.txt");var second=Path.Combine(db.Dir,"report-b.txt");
        await File.WriteAllTextAsync(first,"first");await File.WriteAllTextAsync(second,"second");
        settings.ReportBody="Ошибка в карточке";
        dialog.OpenPath=first;await settings.PickReportLogCommand.ExecuteAsync(null);
        dialog.OpenPath=second;await settings.PickReportLogCommand.ExecuteAsync(null);
        var file=Assert.Single(settings.ReportFiles.Where(item=>item.Name=="report-a.txt"));
        settings.RemoveReportFileCommand.Execute(file);
        Assert.Equal("Ошибка в карточке",settings.ReportBody);
        Assert.Equal("report-b.txt",Assert.Single(settings.ReportFiles).Name);
        await settings.SendReportCommand.ExecuteAsync(null);
        Assert.Equal("Ошибка в карточке",settings.ReportBody);
        Assert.Single(settings.ReportFiles);
        settings.Detach();shell.Detach();
    }
    [Fact] public void Delayed_support_success_cannot_clear_a_newer_draft_revision()
    {
        Assert.True(SettingsViewModel.MayClearReportDraft(7,7));
        Assert.False(SettingsViewModel.MayClearReportDraft(7,8));
    }
    [Fact] public async Task Support_send_waits_for_a_pending_attachment_choice()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        var dialog=new FakeFileDialogs();db.Services.FileDialogs=dialog;
        var pending=new TaskCompletionSource<IReadOnlyList<string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        dialog.SupportOpen=_=>pending.Task;
        settings.ReportSubject="Ошибка";settings.ReportBody="Важный текст";
        var pick=settings.PickReportLogCommand.ExecuteAsync(null);
        Assert.True(settings.ReportAttachmentLoading);
        await settings.SendReportCommand.ExecuteAsync(null);
        Assert.Equal("Важный текст",settings.ReportBody);
        Assert.Contains("Дождитесь",settings.ReportNote);
        pending.SetResult([]);await pick;
        Assert.False(settings.ReportAttachmentLoading);
        settings.Detach();shell.Detach();
    }
    [Fact] public async Task System_reduced_motion_keeps_priority_over_the_local_toggle()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        await settings.ActivateAsync();settings.Animations=true;Assert.False(db.Services.Motion.Enabled);
        settings.OpenPanelCommand.Execute("notifications");settings.NotificationsEnabled=false;Assert.False(db.Services.Prefs.NotificationsEnabled);
        settings.BackToOverviewCommand.Execute(null);Assert.False(settings.NotificationsEnabled);settings.Detach();shell.Detach();
    }
}
