using Vograph.Desktop.Controls;
using Xunit;

namespace Vograph.Desktop.Tests;

public sealed class PlannerSwipePolicyTests
{
    [Theory]
    [InlineData(-100, 8, 1)]
    [InlineData(100, -8, -1)]
    [InlineData(63, 0, 0)]
    [InlineData(80, 90, 0)]
    [InlineData(90, 70, 0)]
    [InlineData(0, 200, 0)]
    public void Only_deliberate_horizontal_release_navigates(double x, double y, int expected)
        => Assert.Equal(expected, PlannerSwipePolicy.Direction(x, y));

    [Fact]
    public void Edges_and_small_surfaces_do_not_start_navigation()
    {
        Assert.False(PlannerSwipePolicy.CanStart(10, 400));
        Assert.False(PlannerSwipePolicy.CanStart(395, 400));
        Assert.False(PlannerSwipePolicy.CanStart(20, 40));
        Assert.True(PlannerSwipePolicy.CanStart(200, 400));
    }

    [Fact]
    public void Vertical_lock_and_short_taps_are_distinct_from_horizontal_drag()
    {
        Assert.False(PlannerSwipePolicy.IsHorizontal(6, 2));
        Assert.False(PlannerSwipePolicy.IsVertical(6, 2));
        Assert.True(PlannerSwipePolicy.IsVertical(8, 20));
        Assert.False(PlannerSwipePolicy.IsHorizontal(8, 20));
        Assert.True(PlannerSwipePolicy.IsHorizontal(20, 8));
    }
}
