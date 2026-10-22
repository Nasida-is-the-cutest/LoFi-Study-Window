using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using PotatoOptimization.Utilities;
using PotatoOptimization.Core;

class Program
{
    delegate IntPtr WndProc(IntPtr window, uint message, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WindowClass { public uint Style; public WndProc Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Brush; public string Menu, Name; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern ushort RegisterClass(ref WindowClass wc);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    static WndProc _procedure = DefWindowProc;
    static int _checks;
    static IntPtr MakeWindow()
    {
        var wc = new WindowClass { Procedure = _procedure, Name = "UnityWndClass", Instance = GetModuleHandle(null) };
        RegisterClass(ref wc);
        return CreateWindowEx(0, wc.Name, "iGPU native validation (hidden)", 0x00CF0000, 40, 40, 480, 270, IntPtr.Zero, IntPtr.Zero, wc.Instance, IntPtr.Zero);
    }
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); _checks++; }
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "foreign")
        { var foreign = MakeWindow(); File.WriteAllText(args[1], foreign.ToInt64().ToString()); Thread.Sleep(30000); DestroyWindow(foreign); return; }
        string marker = Path.Combine(Path.GetTempPath(), "igpu-native-" + Guid.NewGuid().ToString("N"));
        var start = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add("foreign"); start.ArgumentList.Add(marker);
        using var child = Process.Start(start);
        IntPtr owned = IntPtr.Zero;
        try
        {
            var deadline = Stopwatch.StartNew(); while (!File.Exists(marker) && deadline.Elapsed.TotalSeconds < 10) Thread.Sleep(50);
            Check(File.Exists(marker), "Second process did not create its test window");
            var foreign = new IntPtr(long.Parse(File.ReadAllText(marker)));
            owned = MakeWindow(); Check(owned != IntPtr.Zero, "Owned window not created");
            Check(WindowManager.IsOwnedWindow(owned), "Owned window rejected");
            Check(!WindowManager.IsOwnedWindow(foreign), "Foreign Unity window accepted");
            var foreignStyle = WindowManager.GetWindowStyle(foreign, Constants.GWL_STYLE);
            Check(!WindowManager.MoveWindow(foreign, 500, 500), "Foreign move accepted");
            Check(!WindowManager.ResizeWindow(foreign, 800, 600), "Foreign resize accepted");
            Check(!WindowManager.ResizeClientArea(foreign, 800, 600), "Foreign client resize accepted");
            Check(WindowManager.SetWindowStyle(foreign, Constants.GWL_STYLE, IntPtr.Zero) == IntPtr.Zero, "Foreign style mutation accepted");
            Check(WindowManager.GetWindowStyle(foreign, Constants.GWL_STYLE) == foreignStyle, "Foreign style changed");
            Check(WindowManager.GetCurrentWindowHandle() == owned, "Wrong PID selected");
            var style = WindowManager.GetWindowStyle(owned, Constants.GWL_STYLE); var extended = WindowManager.GetWindowStyle(owned, Constants.GWL_EXSTYLE);
            var proc = WindowManager.GetWindowStyle(owned, -4); var foreground = GetForegroundWindow();
            // Simulate a destroyed HWND whose cached numeric value has since
            // been assigned to the foreign process's Unity window.
            typeof(WindowManager).GetField("_hookWindow", BindingFlags.NonPublic|BindingFlags.Static).SetValue(null, foreign);
            typeof(WindowManager).GetField("_pipWindowProcInstalled", BindingFlags.NonPublic|BindingFlags.Static).SetValue(null, true);
            WindowManager.SetPiPWindowStyle(owned);
            Check((WindowManager.GetWindowStyle(owned, Constants.GWL_EXSTYLE).ToInt64() & 8) != 0, "Recycled foreign HWND blocked a new owned hook");
            WindowManager.RestoreWindowStyle(owned, style, extended);
            for (int i = 0; i < 10; i++)
            {
                WindowManager.SetPiPWindowStyle(owned);
                Check((WindowManager.GetWindowStyle(owned, Constants.GWL_EXSTYLE).ToInt64() & 8) != 0, "PiP not topmost");
                Check(WindowManager.ResizeWindow(owned, 480, 270), "Resize failed");
                Check(WindowManager.MoveWindow(owned, 100, 100), "Move failed");
                Check(GetForegroundWindow() == foreground, "PiP stole focus");
                WindowManager.RestoreWindowStyle(owned, style, extended);
                Check(WindowManager.GetWindowStyle(owned, Constants.GWL_STYLE) == style, "Style not restored");
                Check(WindowManager.GetWindowStyle(owned, Constants.GWL_EXSTYLE) == extended, "Extended style not restored");
                Check(WindowManager.GetWindowStyle(owned, -4) == proc, "Window procedure not restored");
            }
            Check((WindowManager.GetWindowStyle(foreign, Constants.GWL_EXSTYLE).ToInt64() & 8) == 0, "Foreign window changed");
            WindowManager.RestoreWindowStyle(owned, style, extended, true);
            Check((WindowManager.GetWindowStyle(owned, Constants.GWL_STYLE).ToInt64() & 0x10000000L) != 0, "Restored game window stayed hidden");
            Check(GetForegroundWindow() == foreground, "Showing the restored window stole focus");
            WindowManager.RestoreWindowStyle(owned, style, extended);
            var area = WindowManager.WorkingArea(owned); Check(area.Right > area.Left && area.Bottom > area.Top, "Bad working area");
            DestroyWindow(owned); owned = MakeWindow(); Check(WindowManager.GetCurrentWindowHandle() == owned, "Stale HWND was reused");
            Console.WriteLine("Native window checks passed: " + _checks);
        }
        finally { if (owned != IntPtr.Zero) DestroyWindow(owned); if (!child.HasExited) child.Kill(); if (File.Exists(marker)) File.Delete(marker); }
    }
}
namespace PotatoOptimization.Core
{
    public static class PotatoPlugin { public static TestLogger Log { get; } = new TestLogger(); }
    public class TestLogger { public void LogError(object value) { Console.Error.WriteLine(value); } }
}
