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

    // THE WINDOW OF AN APP, ON THE STAGE: the visible top-level windows of these processes and of the processes they started are made owned by Thalia's window (in front of it, and with it);
    // a window that can be resized is placed on the rectangle (x, y, w, h: pixels of the screen), a dialog is only brought in front; hide: they are hidden. Returns how many windows.
    static readonly HashSet<IntPtr> hiddenByThalia = new();
    public static int Place(IEnumerable<int> roots, int x, int y, int w, int h, bool hide)
    {
        if (!OperatingSystem.IsWindows()) return 0;
        var thalia = Windows();
        var pids = Descendants(roots);
        var mine = new List<IntPtr>();
        EnumWindows((hw, _) =>
        {
            if (!IsWindowVisible(hw) && !hiddenByThalia.Contains(hw)) return true;   // an invisible window is shown again only when Thalia is the one that hid it
            GetWindowThreadProcessId(hw, out var pid);
            if (!pids.Contains(pid)) return true;
            var t = new StringBuilder(256); GetWindowText(hw, t, 256);
            if (t.Length == 0) return true;                                           // the invisible helpers of a program (IME, GDI+) have no title
            mine.Add(hw); return true;
        }, IntPtr.Zero);
        var main = thalia.Count > 0 ? thalia[0] : IntPtr.Zero;
        // INSIDE WHAT THE SCREEN SHOWS (2026-11-25): the stage is measured on the whole screen, but the taskbar of Windows stays over its bottom - ASM's buttons were under it. The rectangle
        // is kept inside the work area of the screen it is on.
        var mon = MonitorFromPoint(new Pt { X = x + w / 2, Y = y + h / 2 }, 2);          // MONITOR_DEFAULTTONEAREST
        var info = new MonitorInfo { cbSize = Marshal.SizeOf<MonitorInfo>() };
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref info))
        {
            var work = info.rcWork;
            if (x < work.L) { w -= work.L - x; x = work.L; }
            if (y < work.T) { h -= work.T - y; y = work.T; }
            if (x + w > work.R) w = work.R - x;
            if (y + h > work.B) h = work.B - y;
        }
        foreach (var hw in mine)
        {
            // A window owned by another window of the App (a dialog of ASM's window) stays with its owner, in front of it: it is not placed, and is hidden and shown with it.
            var owner = GetWindow(hw, 4);                                             // GW_OWNER
            if (owner != IntPtr.Zero && owner != main) continue;
            if (hide) { ShowWindow(hw, 0); lock (hiddenByThalia) hiddenByThalia.Add(hw); continue; }   // SW_HIDE
            var wasHidden = false; lock (hiddenByThalia) wasHidden = hiddenByThalia.Remove(hw);
            var first = owner != main;                                                // not yet Thalia's
            if (first && main != IntPtr.Zero) SetWindowLongPtr(hw, -8, main);         // GWLP_HWNDPARENT: owned by Thalia's window
            var cls = new StringBuilder(64); GetClassName(hw, cls, 64);
            var resizable = (GetWindowLong(hw, -16) & 0x00040000) != 0 && cls.ToString() != "#32770";   // WS_THICKFRAME, and not a dialog
            if (wasHidden) ShowWindow(hw, 5);                                         // SW_SHOW
            if (resizable && w > 100 && h > 100)
            {
                GetWindowRect(hw, out var r);
                if (IsIconic(hw) || IsZoomed(hw)) ShowWindow(hw, 9);                  // SW_RESTORE
                if (r.L != x || r.T != y || r.R - r.L != w || r.B - r.T != h)         // placed only when it is not where it should be: nothing flickers
                    SetWindowPos(hw, IntPtr.Zero, x, y, w, h, 0x0004 | 0x0010);      // SWP_NOZORDER | SWP_NOACTIVATE
            }
            if (first || wasHidden) BringWindowToTop(hw);
        }
        return mine.Count;
    }

    // the processes started by these, and by those, as Windows records who started whom
    static HashSet<uint> Descendants(IEnumerable<int> roots)
    {
        var parent = new Dictionary<uint, uint>();
        var snap = CreateToolhelp32Snapshot(0x2, 0);                                  // TH32CS_SNAPPROCESS
        try
        {
            var e = new ProcessEntry { dwSize = (uint)Marshal.SizeOf<ProcessEntry>() };
            if (Process32First(snap, ref e)) do { parent[e.th32ProcessID] = e.th32ParentProcessID; } while (Process32Next(snap, ref e));
        }
        finally { CloseHandle(snap); }
        var set = new HashSet<uint>(roots.Select(r => (uint)r));
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var (child, par) in parent) if (!set.Contains(child) && set.Contains(par)) { set.Add(child); grew = true; }
        }
        return set;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ProcessEntry
    {
        public uint dwSize, cntUsage, th32ProcessID; public IntPtr th32DefaultHeapID; public uint th32ModuleID, cntThreads, th32ParentProcessID; public int pcPriClassBase; public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }
    [DllImport("kernel32.dll")] static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW")] static extern bool Process32First(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW")] static extern bool Process32Next(IntPtr snap, ref ProcessEntry e);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr h, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int L, T, R, B; }
    [StructLayout(LayoutKind.Sequential)] struct Pt { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int cbSize; public Rect rcMonitor, rcWork; public uint dwFlags; }
    [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(Pt p, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr mon, ref MonitorInfo info);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect r);
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);

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
