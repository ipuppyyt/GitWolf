using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitWolf.App.Services.Shell;

/// <summary>The small count drawn over the taskbar button. UI thread only.</summary>
internal sealed class TaskbarBadge(nint window)
{
    private ITaskbarList3? _taskbar;
    private string? _label;

    /// <summary>Null removes the badge.</summary>
    public void Show(string? label)
    {
        if (_label == label) return;
        _label = label;
        Apply();
    }

    /// <summary>Also needed when the taskbar button is created again.</summary>
    public void Apply()
    {
        var icon = _label is null ? 0 : Draw(_label);
        try
        {
            if (_taskbar is null)
            {
                _taskbar = (ITaskbarList3)new TaskbarList();
                _taskbar.HrInit();
            }
            _taskbar.SetOverlayIcon(window, icon, _label is null ? "" : $"{_label} changed files");
        }
        catch (COMException e)
        {
            // No taskbar button yet: it is applied again when there is one.
            Trace.TraceInformation($"The taskbar badge wasn't set (0x{e.HResult:X8}).");
        }
        finally
        {
            // The taskbar keeps its own copy.
            if (icon != 0) DestroyIcon(icon);
        }
    }

    /// <summary>White digits on a red disc, with a smooth edge.</summary>
    private static nint Draw(string label)
    {
        var size = Math.Max(16, GetSystemMetrics(49));
        var header = new BitmapHeader { Size = 40, Width = size, Height = -size, Planes = 1, Bits = 32 };
        var dc = CreateCompatibleDC(0);
        var colour = CreateDIBSection(dc, ref header, 0, out var pixels, 0, 0);
        var mask = CreateBitmap(size, size, 1, 1, 0);
        if (dc == 0 || colour == 0 || mask == 0 || pixels == 0)
        {
            Free(dc, colour, mask, 0);
            return 0;
        }
        var previous = SelectObject(dc, colour);
        var disc = new int[size * size];
        const int red = 0xC42B1C;
        var radius = size / 2.0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                disc[y * size + x] = red;
        Marshal.Copy(disc, 0, pixels, disc.Length);
        var font = CreateFont(-(int)(size * (label.Length > 2 ? 0.5 : 0.72)), 0, 0, 0, 700, 0, 0, 0, 1, 0, 0, 4, 0,
            "Segoe UI");
        var oldFont = SelectObject(dc, font);
        SetBkMode(dc, 1);
        SetTextColor(dc, 0xFFFFFF);
        var area = new Area { Right = size, Bottom = size };
        const int centred = 0x1 | 0x4 | 0x20 | 0x100;
        DrawText(dc, label, -1, ref area, centred);
        GdiFlush();
        // Text drawing leaves no transparency behind, so it is written afterwards.
        Marshal.Copy(pixels, disc, 0, disc.Length);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x + 0.5 - radius, 2) + Math.Pow(y + 0.5 - radius, 2));
                var alpha = (int)(Math.Clamp(radius - distance + 0.5, 0, 1) * 255);
                disc[y * size + x] = (alpha << 24) | (disc[y * size + x] & 0xFFFFFF);
            }
        Marshal.Copy(disc, 0, pixels, disc.Length);
        SelectObject(dc, oldFont);
        SelectObject(dc, previous);
        var info = new IconInfo { IsIcon = true, Mask = mask, Colour = colour };
        var icon = CreateIconIndirect(ref info);
        Free(dc, colour, mask, font);
        return icon;
    }

    private static void Free(nint dc, params nint[] objects)
    {
        foreach (var item in objects)
            if (item != 0) DeleteObject(item);
        if (dc != 0) DeleteDC(dc);
    }

    [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090")]
    private class TaskbarList;

    /// <summary>The methods before the one used only hold their places.</summary>
    [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        void HrInit();
        void AddTab();
        void DeleteTab();
        void ActivateTab();
        void SetActiveAlt();
        void MarkFullscreenWindow();
        void SetProgressValue();
        void SetProgressState();
        void RegisterTab();
        void UnregisterTab();
        void SetTabOrder();
        void SetTabActive();
        void ThumbBarAddButtons();
        void ThumbBarUpdateButtons();
        void ThumbBarSetImageList();
        void SetOverlayIcon(nint window, nint icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapHeader
    {
        public int Size, Width, Height;
        public short Planes, Bits;
        public int Compression, ImageSize, XPerMeter, YPerMeter, Used, Important;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Area
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        [MarshalAs(UnmanagedType.Bool)] public bool IsIcon;
        public int HotspotX, HotspotY;
        public nint Mask, Colour;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll")] private static extern nint CreateIconIndirect(ref IconInfo info);
    [DllImport("user32.dll", EntryPoint = "DrawTextW", CharSet = CharSet.Unicode)]
    private static extern int DrawText(nint dc, string text, int length, ref Area area, int format);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BitmapHeader header, int usage, out nint pixels,
        nint section, int offset);
    [DllImport("gdi32.dll")] private static extern nint CreateBitmap(int width, int height, int planes, int bits, nint data);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint item);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(nint dc, int mode);
    [DllImport("gdi32.dll")] private static extern int SetTextColor(nint dc, int colour);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode)]
    private static extern nint CreateFont(int height, int width, int escapement, int orientation, int weight,
        int italic, int underline, int strikeOut, int charSet, int outPrecision, int clipPrecision, int quality,
        int pitch, string face);
}
