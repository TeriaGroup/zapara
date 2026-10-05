using Vograph.Core.Models;
using Vograph.Desktop.Dialogs;
using Vograph.Desktop.Features.Homeworks;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkDuplicateUx300Tests : UiTest
{
    [Fact]
    public void Same_subject_text_and_due_warns_but_different_due_does_not()
    {
        var due = new DateTime(2026, 10, 5);
        Homework[] existing = [new() { SubjectRawNormalized = "лек высш. математ", Text = "§5, задачи 1–12", DueDateComputed = due }];

        Assert.True(HomeworkDuplicateRule.Exists(existing, "лек ВЫСШ. МАТЕМАТ", " §5, задачи 1–12 ", due));
        Assert.False(HomeworkDuplicateRule.Exists(existing, "лек ВЫСШ. МАТЕМАТ", "§5, задачи 1–12", due.AddDays(7)));
        Assert.False(HomeworkDuplicateRule.Exists(existing, "лек ВЫСШ. МАТЕМАТ", "Другое задание", due));
    }

    [Fact]
    public async Task Duplicate_warning_requires_explicit_create_anyway()
    {
        using var db = TestDb.Create();
        var existing = Assert.Single(db.Services.Homework.GetAll());
        var shell = new ShellViewModel(db.Services);
        var vm = new HomeworkViewModel(db.Services, shell, () => existing.CreatedAt);
        await vm.LoadAsync();

        var adding = vm.AddCommand.ExecuteAsync(null);
        var picker = await Waits.ForDialogAsync<SubjectPickerDialogViewModel>(shell);
        picker.Selected = picker.Filtered.First(option => option.SubjectRaw == TestDb.MathSubject);
        picker.ConfirmCommand.Execute(null);
        var dialog = await Waits.ForDialogAsync<HomeworkDialogViewModel>(shell);
        dialog.Text = existing.Text;
        dialog.ConfirmCommand.Execute(null);
        await Waits.Until(() => dialog.DuplicateWarning, "duplicate warning");
        Assert.Single(db.Services.Homework.GetAll());

        dialog.CreateDuplicateAnywayCommand.Execute(null);
        await adding;
        Assert.Equal(2, db.Services.Homework.GetAll().Count);
    }
}
