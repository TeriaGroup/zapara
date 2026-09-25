namespace Vograph.Desktop.Features.Chat;

public static class ChatScrollLogic
{
    public static bool NearLatest(double extent, double viewport, double offset)
        => extent - viewport - offset <= 64;

    public static double AfterPrepend(double oldOffset, double oldExtent, double newExtent)
        => oldOffset + Math.Max(0, newExtent - oldExtent);
}
