using System.Diagnostics;
using System.Text;

namespace Vograph.Desktop.Services;

/// <summary>The running exe cannot overwrite itself: a batch file waits for it to exit, unpacks the zip over the
/// install directory and starts the app again (ported from the WPF client; UTF-8 code page so Cyrillic paths survive).</summary>
public static class UpdateRunner
{
    public static string BuildBatch(string appDir, string zipPath, string exeName = "Vograph.exe")
    {
        var dir = appDir.TrimEnd('\\', '/');
        return "@echo off\r\n" +
               "chcp 65001 >NUL\r\n" +
               ":wait\r\n" +
               $"tasklist /FI \"IMAGENAME eq {exeName}\" 2>NUL | find /I \"{exeName}\" >NUL\r\n" +
               "if not errorlevel 1 (timeout /t 1 /nobreak >NUL & goto wait)\r\n" +
               $"powershell -NoProfile -ExecutionPolicy Bypass -Command \"Expand-Archive -LiteralPath '{zipPath.Replace("'", "''")}' -DestinationPath '{dir.Replace("'", "''")}' -Force\"\r\n" +
               $"start \"\" \"{dir}\\{exeName}\"\r\n" +
               "del \"%~f0\"\r\n";
    }

    public static void Apply(string zipPath, string appDir, Action shutdown, string exeName = "Vograph.exe")
    {
        var bat = Path.Combine(Path.GetTempPath(), "vograph_update.bat");
        File.WriteAllText(bat, BuildBatch(appDir, zipPath, exeName), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden });
        shutdown();
    }
}
