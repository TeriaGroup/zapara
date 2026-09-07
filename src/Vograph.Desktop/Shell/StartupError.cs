namespace Vograph.Desktop.Shell;

/// <summary>What the startup error window shows when AppServices.Create failed (a locked or corrupt database, an unwritable data folder).</summary>
public sealed record StartupError(string Message, string DataDir, string LogFile);
