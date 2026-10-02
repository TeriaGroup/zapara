using Vograph.Desktop.Features.Preferences;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SupportDiagnosticsUx300Tests : UiTest
{
    [Fact]
    public void Formatter_only_emits_allowlisted_version_and_counts()
    {
        var text = new SupportDiagnostics("token=C:\\secret", 2, 5, 3, 9, 1).Format();
        Assert.Contains("Версия: неизвестно", text);
        Assert.Contains("Платформа: Windows", text);
        Assert.Contains("Копии расписания: 2 из 5", text);
        Assert.DoesNotContain("secret", text);
        Assert.DoesNotContain("token=", text);
    }

    [Fact]
    public async Task Preview_does_not_copy_until_explicit_action_and_clears_on_group_scope_change()
    {
        using var db = TestDb.Create();
        var vm = new SettingsViewModel(db.Services, new ShellViewModel(db.Services));
        string? copied = null;
        vm.SetDiagnosticsClipboardWriter(value => { copied = value; return Task.CompletedTask; });
        await vm.PreviewDiagnosticsCommand.ExecuteAsync(null);
        Assert.True(vm.HasDiagnosticsPreview);
        Assert.Null(copied);
        Assert.DoesNotContain(TestDb.MyGroupId, vm.DiagnosticsPreview);
        Assert.DoesNotContain(db.Dir, vm.DiagnosticsPreview);
        await vm.CopyDiagnosticsCommand.ExecuteAsync(null);
        Assert.Equal(vm.DiagnosticsPreview, copied);

        copied = null;
        var settings = db.Services.Db.GetSettings(); settings.MyGroupId = "другая"; db.Services.Db.SaveSettings(settings);
        await vm.CopyDiagnosticsCommand.ExecuteAsync(null);
        Assert.Null(copied);
        Assert.False(vm.HasDiagnosticsPreview);
    }
}
