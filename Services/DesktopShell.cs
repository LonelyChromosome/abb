using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace HenshinGrid.Services;

// Animated desktop mode uses an undocumented Explorer WorkerW window and is best-effort.
// It makes no changes to Explorer, game files, kernel services, or process memory.
public static class DesktopShell
{
    private const uint WM_SPAWN_WORKERW = 0x052C;
    private const int GWL_STYLE = -16;
    private const long WS_CHILD = 0x40000000;
    private const long WS_POPUP = unchecked((long)0x80000000);
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SPI_SETDESKWALLPAPER = 0x0014;
    private const uint SPIF_UPDATEINIFILE = 0x0001;
    private const uint SPIF_SENDCHANGE = 0x0002;

    private delegate bool EnumWindowsProc(nint hwnd, nint lparam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindow(string className, string? windowName);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint FindWindowEx(nint parent, nint childAfter, string className, string? windowName);
    [DllImport("user32.dll")]
    private static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeoutMs, out nint result);
    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint param);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint newParent);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hwnd, nint hwndAfter, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, string path, uint flags);

    public static bool SetStaticWallpaper(string path) => File.Exists(path) &&
        SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, path, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);

    public static bool TryAttach(Window window)
    {
        nint progman = FindWindow("Progman", null);
        if (progman == 0) return false;
        SendMessageTimeout(progman, WM_SPAWN_WORKERW, (nint)0xD, (nint)1, 0, 1000, out _);
        SendMessageTimeout(progman, WM_SPAWN_WORKERW, 0, 0, 0, 1000, out _);
        nint target = 0;
        EnumWindows((top, _) =>
        {
            if (FindWindowEx(top, 0, "SHELLDLL_DefView", null) == 0) return true;
            target = FindWindowEx(0, top, "WorkerW", null);
            return target == 0;
        }, 0);
        if (target == 0) target = FindWindowEx(progman, 0, "WorkerW", null);
        if (target == 0) return false;

        nint hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == 0) return false;
        nint style = GetWindowLongPtr(hwnd, GWL_STYLE);
        nint newStyle = (nint)((style.ToInt64() | WS_CHILD) & ~WS_POPUP);
        SetWindowLongPtr(hwnd, GWL_STYLE, newStyle);
        SetLastError(0);
        nint prev = SetParent(hwnd, target);
        if (prev == 0 && Marshal.GetLastWin32Error() != 0) return false;
        var width = (int)SystemParameters.PrimaryScreenWidth;
        var height = (int)SystemParameters.PrimaryScreenHeight;
        return SetWindowPos(hwnd, (nint)1, 0, 0, width, height, SWP_NOACTIVATE);
    }

    [DllImport("kernel32.dll")]
    private static extern void SetLastError(uint error);
}