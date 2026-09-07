using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Input;
using FlaUI.Core.Tools;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;

namespace Vograph.Desktop.UiVerify;

/// <summary>Launches ONE Vograph.exe on a scratch data folder and drives it by AutomationId. Only that process (its pid) is
/// ever closed or killed — the user may have another Vograph running on the real profile.</summary>
public sealed class Ui : IDisposable
{
    private readonly Application _app;
    private readonly UIA3Automation _automation = new();
    private readonly TimeSpan _timeout;
    private readonly string _framesDir;
    private int _frame;

    public Ui(Options o)
    {
        _timeout = o.Timeout;
        _framesDir = Path.Combine(o.Out, "frames");
        Directory.CreateDirectory(_framesDir);
        var psi = new ProcessStartInfo(o.Exe) { WorkingDirectory = Path.GetDirectoryName(o.Exe)!, UseShellExecute = false };
        psi.Environment["VOGRAPH_DATA_DIR"] = o.Data;
        psi.Environment["VOGRAPH_OFFLINE"] = "1";
        _app = Application.Launch(psi);
        Pid = _app.ProcessId;
        Window = _app.GetMainWindow(_automation, _timeout) ?? throw new TimeoutException($"main window did not appear within {_timeout}");
    }

    public int Pid { get; }
    public Window Window { get; }

    public AutomationElement Find(string automationId)
    {
        var result = Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), _timeout, TimeSpan.FromMilliseconds(100));
        return result.Result ?? throw new InvalidOperationException($"element {automationId} not found within {_timeout}");
    }

    public AutomationElement? TryFind(string automationId, TimeSpan? wait = null) =>
        Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), wait ?? TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100)).Result;

    public AutomationElement[] FindAll(string automationId) => Window.FindAllDescendants(cf => cf.ByAutomationId(automationId));

    public void Click(string automationId)
    {
        var el = Find(automationId);
        if (el.Patterns.Invoke.IsSupported) el.Patterns.Invoke.Pattern.Invoke();
        else el.Click();
        Thread.Sleep(350); // let the 180–220 ms transitions finish before the next step reads the screen
    }

    public void Toggle(string automationId)
    {
        var el = Find(automationId);
        if (el.Patterns.Toggle.IsSupported) el.Patterns.Toggle.Pattern.Toggle();
        else el.Click();
        Thread.Sleep(350);
    }

    public void Hover(AutomationElement el)
    {
        Mouse.MoveTo(el.BoundingRectangle.Center());
        Thread.Sleep(250);
    }

    public string Text(string automationId)
    {
        var el = Find(automationId);
        return el.Patterns.Value.IsSupported ? el.Patterns.Value.Pattern.Value.Value : el.Name;
    }

    /// <summary>Waits until some element with that id has the given text (a TextBlock exposes its text as Name).</summary>
    public bool WaitText(string automationId, Func<string, bool> predicate, TimeSpan? wait = null) =>
        Retry.WhileFalse(() => FindAll(automationId).Any(e => predicate(e.Name)), wait ?? _timeout, TimeSpan.FromMilliseconds(200)).Result;

    public void Keys(params VirtualKeyShort[] keys)
    {
        if (keys.Length == 1) Keyboard.Type(keys[0]);
        else Keyboard.TypeSimultaneously(keys);
        Thread.Sleep(350);
    }

    public void TypeText(string text)
    {
        Keyboard.Type(text);
        Thread.Sleep(200);
    }

    /// <summary>Capture.Element copies the screen under the window's rectangle, so whatever sits on top of it lands
    /// in the PNG instead — another app re-asserting the foreground (a game, an overlay) silently poisons a frame.
    /// Our window is brought to the front first; that is also the state the keyboard steps already depend on.</summary>
    public string Shot(string name)
    {
        var path = Path.Combine(_framesDir, $"{++_frame:00}-{name}.png");
        try { Window.SetForeground(); } catch (Exception ex) { Console.Error.WriteLine($"frame {name}: could not raise the window ({ex.Message}), capturing anyway"); }
        Thread.Sleep(120); // the raise is asynchronous in the window manager; capture after it has landed
        using var image = Capture.Element(Window);
        image.ToFile(path);
        return Path.GetFileName(path);
    }

    /// <summary>Close through the window's own ✕ and wait; kill only as a last resort, and only our pid.</summary>
    public bool CloseGracefully()
    {
        try { Click("Win.Close"); } catch (Exception) { /* the window may already be gone */ }
        var exited = SpinUntilExited(TimeSpan.FromSeconds(10));
        if (!_app.HasExited) _app.Kill(); // our own process, by the handle we launched it with — never by name
        return exited;
    }

    private bool SpinUntilExited(TimeSpan wait)
    {
        var sw = Stopwatch.StartNew();
        while (!_app.HasExited && sw.Elapsed < wait) Thread.Sleep(100);
        return _app.HasExited;
    }

    public void Dispose()
    {
        _automation.Dispose();
        _app.Dispose();
    }
}
