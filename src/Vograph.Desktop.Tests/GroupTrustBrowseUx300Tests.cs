using Vograph.Desktop.Features.Groups;
using Vograph.Desktop.Shell;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupTrustBrowseUx300Tests : UiTest
{
    [Fact]
    public void Role_person_and_access_preview_filters_do_not_change_source_collections()
    {
        using var db = TestDb.Create();
        var vm = new GroupViewModel(db.Services);
        vm.TrustedRoles.Add(new(Guid.NewGuid(), "Подгруппа 1", [], 1));
        vm.TrustedRoles.Add(new(Guid.NewGuid(), "Редакторы", [], 2));
        vm.TrustCandidates.Add(new(Guid.NewGuid(), "Иван", "member"));
        vm.TrustCandidates.Add(new(Guid.NewGuid(), "Пётр", "headman"));
        vm.TrustSearch = "подгруппа";
        Assert.Equal("Подгруппа 1", Assert.Single(vm.VisibleTrustedRoles).Name);
        vm.TrustSearch = "headman";
        Assert.Equal("Пётр", Assert.Single(vm.VisibleTrustCandidates).Name);
        Assert.Equal(2, vm.TrustedRoles.Count);

        vm.AccessPreviewPeople.Add(new("Иван", "Читать", "Читать, писать", []));
        vm.AccessPreviewPeople.Add(new("Пётр", "Читать", "Читать", []));
        vm.AccessPreviewReady = true;
        vm.AccessPreviewChangedOnly = true;
        Assert.Equal("Иван", Assert.Single(vm.VisibleAccessPreviewPeople).Name);
        vm.AccessPreviewSearch = "несуществующий";
        Assert.True(vm.NoAccessPreviewMatches);
        Assert.Equal(2, vm.AccessPreviewPeople.Count);
    }
}
