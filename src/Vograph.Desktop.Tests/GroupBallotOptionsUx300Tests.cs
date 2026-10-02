using Avalonia.Headless.XUnit;
using Xunit;

namespace Vograph.Desktop.Tests;

public class GroupBallotOptionsUx300Tests
{
    [AvaloniaFact]
    public async Task Draft_reorders_options_and_removes_only_the_confirmed_optional_value()
    {
        using var fixture = new GroupSpaceViewModelTests.Fixture("ballots", "ballots");
        await fixture.Vm.ActivateAsync();
        Assert.True(fixture.Vm.CanCreateBallot);
        fixture.Vm.ShowBallotComposer = true;
        fixture.Vm.BallotOptionA = "Первый"; fixture.Vm.BallotOptionB = "Второй";
        fixture.Vm.BallotOptionC = "Третий"; fixture.Vm.BallotOptionD = "Четвёртый";
        fixture.Vm.SelectedBallotOptionIndex = 3;
        fixture.Vm.MoveBallotOptionUpCommand.Execute(null);
        Assert.Equal("Четвёртый", fixture.Vm.BallotOptionC);
        Assert.Equal("Третий", fixture.Vm.BallotOptionD);
        fixture.Vm.RequestRemoveBallotOptionCommand.Execute(null);
        Assert.True(fixture.Vm.ShowRemoveBallotOption);
        fixture.Vm.CancelRemoveBallotOptionCommand.Execute(null);
        Assert.Equal("Четвёртый", fixture.Vm.BallotOptionC);
        fixture.Vm.RequestRemoveBallotOptionCommand.Execute(null);
        fixture.Vm.BallotOptionC = "Правка после запроса";
        fixture.Vm.ConfirmRemoveBallotOptionCommand.Execute(null);
        Assert.Equal("Правка после запроса", fixture.Vm.BallotOptionC);
        fixture.Vm.RequestRemoveBallotOptionCommand.Execute(null);
        fixture.Vm.ConfirmRemoveBallotOptionCommand.Execute(null);
        Assert.Equal("Третий", fixture.Vm.BallotOptionC);
        Assert.Equal("", fixture.Vm.BallotOptionD);
        Assert.Equal("Первый", fixture.Vm.BallotOptionA);
        Assert.Equal("Второй", fixture.Vm.BallotOptionB);
    }
}
