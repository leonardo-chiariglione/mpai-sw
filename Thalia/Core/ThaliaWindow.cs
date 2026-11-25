using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

// THALIA'S WINDOW IS THALIA'S (2026-11-25). Thalia is shown by a browser window (Edge, full screen), and Windows would show it on the taskbar as Edge, with Edge's icon. Thalia marks the
// window as an application of its own - its Application User Model ID, the name Thalia and its icon (wwwroot\thalia.ico) - so that the taskbar shows it apart from Edge, with Thalia's face;
// pinned, it starts Thalia again. Only the windows of the browser whose title begins with "Thalia" are marked; they are looked at every few seconds, since the browser opens them after
// Thalia starts and may give them its own icon again.
static class ThaliaWindow
{
    const string AppId = "MPAI.Thalia";

    public static void Keep(string iconFile, string relaunch, Func<bool> stop)
    {
        if (!OperatingSystem.IsWindows()) return;
        var marked = new HashSet<IntPtr>();
        var thread = new System.Threading.Thread(() =>
        {
            IntPtr big = IntPtr.Zero, small = IntPtr.Zero;
            try { big = LoadImage(IntPtr.Zero, iconFile, 1, 256, 256, 0x10); small = LoadImage(IntPtr.Zero, iconFile, 1, 32, 32, 0x10); } catch { }   // IMAGE_ICON, LR_LOADFROMFILE
            while (!stop())
            {
                try
                {
                    foreach (var w in Windows())
                    {
                        if (big != IntPtr.Zero) SendMessage(w, 0x0080, (IntPtr)1, big);       // WM_SETICON, ICON_BIG
                        if (small != IntPtr.Zero) SendMessage(w, 0x0080, IntPtr.Zero, small);  // ICON_SMALL
                        if (!marked.Contains(w)) { Mark(w, iconFile, relaunch); marked.Add(w); Console.WriteLine($"[Thalia's window] {w}: shown as Thalia on the taskbar"); }
                    }
                }
                catch (Exception e) { Console.Error.WriteLine($"[Thalia's window] not marked: {e.Message}"); }
                System.Threading.Thread.Sleep(3000);
            }
        }) { IsBackground = true, Name = "Thalia's window" };
        thread.SetApartmentState(System.Threading.ApartmentState.STA);         // the Shell's property store of a window is asked from a single-threaded apartment
        thread.Start();
    }

    // The windows of the browser that show Thalia: visible, top-level, of msedge or chrome, titled Thalia...
    static List<IntPtr> Windows()
    {
        var found = new List<IntPtr>();
        var browsers = new HashSet<uint>();
        foreach (var name in new[] { "msedge", "chrome" }) foreach (var p in Process.GetProcessesByName(name)) { browsers.Add((uint)p.Id); p.Dispose(); }
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out var pid);
            if (!browsers.Contains(pid)) return true;
            var t = new StringBuilder(256); GetWindowText(h, t, 256);
            var title = t.ToString();
            if (title == "Thalia" || title.StartsWith("Thalia - ", StringComparison.Ordinal) || title.StartsWith("Thalia ", StringComparison.Ordinal)) found.Add(h);
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static void Mark(IntPtr window, string iconFile, string relaunch)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(window, ref iid, out var store) != 0 || store is null) return;
        try
        {
            Set(store, 5, AppId);                                  // System.AppUserModel.ID: its own button on the taskbar
            Set(store, 2, relaunch);                               // RelaunchCommand: what a pinned Thalia starts
            Set(store, 4, "Thalia");                               // RelaunchDisplayNameResource
            Set(store, 3, iconFile);                               // RelaunchIconResource
            store.Commit();
        }
        finally { Marshal.ReleaseComObject(store); }
    }

    static void Set(IPropertyStore store, uint pid, string value)
    {
        var key = new PropertyKey { fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), pid = pid };
        var v = new PropVariant { vt = 31, p = Marshal.StringToCoTaskMemUni(value) };      // VT_LPWSTR
        try { store.SetValue(ref key, ref v); } finally { Marshal.FreeCoTaskMem(v.p); }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)] struct PropertyKey { public Guid fmtid; public uint pid; }
    [StructLayout(LayoutKind.Explicit)] struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr p; [FieldOffset(16)] public IntPtr pad; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadImage(IntPtr inst, string name, uint type, int cx, int cy, uint load);
    [DllImport("shell32.dll")] static extern int SHGetPropertyStoreForWindow(IntPtr h, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
}
