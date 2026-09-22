using Vograph.Desktop.Legal;
using Xunit;

namespace Vograph.Desktop.Tests;

public class LegalDocumentsTests
{
    [Fact]
    public void Both_documents_are_the_texts_the_account_entry_shows()
    {
        Assert.Equal("Пользовательское соглашение", LegalDocuments.Agreement.Title);
        Assert.Equal("Политика обработки персональных данных", LegalDocuments.Policy.Title);
        foreach (var doc in new[] { LegalDocuments.Agreement, LegalDocuments.Policy })
        {
            Assert.Contains("Расписание военмех", doc.Body);
            Assert.Contains("неофициальное", doc.Body);
            Assert.Contains("не является сервисом университета", doc.Body);
            Assert.Contains("расписание, карты и локальные записи остаются на устройстве", doc.Body);
            Assert.Contains("Аккаунт необязателен", doc.Body);
            Assert.Contains("на сервере оператора в России хранятся только данные, которые нужны этому аккаунту и выбранной синхронизации", doc.Body);
            Assert.Contains("Рекламных SDK нет", doc.Body);
            Assert.Contains("Сторонней аналитики нет", doc.Body);
            Assert.Contains("https://github.com/TeriaGroup/zapara", doc.Body);
            Assert.DoesNotContain("ИНН", doc.Body);
            Assert.DoesNotContain("почтовый адрес", doc.Body);
        }
    }
}
