using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GitWolf.App.Services.Shell;

/// <summary>
/// Recent repositories in the taskbar button's menu. Choosing one starts the program with its folder,
/// which hands it to the running copy.
/// </summary>
internal static class JumpList
{
    public const int MaxEntries = 8;

    public static void Show(IReadOnlyList<string> folders)
    {
        if (Environment.ProcessPath is not { } program) return;
        try
        {
            var list = (ICustomDestinationList)new DestinationList();
            var objectArray = new Guid("92CA9DCD-5622-4bba-A805-5E9F541BD8C9");
            list.BeginList(out _, ref objectArray, out var removed);
            var entries = (IObjectCollection)new ObjectCollection();
            foreach (var folder in folders.Take(MaxEntries))
            {
                var link = (IShellLink)new ShellLink();
                link.SetPath(program);
                link.SetArguments($"\"{folder}\"");
                link.SetDescription(folder);
                link.SetIconLocation(program, 0);
                var title = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
                SetTitle((IPropertyStore)link, title.Length == 0 ? folder : title);
                entries.AddObject(link);
            }
            if (folders.Count > 0) list.AddUserTasks(entries);
            list.CommitList();
            Marshal.ReleaseComObject(removed);
            Marshal.ReleaseComObject(entries);
            Marshal.ReleaseComObject(list);
        }
        catch (Exception e) when (e is COMException or UnauthorizedAccessException)
        {
            // The menu is a convenience; some Windows settings turn it off.
            Trace.TraceInformation($"The jump list wasn't updated (0x{e.HResult:X8}).");
        }
    }

    private static void SetTitle(IPropertyStore store, string title)
    {
        var key = new PropertyKey { Format = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), Id = 2 };
        const short unicodeText = 31;
        var value = new PropVariant { Type = unicodeText, Pointer = Marshal.StringToCoTaskMemUni(title) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            Marshal.FreeCoTaskMem(value.Pointer);
        }
    }

    [ComImport, Guid("77f10cf0-3db5-4966-b520-b7c54fd35ed6")]
    private class DestinationList;

    [ComImport, Guid("2d3468c1-36a7-43b6-ac24-d3f02fd9607a")]
    private class ObjectCollection;

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink;

    // In these interfaces, methods without parameters that aren't called only hold their places.

    [ComImport, Guid("6332debf-87b5-4670-90c0-5e57b408a49e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        void SetAppID();
        void BeginList(out int slots, ref Guid id, [MarshalAs(UnmanagedType.IUnknown)] out object removed);
        void AppendCategory();
        void AppendKnownCategory();
        void AddUserTasks([MarshalAs(UnmanagedType.IUnknown)] object tasks);
        void CommitList();
    }

    [ComImport, Guid("5632b1a4-e38a-400a-928a-d4cd63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        void GetCount();
        void GetAt();
        void AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        void GetPath();
        void GetIDList();
        void SetIDList();
        void GetDescription();
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        void GetWorkingDirectory();
        void SetWorkingDirectory();
        void GetArguments();
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey();
        void SetHotkey();
        void GetShowCmd();
        void SetShowCmd();
        void GetIconLocation();
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string file, int index);
        void SetRelativePath();
        void Resolve();
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        void GetCount();
        void GetAt();
        void GetValue();
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey
    {
        public Guid Format;
        public int Id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public short Type, Reserved1, Reserved2, Reserved3;
        public nint Pointer;
        public nint Rest;
    }
}
