using Vograph.Desktop.Features.Summary;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class SummarySortUx300Tests
{
    [Fact]
    public void Frequency_and_alphabetical_orders_are_stable_and_keep_counts()
    {
        CountItem[] rows = [new("Физика", 2), new("Алгебра", 2), new("История", 5)];

        Assert.Equal(["История", "Алгебра", "Физика"], SummaryBrowse.Sort(rows, 0).Select(row => row.Name));
        Assert.Equal(["Алгебра", "История", "Физика"], SummaryBrowse.Sort(rows, 1).Select(row => row.Name));
        Assert.Equal(9, SummaryBrowse.Sort(rows, 1).Sum(row => row.Count));
    }

    [Fact]
    public void Long_summary_lists_expand_and_collapse_without_losing_items()
    {
        using var db = TestDb.Create();
        var vm = new SummaryViewModel(db.Services, new ShellViewModel(db.Services));
        vm.Subjects = Enumerable.Range(1, 7).Select(number => new CountItem($"Предмет {number}", number)).ToArray();
        Assert.Equal(5, vm.VisibleSubjects.Count);

        vm.ToggleSubjectsCommand.Execute(null);
        Assert.Equal(7, vm.VisibleSubjects.Count);
        vm.ToggleSubjectsCommand.Execute(null);
        Assert.Equal(5, vm.VisibleSubjects.Count);
        Assert.Equal(7, vm.Subjects.Count);
    }
}
