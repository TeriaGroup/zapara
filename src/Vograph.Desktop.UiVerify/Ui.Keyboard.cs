using System.Runtime.InteropServices;
using System.Text.Json;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.WindowsAPI;

namespace Vograph.Desktop.UiVerify;

public sealed partial class Ui
{
    // Only IDs/types/geometry, never UIA names or values (which can contain secrets).
    public string FocusEvidence()
    {
        var focused = _automation.FocusedElement();
        return focused is null ? "null" :
            $"pid={focused.Properties.ProcessId.ValueOrDefault}; id={focused.Properties.AutomationId.ValueOrDefault}; type={focused.ControlType}; focused={focused.Properties.HasKeyboardFocus.ValueOrDefault}; bounds={focused.BoundingRectangle}";
    }

    public bool HasFocus(AutomationElement target) => target.Properties.HasKeyboardFocus.ValueOrDefault &&
        _automation.FocusedElement()?.Equals(target) == true && FocusedPid() == Pid;

    public void TabTo(AutomationElement target)
    {
        for (var i = 0; i < 64; i++)
        {
            if (HasFocus(target))
            {
                var b = target.BoundingRectangle;
                if (!target.IsEnabled || target.IsOffscreen || b.Width <= 0 || b.Height <= 0 ||
                    !Window.BoundingRectangle.Contains(b))
                    throw new InvalidOperationException("Keyboard target is not visibly reachable: " + target.Properties.AutomationId.ValueOrDefault);
                return;
            }
            Keys(VirtualKeyShort.TAB);
        }
        throw new InvalidOperationException("Tab target unreachable: " + target.Properties.AutomationId.ValueOrDefault + "; " + FocusEvidence());
    }

    public void Press(string id, VirtualKeyShort key = VirtualKeyShort.RETURN)
    {
        TabTo(Find(id));
        Keys(key);
    }

    public void SetMinimumWindow(string output)
    {
        var handle = new IntPtr(Window.Properties.NativeWindowHandle.Value);
        var dpi = GetDpiForWindow(handle);
        // This bounded unit is 100% scale; a different DPI is a separate matrix, not a pixel-resize workaround.
        if (dpi != 96) throw new InvalidOperationException($"Minimum keyboard unit requires 96 DPI; actual {dpi}");
        if (!SetWindowPos(handle, IntPtr.Zero, 0, 0, 960, 600, 0x0016))
            throw new InvalidOperationException("SetWindowPos failed");
        Thread.Sleep(500);
        if (!GetWindowRect(handle, out var outer) || !GetClientRect(handle, out var client))
            throw new InvalidOperationException("Window measurements unavailable");
        var measured = new { requestedWidth = 960, requestedHeight = 600, dpi,
            outerWidth = outer.Right - outer.Left, outerHeight = outer.Bottom - outer.Top,
            clientWidth = client.Right - client.Left, clientHeight = client.Bottom - client.Top };
        File.WriteAllText(Path.Combine(output, "minimum-window.json"), JsonSerializer.Serialize(measured));
        if (measured.clientWidth != 960 || measured.clientHeight != 600 ||
            measured.outerWidth != 960 || measured.outerHeight != 600)
            throw new InvalidOperationException("Actual client/outer differs from 960x600; see minimum-window.json");
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
