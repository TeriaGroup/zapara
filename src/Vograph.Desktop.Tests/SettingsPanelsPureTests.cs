using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Shell;
using Xunit;
namespace Vograph.Desktop.Tests;

public sealed class SettingsPanelsPureTests
{
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
    [Fact] public async Task System_reduced_motion_keeps_priority_over_the_local_toggle()
    {
        using var db=TestDb.Create();var shell=new ShellViewModel(db.Services);var settings=new SettingsViewModel(db.Services,shell);
        await settings.ActivateAsync();settings.Animations=true;Assert.False(db.Services.Motion.Enabled);
        settings.OpenPanelCommand.Execute("notifications");settings.NotificationsEnabled=false;Assert.False(db.Services.Prefs.NotificationsEnabled);
        settings.BackToOverviewCommand.Execute(null);Assert.False(settings.NotificationsEnabled);settings.Detach();shell.Detach();
    }
}
