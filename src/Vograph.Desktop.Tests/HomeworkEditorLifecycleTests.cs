using Vograph.Desktop.Dialogs;
using Vograph.Core.Services;
using Vograph.Desktop.Services;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Core.Services.Communities;
using static Vograph.Desktop.Tests.CommunityClientTestSupport;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkEditorLifecycleTests
{
    public HomeworkEditorLifecycleTests() => Loc.Init(new I18nService());

    [Fact]
    public async Task Dismiss_Changed_Draft_Keeps_Editor_Open()
    {
        var dialog = new HomeworkDialogViewModel("Матан", _ => null);
        dialog.Text = "задачи";
        dialog.Cancel();
        Assert.False(dialog.Completion.IsCompleted);
        Assert.True(dialog.ShowDiscardConfirmation);
        dialog.KeepEditingCommand.Execute(null);
        Assert.False(dialog.ShowDiscardConfirmation);
        Assert.Equal("задачи", dialog.Text);
        dialog.Cancel();
        dialog.DiscardCommand.Execute(null);
        Assert.False(await dialog.Completion);
    }

    [Fact]
    public async Task Import_Blocks_Save_And_Close_Until_Attachment_Is_Ready()
    {
        var imported = new TaskCompletionSource<HomeworkAttachment?>();
        var dialog = new HomeworkDialogViewModel("Матан", _ => null, "задачи")
        {
            Import = _ => imported.Task
        };
        var importing = dialog.AddPhotoCommand.ExecuteAsync(null);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        dialog.Cancel();
        Assert.False(dialog.Completion.IsCompleted);
        imported.SetResult(new("photo", "photo", "photo.jpg", true));
        await importing;
        Assert.Single(dialog.Files);
    }

    [Fact]
    public void Reverting_Text_Count_Share_And_Files_Makes_Draft_Clean()
    {
        var dialog = new HomeworkDialogViewModel("Матан", _ => null, "задачи", 2) { CanShare = true };
        var file = new HomeworkAttachment("existing", "document", "list.txt", false);
        dialog.Files.Add(file);
        dialog.CaptureInitialState();
        dialog.Text = "другое";
        dialog.Nth = 3;
        dialog.Share = true;
        dialog.Files.Clear();
        Assert.True(dialog.IsDirty);
        dialog.Text = "задачи";
        dialog.Nth = 2;
        dialog.Share = false;
        dialog.Files.Add(file);
        Assert.False(dialog.IsDirty);
        dialog.Cancel();
        Assert.True(dialog.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Save_Retains_Host_Gates_All_Mutations_And_Closes_After_Persistence()
    {
        var saved = new TaskCompletionSource();
        var dialog = new HomeworkDialogViewModel("Матан", _ => null, "задачи")
        { CanShare = true, PersistAsync = () => saved.Task };
        var file = new HomeworkAttachment("file", "document", "list.txt", true);
        dialog.Files.Add(file);
        var host = new DialogHostViewModel();
        var shown = host.ShowAsync(dialog);
        var saving = dialog.SaveAsync();
        Assert.True(dialog.IsSaving);
        Assert.True(host.IsOpen);
        Assert.False(dialog.ConfirmCommand.CanExecute(null));
        Assert.False(dialog.AddDocumentCommand.CanExecute(null));
        dialog.Text = "потерять";
        dialog.Nth = 8;
        dialog.Share = true;
        dialog.RemoveFileCommand.Execute(file);
        dialog.Cancel();
        dialog.Confirm();
        Assert.Equal("задачи", dialog.Text);
        Assert.Equal(1, dialog.Nth);
        Assert.False(dialog.Share);
        Assert.Single(dialog.Files);
        Assert.False(shown.IsCompleted);
        Assert.False(await host.ShowAsync(new ConfirmDialogViewModel("t", "m", "ok", false)));
        Assert.Same(dialog, host.Current);
        saved.SetResult();
        await saving;
        Assert.True(await shown);
        Assert.False(host.IsOpen);
    }

    [Fact]
    public async Task Failed_Save_Preserves_Draft_And_Can_Be_Retried()
    {
        var dialog = new HomeworkDialogViewModel("Матан", _ => null, "задачи")
        { PersistAsync = () => Task.FromException(new IOException("storage")) };
        var file = new HomeworkAttachment("file", "document", "list.txt", true);
        dialog.Files.Add(file);
        await dialog.SaveAsync();
        Assert.True(dialog.HasError);
        Assert.Equal("задачи", dialog.Text);
        Assert.Equal(file, Assert.Single(dialog.Files));
        Assert.False(dialog.Completion.IsCompleted);
        Assert.True(dialog.CanEdit);
        dialog.PersistAsync = () => Task.CompletedTask;
        await dialog.SaveAsync();
        Assert.False(dialog.HasError);
        Assert.True(await dialog.Completion);
    }

    [Fact]
    public async Task Local_Row_Survives_File_Failure_And_Retry_Does_Not_Duplicate_It()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var dialog = new HomeworkDialogViewModel("Матан", _ => null);
        dialog.Text = "задачи";
        var staged = db.Services.HomeworkFiles.StageBytes(dialog.DraftId, "document", "list.txt", [65], 0);
        dialog.Files.Add(new(staged.Id, staged.Kind, staged.Name, true));
        var blocker = Path.Combine(db.Dir, "homework-files", "items");
        File.WriteAllText(blocker, "blocked directory");
        dialog.PersistAsync = () =>
        {
            HomeworkShare.SaveLocal(db.Services, dialog, TestDb.MathSubject, dialog.Text, new DateTime(2026, 9, 6));
            return Task.CompletedTask;
        };
        await dialog.SaveAsync();
        Assert.True(dialog.HasError);
        var savedId = Assert.Single(db.Services.Homework.GetAll()).Id;
        Assert.Equal(savedId, dialog.SavedId);
        File.Delete(blocker);
        dialog.Text = "исправленные задачи";
        await dialog.SaveAsync();
        Assert.True(await dialog.Completion);
        var row = Assert.Single(db.Services.Homework.GetAll());
        Assert.Equal(savedId, row.Id);
        Assert.Equal("исправленные задачи", row.Text);
        Assert.Equal(staged.Id, Assert.Single(db.Services.HomeworkFiles.List(savedId)).Id);
    }

    [Fact]
    public async Task Removing_Committed_Staged_File_After_Later_Failure_Removes_Stored_Copy_On_Retry()
    {
        using var db = TestDb.Create(seedPersonalization: false);
        var dialog = new HomeworkDialogViewModel("Матан", _ => null) { Text = "задачи" };
        dialog.Bind(db.Services);
        var staged = db.Services.HomeworkFiles.StageBytes(dialog.DraftId, "document", "list.txt", [65], 0);
        var attachment = new HomeworkAttachment(staged.Id, staged.Kind, staged.Name, true);
        dialog.Files.Add(attachment);
        void SaveLocal() => HomeworkShare.SaveLocal(db.Services, dialog, TestDb.MathSubject, dialog.Text, new DateTime(2026, 9, 6));
        dialog.PersistAsync = () => { SaveLocal(); throw new OperationCanceledException(); };
        await dialog.SaveAsync();
        Assert.True(dialog.HasError);
        Assert.Single(db.Services.HomeworkFiles.List(dialog.SavedId));
        dialog.RemoveFileCommand.Execute(attachment);
        dialog.PersistAsync = () => { SaveLocal(); return Task.CompletedTask; };
        await dialog.SaveAsync();
        Assert.True(await dialog.Completion);
        Assert.Empty(db.Services.HomeworkFiles.List(dialog.SavedId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Uncertain_Shared_Create_Is_Not_Dispatched_Again_On_Retry(bool cancel)
    {
        using var db = TestDb.Create(seedPersonalization: false);
        using var handler = new AccountClientHandler();
        using var http = new HttpClient(handler);
        using var communities = new CommunityHttpClient(http, Root);
        db.Services.UseCommunities(communities, _ => Task.FromResult<string?>(Access));
        var creates = 0;
        handler.Send = (request, _) =>
        {
            if (request.Method == HttpMethod.Get) return Task.FromResult(Payload(new[] { Membership }));
            creates++;
            return Task.FromException<HttpResponseMessage>(cancel ? new OperationCanceledException() : new HttpRequestException("response lost"));
        };
        var dialog = new HomeworkDialogViewModel("Матан", _ => null) { Text = "задачи", CanShare = true, Share = true };
        Task SaveLocal(string subject, string text)
        {
            HomeworkShare.SaveLocal(db.Services, dialog, subject, text, new DateTime(2026, 9, 6));
            return Task.CompletedTask;
        }
        var first = await HomeworkShare.SaveNewAsync(db.Services, TestDb.MathSubject, dialog,
            () => Task.FromResult<string?>(TestDb.MyGroupId), SaveLocal);
        Assert.True(first.Stored);
        Assert.False(first.Sent);
        Assert.Contains("проверьте группу", first.Note);
        dialog.Text = "исправленные задачи";
        var second = await HomeworkShare.SaveNewAsync(db.Services, TestDb.MathSubject, dialog,
            () => Task.FromResult<string?>(TestDb.MyGroupId), SaveLocal);
        Assert.True(second.Stored);
        Assert.False(second.Sent);
        Assert.Contains("проверьте группу", second.Note);
        Assert.Equal(1, creates);
        Assert.Equal("исправленные задачи", Assert.Single(db.Services.Homework.GetAll()).Text);
    }

    [Fact]
    public void Subject_Search_Empty_Result_Clears_Selection_And_Reset_Restores_Choices()
    {
        var subjects = new[] { new SubjectOption("math", "Матан", "лекция"), new SubjectOption("history", "История", "лекция") };
        var picker = new SubjectPickerDialogViewModel(subjects) { Selected = subjects[0] };
        picker.Query = "несуществующий";
        Assert.True(picker.NoResults);
        Assert.Null(picker.Selected);
        Assert.False(picker.ConfirmCommand.CanExecute(null));
        picker.ClearQueryCommand.Execute(null);
        Assert.False(picker.NoResults);
        Assert.False(picker.HasQuery);
        Assert.Equal(subjects, picker.Filtered);
    }

    [Fact]
    public async Task Profile_Suspension_Waits_For_Import_Then_Discards_Late_File()
    {
        var work = new Vograph.Desktop.Services.Profiles.ProfileWorkLifetime();
        var host = new DialogHostViewModel(work: work);
        var imported = new TaskCompletionSource<HomeworkAttachment?>();
        string? discarded = null;
        var dialog = new HomeworkDialogViewModel("Матан", _ => null, "задачи")
        { Import = _ => imported.Task, DiscardStaged = id => discarded = id };
        var shown = host.ShowAsync(dialog);
        var importing = dialog.AddDocumentCommand.ExecuteAsync(null);
        work.Suspend();
        host.DismissCommand.Execute(null);
        Assert.False(shown.IsCompleted);
        imported.SetResult(new("late", "document", "late.txt", true));
        await importing;
        Assert.Equal("late", discarded);
        Assert.Empty(dialog.Files);
        Assert.False(await shown);
        await work.WhenIdleAsync(TestContext.Current.CancellationToken);
    }
}
