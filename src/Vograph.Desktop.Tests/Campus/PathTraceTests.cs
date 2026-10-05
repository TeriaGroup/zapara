using Vograph.Core.Campus;
using Xunit;

namespace Vograph.Desktop.Tests;

public class PathTraceTests
{
    private static TracePoint P(double x, double y) => new(x, y);

    [Fact]
    public void Length_sums_segment_lengths_and_skips_empty()
    {
        Assert.Equal(0, PathTrace.Length(Array.Empty<TracePoint>()));
        Assert.Equal(0, PathTrace.Length([P(1, 1)]));
        Assert.Equal(7, PathTrace.Length([P(0, 0), P(3, 0), P(3, 4)]), 6);
        Assert.Equal(7, PathTrace.Length([[P(0, 0), P(3, 0)], [P(8, 0), P(12, 0)]]), 6);
        Assert.Equal(0, PathTrace.Length(Array.Empty<IReadOnlyList<TracePoint>>()));
    }

    [Fact]
    public void Prefix_cuts_on_a_vertex_and_inside_a_segment()
    {
        var l = new[] { P(0, 0), P(3, 0), P(3, 4) };
        Assert.Equal([P(0, 0)], PathTrace.Prefix(l, 0));
        Assert.Equal([P(0, 0), P(1.5, 0)], PathTrace.Prefix(l, 1.5));
        Assert.Equal([P(0, 0), P(3, 0)], PathTrace.Prefix(l, 3));
        Assert.Equal([P(0, 0), P(3, 0), P(3, 2)], PathTrace.Prefix(l, 5));
        Assert.Equal(l, PathTrace.Prefix(l, 100));
        Assert.Empty(PathTrace.Prefix([], 1));
    }

    [Fact]
    public void PointAt_walks_the_polyline_and_clamps()
    {
        var l = new[] { P(0, 0), P(3, 0), P(3, 4) };
        Assert.Equal(P(0, 0), PathTrace.PointAt(l, 0));
        Assert.Equal(P(1.5, 0), PathTrace.PointAt(l, 1.5));
        Assert.Equal(P(3, 2), PathTrace.PointAt(l, 5));
        Assert.Equal(P(3, 4), PathTrace.PointAt(l, 99));
        Assert.Null(PathTrace.PointAt([], 1));
        Assert.Equal(P(2, 2), PathTrace.PointAt([P(2, 2)], 4));
    }

    [Fact]
    public void At_reveals_disconnected_strokes_in_order()
    {
        IReadOnlyList<IReadOnlyList<TracePoint>> strokes =
        [
            [P(0, 0), P(2, 0)],
            [P(5, 0), P(7, 0)]
        ];
        var start = PathTrace.At(strokes, 0);
        Assert.Empty(start.Revealed);
        Assert.False(start.Complete);
        Assert.Equal(P(0, 0), start.Head);
        Assert.Equal(P(0, 0), start.Start);
        Assert.Equal(P(7, 0), start.End);

        var half = PathTrace.At(strokes, 0.5);
        Assert.Equal([P(0, 0), P(2, 0)], Assert.Single(half.Revealed));
        Assert.Equal(P(2, 0), half.Head);
        Assert.False(half.Complete);

        var three = PathTrace.At(strokes, 0.75);
        Assert.Equal(2, three.Revealed.Count);
        Assert.Equal([P(0, 0), P(2, 0)], three.Revealed[0]);
        Assert.Equal([P(5, 0), P(6, 0)], three.Revealed[1]);
        Assert.Equal(P(6, 0), three.Head);
        Assert.False(three.Complete);

        var done = PathTrace.At(strokes, 1);
        Assert.Equal(strokes, done.Revealed);
        Assert.True(done.Complete);
        Assert.Equal(P(7, 0), done.Head);
    }

    [Fact]
    public void At_empty_or_short_is_complete_with_nothing_to_draw()
    {
        var empty = PathTrace.At([], 0.4);
        Assert.Empty(empty.Revealed);
        Assert.True(empty.Complete);
        Assert.Null(empty.Head);
        Assert.Null(empty.Start);
        Assert.Null(empty.End);

        var shortStroke = PathTrace.At([[P(1, 1)]], 1);
        Assert.Empty(shortStroke.Revealed);
        Assert.True(shortStroke.Complete);
    }

    [Theory]
    [InlineData(0, 1200, 0)]
    [InlineData(600, 1200, 0.5)]
    [InlineData(1200, 1200, 1)]
    [InlineData(2000, 1200, 1)]
    [InlineData(50, 0, 1)]
    [InlineData(-20, 1200, 0)]
    public void DrawProgress_clamps_and_snaps_without_duration(double elapsed, int duration, double expected) =>
        Assert.Equal(expected, PathTrace.DrawProgress(elapsed, duration), 6);

    [Fact]
    public void LoopProgress_sits_at_the_end_until_the_draw_finishes()
    {
        Assert.Equal(1, PathTrace.LoopProgress(0, 1200, 800), 6);
        Assert.Equal(1, PathTrace.LoopProgress(1199, 1200, 800), 6);
        Assert.Equal(0, PathTrace.LoopProgress(1200, 1200, 800), 6);
        Assert.Equal(0.5, PathTrace.LoopProgress(1600, 1200, 800), 6);
        Assert.Equal(0, PathTrace.LoopProgress(2000, 1200, 800), 6);
        Assert.Equal(1, PathTrace.LoopProgress(500, 0, 800), 6);
        Assert.Equal(1, PathTrace.LoopProgress(2000, 1200, 0), 6);
    }

    [Fact]
    public void DashOffset_marches_a_period_and_freezes_without_a_cycle()
    {
        Assert.Equal(0, PathTrace.DashOffset(0, 1000, 18), 6);
        Assert.Equal(9, PathTrace.DashOffset(500, 1000, 18), 6);
        Assert.Equal(0, PathTrace.DashOffset(1000, 1000, 18), 6);
        Assert.Equal(0, PathTrace.DashOffset(250, 0, 18), 6);
    }

    [Fact]
    public void DrawMs_matches_the_map_pulse_contract() =>
        Assert.Equal(1200, PathTrace.DrawMs);
}
