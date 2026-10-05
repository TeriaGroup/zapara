using Vograph.Desktop.Features.Account;
using Xunit;

namespace Vograph.Desktop.Tests;

public class AccountLegalUx300Tests
{
    [Fact]
    public void Legal_search_finds_local_paragraph_and_text_scale_can_change()
    {
        using var db = TestDb.Create();
        using var account = new AccountPanelViewModel();
        account.OpenAgreementCommand.Execute(null);
        account.DocumentSearch = "университета";
        var found = Assert.Single(account.DocumentMatches.Take(1));

        account.SelectDocumentMatchCommand.Execute(found);
        Assert.Contains("университета", account.SelectedDocumentParagraph, StringComparison.OrdinalIgnoreCase);
        var before = account.DocumentTextSize;
        account.EnlargeDocumentTextCommand.Execute(null);
        Assert.True(account.DocumentTextSize > before);
    }
}
