using Avalonia.Headless.XUnit;
using CommunityToolkit.Mvvm.Input;
using Vograph.Desktop.Features.Chat;
using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Shell;
using Zapara.Contracts.Social;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class QuoteJumpTests
{
    [AvaloniaFact]
    public void Group_quote_uses_only_loaded_exact_target_and_reports_missing_or_deleted()
    {
        using var db = TestDb.Create();
        var vm = new GroupViewModel(db.Services);
        var parent = new GroupMessageRow(Guid.NewGuid(), "Аня", "Первая строка", "09:00", false);
        var quote = new GroupMessageRow(Guid.NewGuid(), "Борис", "Ответ", "09:01", false, replyPreview:"Первая строка", replyToId:parent.Id);
        vm.Messages.Add(parent); vm.Messages.Add(quote); vm.Draft = "Не трогать черновик";
        vm.JumpQuoteCommand.Execute(quote);
        Assert.True(parent.IsQuoteTarget); Assert.Equal("Не трогать черновик", vm.Draft);
        vm.Messages.Remove(parent); vm.HasMore = true;
        vm.JumpQuoteCommand.Execute(quote);
        Assert.Contains("Загрузите ранние", vm.QuoteFeedback);
        Assert.Equal(vm.QuoteFeedback, quote.QuoteHint);
        vm.Messages.Add(new GroupMessageRow(parent.Id, "Аня", "", "09:00", false, deleted:true));
        vm.JumpQuoteCommand.Execute(quote);
        Assert.Contains("удалено", vm.QuoteFeedback);
    }

    [AvaloniaFact]
    public void Personal_quote_preserves_search_draft_and_only_targets_loaded_history()
    {
        using var db = TestDb.Create();
        var shell = new ShellViewModel(db.Services);
        var vm = new ChatInboxViewModel(db.Services, shell);
        var parent = Row("Первое", null);
        var quote = Row("Ответ", parent.Id);
        vm.Messages.Add(parent); vm.Messages.Add(quote); vm.MessageSearch = "ответ";
        vm.JumpQuoteCommand.Execute(quote);
        Assert.True(parent.IsQuoteTarget); Assert.Equal("", vm.MessageSearch);
        vm.Messages.Remove(parent); vm.HasMore = true;
        vm.JumpQuoteCommand.Execute(quote);
        Assert.Contains("Ранее", vm.QuoteFeedback);
        Assert.Equal(vm.QuoteFeedback, quote.QuoteHint);
        vm.Detach(); shell.Detach();
    }

    private static ChatMessageRow Row(string text, Guid? replyTo)
    {
        var response = new SocialMessageResponse(Guid.NewGuid(), Guid.NewGuid(), "Аня", "text", text,
            null, null, null, null, DateTimeOffset.UtcNow, replyTo, replyTo is null ? null : "Первое",
            null, false, false, null, []);
        return new ChatMessageRow(response, false, new RelayCommand(() => {}), new RelayCommand(() => {}),
            new AsyncRelayCommand(() => Task.CompletedTask), new AsyncRelayCommand(() => Task.CompletedTask), null, null,
            new AsyncRelayCommand(() => Task.CompletedTask));
    }
}
