using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitWolf.App.Services.Shell;

/// <summary>An entry of the tray menu. With no <see cref="Run"/> and no children it is a separator.</summary>
internal sealed record TrayItem(string Title = "", Action? Run = null, IReadOnlyList<TrayItem>? Children = null);

/// <summary>
/// The notification-area icon, with a native menu built only when it opens, and its notifications.
/// Messages arrive through the owning window. UI thread only.
/// </summary>
internal sealed class TrayIcon : IDisposable
{
    public const int Message = 0x8000 + 17;

    private const int Add = 0, Modify = 1, Delete = 2, SetVersion = 4;
    private const int FlagMessage = 1, FlagIcon = 2, FlagTip = 4, FlagInfo = 16, FlagShowTip = 128;
    private const int InfoPlain = 1, InfoWarning = 2;
    private const int Version4 = 4;
    private const int Select = 0x400, KeySelect = 0x401, Context = 0x7B;
    private const int BalloonHide = 0x403, BalloonTimeout = 0x404, BalloonClick = 0x405;
    private const int MenuString = 0, MenuPopup = 0x10, MenuSeparator = 0x800;
    private const int ReturnCommand = 0x100, RightButton = 2;

    private readonly nint _window;
    private readonly nint _icon;
    private bool _added;
    private bool _wanted;
    /// <summary>Added only to carry a notification, and removed once that has gone.</summary>
    private bool _temporary;
    private string _tip = "GitWolf";
    private Action? _balloonClicked;

    public TrayIcon(nint window, string iconFile)
    {
        _window = window;
        const int imageIcon = 1, loadFromFile = 0x10;
        _icon = LoadImage(0, iconFile, imageIcon, GetSystemMetrics(49), GetSystemMetrics(50), loadFromFile);
    }

    /// <summary>The icon was clicked.</summary>
    public event Action? Activated;

    /// <summary>Builds the menu each time it opens.</summary>
    public Func<IReadOnlyList<TrayItem>>? Menu { get; set; }

    public bool Visible
    {
        set
        {
            _wanted = value;
            if (value)
            {
                _temporary = false;
                Show();
            }
            else Remove();
        }
    }

    public string Tip
    {
        set
        {
            if (_tip == value) return;
            _tip = value;
            if (!_added) return;
            var data = Data(FlagTip | FlagShowTip);
            Shell_NotifyIcon(Modify, ref data);
        }
    }

    /// <summary>Puts the icon back after Explorer restarted.</summary>
    public void Restore()
    {
        _added = false;
        if (_wanted) Show();
    }

    public void Notify(string title, string text, bool warning, Action clicked)
    {
        if (!_added)
        {
            _temporary = true;
            Show();
        }
        _balloonClicked = clicked;
        var data = Data(FlagInfo | FlagShowTip | FlagTip);
        data.InfoTitle = Fit(title, 63);
        data.Info = Fit(text, 255);
        data.InfoFlags = warning ? InfoWarning : InfoPlain;
        if (!Shell_NotifyIcon(Modify, ref data)) Trace.TraceWarning("A notification could not be shown.");
    }

    /// <summary>Handles a <see cref="Message"/>.</summary>
    public void Receive(nint wParam, nint lParam)
    {
        switch ((int)(lParam & 0xFFFF))
        {
            case Select or KeySelect:
                Activated?.Invoke();
                break;
            case Context:
                // The pointer position, as signed 16-bit values: screens left of the main one are negative.
                ShowMenu((short)(wParam & 0xFFFF), (short)((wParam >> 16) & 0xFFFF));
                break;
            case BalloonClick:
                var clicked = _balloonClicked;
                EndBalloon();
                clicked?.Invoke();
                break;
            case BalloonHide or BalloonTimeout:
                EndBalloon();
                break;
        }
    }

    private void EndBalloon()
    {
        _balloonClicked = null;
        if (_temporary && !_wanted) Remove();
        _temporary = false;
    }

    private void Show()
    {
        if (_added) return;
        var data = Data(FlagMessage | FlagIcon | FlagTip | FlagShowTip);
        if (!Shell_NotifyIcon(Add, ref data)) return;
        _added = true;
        data.TimeoutOrVersion = Version4;
        Shell_NotifyIcon(SetVersion, ref data);
    }

    private void Remove()
    {
        if (!_added) return;
        var data = Data(0);
        Shell_NotifyIcon(Delete, ref data);
        _added = false;
    }

    private NotifyIconData Data(int flags) => new()
    {
        Size = Marshal.SizeOf<NotifyIconData>(), Window = _window, Id = 1, Flags = flags,
        CallbackMessage = Message, Icon = _icon, Tip = Fit(_tip, 127), Info = "", InfoTitle = "",
    };

    private static string Fit(string text, int length) => text.Length <= length ? text : text[..(length - 1)] + "…";

    private void ShowMenu(int x, int y)
    {
        if (Menu?.Invoke() is not { } items) return;
        var actions = new List<Action>();
        var menu = Build(items, actions);
        // Without this the menu would stay open after a click elsewhere.
        SetForegroundWindow(_window);
        var chosen = TrackPopupMenuEx(menu, ReturnCommand | RightButton, x, y, _window, 0);
        // Destroying a menu destroys its submenus too.
        DestroyMenu(menu);
        if (chosen > 0 && chosen <= actions.Count) actions[chosen - 1]();
    }

    private static nint Build(IReadOnlyList<TrayItem> items, List<Action> actions)
    {
        var menu = CreatePopupMenu();
        foreach (var item in items)
        {
            // A single "&" would underline the next letter.
            var title = item.Title.Replace("&", "&&");
            if (item.Children is { } children)
                AppendMenu(menu, MenuPopup, Build(children, actions), title);
            else if (item.Run is { } run)
            {
                actions.Add(run);
                AppendMenu(menu, MenuString, actions.Count, title);
            }
            else AppendMenu(menu, MenuSeparator, 0, null);
        }
        return menu;
    }

    public void Dispose()
    {
        _wanted = false;
        Remove();
        if (_icon != 0) DestroyIcon(_icon);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public int Size;
        public nint Window;
        public int Id;
        public int Flags;
        public int CallbackMessage;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Tip;
        public int State;
        public int StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string Info;
        public int TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string InfoTitle;
        public int InfoFlags;
        public Guid Item;
        public nint BalloonIcon;
    }

    [DllImport("shell32.dll", EntryPoint = "Shell_NotifyIconW", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NotifyIconData data);

    [DllImport("user32.dll", EntryPoint = "LoadImageW", CharSet = CharSet.Unicode)]
    private static extern nint LoadImage(nint instance, string name, int type, int width, int height, int flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint icon);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", EntryPoint = "AppendMenuW", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(nint menu, int flags, nint id, string? text);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(nint menu, int flags, int x, int y, nint window, nint parameters);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);
}
