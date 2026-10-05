using Avalonia.Headless.XUnit;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.VisualTree;
using Vograph.Core.Services.Sync;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Services;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Sync;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class PrivateSyncShellResolutionTests : UiTest
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Explicit_dialog_choice_changes_the_real_profile_not_only_the_dialog_result(bool keepLocal)
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var shell = new ShellViewModel(app);
        var window = new MainWindow { DataContext = shell, Width = 1200, Height = 800 };
        window.Show();
        try
        {
            var now = new DateTimeOffset(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
            var epoch = Guid.NewGuid();
            app.Outbox.SetEpoch(epoch, 0);
            var localId = app.Homework.AddHomework("Математика", "Локальная версия", 2, now.UtcDateTime);
            var op = Assert.Single(app.Outbox.Pending());
            var serverValue = new HomeworkValue("Математика", "математика", "Серверная версия", 1, now.AddDays(-1), null);
            var record = new SyncRecord("homework", op.EntityId, 4, false, now, serverValue);
            app.Outbox.MarkConflict(op, new(409, "revision_conflict", new(epoch, 4, 0), record));
            var showing = shell.ShowSyncConflictAsync(new("homework", op.EntityId, "Состояние записи не совпало."));
            var dialog = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Pump(); // Current is assigned before the dialog's visual template is rendered.
            Assert.Contains("Локальная версия", dialog.LocalVersion);
            Assert.Contains("Серверная версия", dialog.ServerVersion);
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VOGRAPH_FRAMES_DIR")))
                Frames.Capture(window, keepLocal ? "windows-conflict-before-local" : "windows-conflict-before-server");
            var buttonId = keepLocal ? "Dialog.KeepLocal" : "Dialog.KeepServer";
            var button = window.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetAutomationId(b) == buttonId);
            Click(window, button);
            await showing;
            Assert.Empty(app.Outbox.Drafts());
            Assert.Equal(keepLocal ? "Локальная версия" : "Серверная версия", app.Homework.GetById(localId)!.Text);
            if (keepLocal)
            {
                var queued = Assert.Single(app.Outbox.Pending());
                Assert.NotEqual(op.OpId, queued.OpId);
                Assert.Equal(4, queued.ExpectedRevision);
                Assert.Equal("pending", queued.Status);
                var value = (HomeworkValue)app.Outbox.BuildMutation(queued, epoch)!.Value!;
                Assert.Equal(serverValue.CreatedAtUtc, value.CreatedAtUtc);
            }
            else Assert.Empty(app.Outbox.Pending());
        }
        finally { window.Close(); shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Expired_confirm_aborts_the_queued_row_and_does_not_replay_it()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var shell = new ShellViewModel(app);
        try
        {
            var now = new DateTimeOffset(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
            var epoch = Guid.NewGuid();
            app.Outbox.SetEpoch(epoch, 0);
            var localId = app.Homework.AddHomework("Математика", "Локальная версия", 2, now.UtcDateTime);
            var op = Assert.Single(app.Outbox.Pending());
            var serverValue = new HomeworkValue("Математика", "математика", "Серверная версия", 1, now.AddDays(-1), null);
            var record = new SyncRecord("homework", op.EntityId, 4, false, now, serverValue);
            app.Outbox.MarkConflict(op, new(409, "revision_conflict", new(epoch, 4, 0), record));
            var showing = shell.ShowSyncConflictAsync(new(op.EntityType, op.EntityId, "Снимок синхронизации устарел."));
            var dialog = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            Assert.True(dialog.IsExpired);
            dialog.ConfirmCommand.Execute(null);
            Assert.True(await showing);
            Assert.Empty(app.Outbox.Drafts());
            Assert.Empty(app.Outbox.Pending());
            Assert.Equal("Локальная версия", app.Homework.GetById(localId)!.Text);
        }
        finally { shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Expired_cancel_keeps_the_queued_row()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var shell = new ShellViewModel(app);
        try
        {
            var now = new DateTimeOffset(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
            app.Outbox.SetEpoch(Guid.NewGuid(), 0);
            app.Homework.AddHomework("Математика", "Локальная версия", 2, now.UtcDateTime);
            var op = Assert.Single(app.Outbox.Pending());
            var serverValue = new HomeworkValue("Математика", "математика", "Серверная версия", 1, now.AddDays(-1), null);
            var record = new SyncRecord("homework", op.EntityId, 4, false, now, serverValue);
            app.Outbox.MarkConflict(op, new(409, "revision_conflict", new(app.Outbox.SyncEpoch!.Value, 4, 0), record));
            var showing = shell.ShowSyncConflictAsync(new(op.EntityType, op.EntityId, "Снимок синхронизации устарел."));
            var dialog = await Waits.ForDialogAsync<SyncConflictDialogViewModel>(shell);
            dialog.CancelCommand.Execute(null);
            Assert.False(await showing);
            Assert.Single(app.Outbox.Drafts());
            Assert.Single(app.Outbox.Pending());
        }
        finally { shell.Stop(); }
    }

    [AvaloniaFact]
    public async Task Received_data_refreshes_an_already_open_homework_section_after_commit()
    {
        using var dir = new ProfileTestDirectory();
        using var app = PrivateSyncOutboxTests.OpenAccount(dir.Root);
        var shell = new ShellViewModel(app);
        try
        {
            var vm = shell.Section<Vograph.Desktop.Features.Homeworks.HomeworkViewModel>(SectionKey.Homework);
            await vm.ActivateAsync();
            Assert.Empty(vm.Groups);
            var epoch = Guid.NewGuid();
            var now = new DateTimeOffset(2026, 9, 21, 1, 30, 0, TimeSpan.Zero);
            var manifest = new SyncResyncManifest(Guid.NewGuid(), epoch, 2, now, now.AddMinutes(10), 2);
            var items = new SyncManifestItem[] {
                new(1, new("homework", Guid.NewGuid(), 1, false, now, new HomeworkValue("Математика", "математика", "Пришло с другого устройства", 1, now, null))),
                new(2, new("settings", SyncValidation.SettingsId, 2, false, now, new SettingsValue("42", false, null, null, 25, false)))
            };
            static System.Net.Http.HttpResponseMessage Json<T>(T value)
            {
                var response = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.ByteArrayContent(SyncJson.Serialize(value)) };
                response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
                return response;
            }
            using var transport = new HttpClient(new FakeHttpHandler { Respond = r => r.Method == HttpMethod.Post ? Json(manifest)
                : r.RequestUri!.AbsolutePath.Contains("/resync/") ? Json(new SyncResyncPage(manifest, 0, 2, false, items))
                : Json(new SyncChangesPage(new(epoch, 2, 0), 2, 2, false, [])) });
            using var client = new PrivateSyncHttpClient(transport, new Uri("http://127.0.0.1/"));
            app.PrivateSync!.Attach(client, _ => Task.FromResult("za_" + new string('A', 43)));
            var callbacks = 0;
            app.PrivateSync.Applied += () => { Assert.True(app.CoreGate.Wait(0)); app.CoreGate.Release(); callbacks++; };
            await app.PrivateSync.PullAsync(TestContext.Current.CancellationToken);
            await Waits.Until(() => vm.Groups.SelectMany(g => g.Items).Any(h => h.Text == "Пришло с другого устройства"), "current homework projection");
            Assert.Equal(1, callbacks);
            Assert.Empty(app.Outbox.Pending());
        }
        finally { shell.Stop(); }
    }
}
