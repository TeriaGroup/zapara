using Vograph.Core.Services;
using Xunit;

namespace Vograph.Desktop.Tests;

public class HomeworkFileRulesTests
{
    [Fact]
    public void A_Document_Keeps_One_Extension_And_Rejects_A_Double_One()
    {
        Assert.Equal("конспект.pdf", HomeworkFileRules.CleanName("C:\\папка\\конспект.pdf"));
        Assert.Equal("секрет.pdf", HomeworkFileRules.CleanName("..\\секрет.pdf"));
        Assert.Equal("урок.pdf", HomeworkFileRules.Accept("document", "урок.pdf", 20));
        var rejected = Assert.Throws<HomeworkFileException>(() => HomeworkFileRules.Accept("document", "работа.pdf.exe", 20));
        Assert.Equal("bad", rejected.Code);
        var big = Assert.Throws<HomeworkFileException>(() => HomeworkFileRules.Accept("photo", "снимок.jpg", HomeworkFileRules.PhotoBytes + 1));
        Assert.Equal("big", big.Code);
    }
}
