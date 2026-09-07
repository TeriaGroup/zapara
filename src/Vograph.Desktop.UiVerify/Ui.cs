using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
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
    private bool _raised;

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
        try
        {
            Window = _app.GetMainWindow(_automation, _timeout) ?? throw new TimeoutException($"main window did not appear within {_timeout}");
        }
        catch (Exception)
        {
            // FlaUI's Application.Dispose only releases the process handle, so without this the exit-2 branch
            // would strand a Vograph.exe on the user's desktop for every failed launch (T12-R3).
            Killed = KillOurProcess();
            _automation.Dispose();
            _app.Dispose();
            throw;
        }
    }

    public int Pid { get; }
    public Window Window { get; } = null!;

    /// <summary>Set when a failure path had to kill the process this driver launched; the report says so.</summary>
    public bool Killed { get; private set; }

    public AutomationElement Find(string automationId)
    {
        var result = Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), _timeout, TimeSpan.FromMilliseconds(100));
        return result.Result ?? throw new InvalidOperationException($"element {automationId} not found within {_timeout}");
    }

    public AutomationElement? TryFind(string automationId, TimeSpan? wait = null) =>
        Retry.WhileNull(() => Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId)), wait ?? TimeSpan.FromSeconds(2), TimeSpan.FromMilliseconds(100)).Result;

    public AutomationElement[] FindAll(string automationId) => Window.FindAllDescendants(cf => cf.ByAutomationId(automationId));

    /// <summary>Every element with that id and the name it exposes — for failure messages, so a step that did not
    /// find what it expected says what UIA actually saw instead of just «not found».</summary>
    public string Dump(string automationId)
    {
        var all = FindAll(automationId);
        return all.Length == 0 ? $"no {automationId}" : string.Join(" · ", all.Select(e => $"«{Safe(() => e.Name)}»"));
    }

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

    /// <summary>Toggle state of a Switch (a ToggleButton), for the steps that must read their own switch back.</summary>
    public bool IsOn(string automationId)
    {
        var el = Find(automationId);
        if (!el.Patterns.Toggle.IsSupported) throw new InvalidOperationException($"{automationId} exposes no toggle state");
        return el.Patterns.Toggle.Pattern.ToggleState.Value == FlaUI.Core.Definitions.ToggleState.On;
    }

    /// <summary>Presses an element the driver already holds, through its Invoke pattern rather than the mouse.</summary>
    public void Invoke(AutomationElement el)
    {
        if (el.Patterns.Invoke.IsSupported) el.Patterns.Invoke.Pattern.Invoke();
        else el.Click();
        Thread.Sleep(350);
    }

    /// <summary>On screen at all: an Avalonia control with IsVisible=false leaves the tree, but a scrolled-away
    /// one only becomes offscreen, and «did Escape close it» must be true for both.</summary>
    public bool IsShown(string automationId) => FindAll(automationId).Any(e => !Offscreen(e));

    private static bool Offscreen(AutomationElement el)
    {
        try { return el.IsOffscreen; }
        catch (Exception) { return true; } // gone between the find and the read: not shown
    }

    /// <summary>Mouse.MoveTo is global, like the keyboard: the lesson card's hover actions are the one thing
    /// here that cannot be reached without it (Typography.axaml hides them with IsVisible), so the move happens
    /// only once our own window holds the foreground — otherwise the step fails instead of waving the cursor
    /// through whatever the user has in front (T12-R3).</summary>
    public void Hover(AutomationElement el)
    {
        RequireOurFocus("hover");
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

    /// <summary>Waits until the predicate holds over every element carrying that id (an empty set counts as «not yet»).</summary>
    public bool WaitFor(Func<bool> predicate, TimeSpan? wait = null) =>
        Retry.WhileFalse(predicate, wait ?? _timeout, TimeSpan.FromMilliseconds(200)).Result;

    /// <summary>
    /// Keys and typed text go through SendInput, which lands wherever the *system* focus is — the user's editor,
    /// their game, their password box. So every keystroke is preceded by a check that the focused element belongs
    /// to the process this driver launched; the window is raised once to earn that focus, and a step that still
    /// cannot get it fails loudly instead of typing into somebody else's window (T12-R3).
    /// </summary>
    private void RequireOurFocus(string what)
    {
        if (FocusedPid() == Pid) return;
        if (!_raised)
        {
            _raised = true;
            try { Window.SetForeground(); }
            catch (Exception ex) { Console.Error.WriteLine($"could not raise the window before «{what}»: {ex.Message}"); }
            Thread.Sleep(200);
        }
        else
        {
            try { Window.Focus(); } catch (Exception ex) { Console.Error.WriteLine($"could not focus the window before «{what}»: {ex.Message}"); }
            Thread.Sleep(200);
        }
        var pid = FocusedPid();
        if (pid != Pid)
            throw new InvalidOperationException($"refusing to send «{what}»: the keyboard focus is in pid {pid?.ToString() ?? "?"}, not in our Vograph (pid {Pid})");
    }

    private int? FocusedPid()
    {
        try { return _automation.FocusedElement()?.Properties.ProcessId.ValueOrDefault; }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not read the focused element: {ex.Message}");
            return null;
        }
    }

    public void Keys(params VirtualKeyShort[] keys)
    {
        RequireOurFocus(string.Join("+", keys));
        if (keys.Length == 1) Keyboard.Type(keys[0]);
        else Keyboard.TypeSimultaneously(keys);
        Thread.Sleep(350);
    }

    public void TypeText(string text)
    {
        RequireOurFocus($"text «{text}»");
        Keyboard.Type(text);
        Thread.Sleep(200);
    }

    /// <summary>
    /// PrintWindow with PW_RENDERFULLCONTENT: the window renders itself into our DC, so a frame is the app even
    /// when a fullscreen game, an overlay or a chat window sits on top of it — and the driver never has to fight
    /// the user for the foreground to take a picture. FlaUI's Capture.Element is a screen copy of a bounding
    /// rectangle and cannot avoid that (T12-R5).
    /// </summary>
    public string Shot(string name)
    {
        var path = Path.Combine(_framesDir, $"{++_frame:00}-{name}.png");
        using var bitmap = Capture();
        bitmap.Save(path, ImageFormat.Png);
        return Path.GetFileName(path);
    }

    /// <summary>The window's own pixels, client area included, in window coordinates.</summary>
    public Bitmap Capture()
    {
        var handle = new IntPtr(Window.Properties.NativeWindowHandle.Value);
        if (!GetWindowRect(handle, out var rect)) throw new InvalidOperationException("GetWindowRect failed for the app window");
        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0) throw new InvalidOperationException($"the app window has no area ({width}x{height})");
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        try
        {
            using (var g = Graphics.FromImage(bitmap))
            {
                var dc = g.GetHdc();
                try
                {
                    if (!PrintWindow(handle, dc, PW_RENDERFULLCONTENT))
                        throw new InvalidOperationException("PrintWindow refused to render the app window");
                }
                finally { g.ReleaseHdc(dc); }
            }
            return bitmap;
        }
        catch (Exception)
        {
            bitmap.Dispose();
            throw;
        }
    }

    /// <summary>Window-relative rectangle of an element, for reading one region of a capture.</summary>
    public Rectangle RegionOf(string automationId)
    {
        var handle = new IntPtr(Window.Properties.NativeWindowHandle.Value);
        if (!GetWindowRect(handle, out var window)) throw new InvalidOperationException("GetWindowRect failed for the app window");
        var b = Find(automationId).BoundingRectangle;
        var region = Rectangle.Intersect(
            new Rectangle(b.X - window.Left, b.Y - window.Top, b.Width, b.Height),
            new Rectangle(0, 0, window.Right - window.Left, window.Bottom - window.Top));
        if (region.Width < 8 || region.Height < 8) throw new InvalidOperationException($"{automationId} has no readable area on screen ({region})");
        return region;
    }

    /// <summary>Mean luminance (0..1) of a region of the window's own rendering — how the driver reads a state
    /// the shell exposes only as paint, such as the dark/light theme.</summary>
    public double Luminance(Rectangle region)
    {
        using var bitmap = Capture();
        return Grid(bitmap, region, cells: 1)[0];
    }

    /// <summary>A coarse 6×6 luminance grid of a region: enough to say «this part of the window is drawing
    /// something else now» (another floor plan, a zoomed plan) without pinning exact pixels.</summary>
    public double[] Signature(Rectangle region)
    {
        using var bitmap = Capture();
        return Grid(bitmap, region, cells: 6);
    }

    /// <summary>Largest per-cell difference between two signatures, 0..1.</summary>
    public static double Difference(double[] a, double[] b) =>
        a.Length != b.Length ? 1 : a.Zip(b, (x, y) => Math.Abs(x - y)).Max();

    private static double[] Grid(Bitmap bitmap, Rectangle region, int cells)
    {
        var clipped = Rectangle.Intersect(region, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
        if (clipped.Width < cells || clipped.Height < cells) throw new InvalidOperationException($"region {region} is not inside the {bitmap.Width}x{bitmap.Height} capture");
        var sums = new double[cells * cells];
        var counts = new int[cells * cells];
        // Every fourth pixel in both directions: a 1280x800 frame is 256 000 reads instead of 4 000 000, and a
        // mean over that many samples is stable to far more decimals than any assertion here needs.
        for (var y = clipped.Top; y < clipped.Bottom; y += 4)
        {
            var row = Math.Min(cells - 1, (y - clipped.Top) * cells / clipped.Height);
            for (var x = clipped.Left; x < clipped.Right; x += 4)
            {
                var column = Math.Min(cells - 1, (x - clipped.Left) * cells / clipped.Width);
                var p = bitmap.GetPixel(x, y);
                sums[row * cells + column] += (0.2126 * p.R + 0.7152 * p.G + 0.0722 * p.B) / 255.0;
                counts[row * cells + column]++;
            }
        }
        return sums.Select((s, i) => counts[i] == 0 ? 0 : s / counts[i]).ToArray();
    }

    /// <summary>Close through the window's own ✕ and wait; kill only as a last resort, and only our pid.</summary>
    public bool CloseGracefully()
    {
        try { Click("Win.Close"); } catch (Exception ex) { Console.Error.WriteLine($"✕ could not be clicked ({ex.Message}); waiting for the process anyway"); }
        var exited = SpinUntilExited(TimeSpan.FromSeconds(10));
        if (!exited) Killed = KillOurProcess();
        return exited;
    }

    /// <summary>The only kill in this driver: the process it launched itself, addressed by the handle
    /// Application.Launch returned. Never by name — the user may have their own Vograph.exe running.</summary>
    private bool KillOurProcess()
    {
        try
        {
            if (_app.HasExited) return false;
            _app.Kill();
            Console.Error.WriteLine($"killed our own Vograph.exe (pid {Pid})");
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"could not kill pid {Pid}: {ex.Message}");
            return false;
        }
    }

    private bool SpinUntilExited(TimeSpan wait)
    {
        var sw = Stopwatch.StartNew();
        while (!_app.HasExited && sw.Elapsed < wait) Thread.Sleep(100);
        return _app.HasExited;
    }

    private static string Safe(Func<string> read)
    {
        try { return read(); }
        catch (Exception ex) { return $"<{ex.GetType().Name}>"; }
    }

    public void Dispose()
    {
        _automation.Dispose();
        _app.Dispose();
    }

    private const int PW_RENDERFULLCONTENT = 0x2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, int flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }
}
