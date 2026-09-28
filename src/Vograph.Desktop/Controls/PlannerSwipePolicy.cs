namespace Vograph.Desktop.Controls;

public static class PlannerSwipePolicy
{
    public const double EdgeInset = 24;
    public const double Slop = 12;
    public const double CommitDistance = 64;

    public static bool CanStart(double x, double width)
        => width > EdgeInset * 2 && x >= EdgeInset && x <= width - EdgeInset;

    public static bool IsHorizontal(double x, double y)
        => Math.Abs(x) >= Slop && Math.Abs(x) >= Math.Abs(y) * 1.5;

    public static bool IsVertical(double x, double y)
        => Math.Abs(y) >= Slop && Math.Abs(y) >= Math.Abs(x);

    public static int Direction(double x, double y)
        => Math.Abs(x) >= CommitDistance && IsHorizontal(x, y) ? (x < 0 ? 1 : -1) : 0;
}
