using System.IO;
using System.Reflection;
using System.Windows.Media.Imaging;

internal static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var assembly = typeof(DriverPilot.MainWindow).Assembly; // Does not construct MainWindow or App.
            using var stream = assembly.GetManifestResourceStream("DriverPilot.Assets.DriverPilot.ico");
            Require(stream != null, "Production assembly embeds DriverPilot.Assets.DriverPilot.ico");
            var decoder = new IconBitmapDecoder(stream!, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int[] expected = [16, 20, 24, 32, 40, 48, 64, 128, 256];
            var actual = decoder.Frames.Select(f => f.PixelWidth).Order().ToArray();
            Require(actual.SequenceEqual(expected), "ICO sizes: " + string.Join(",", actual));
            foreach (var frame in decoder.Frames)
                Require(frame.PixelWidth == frame.PixelHeight, "Square ICO frame " + frame.PixelWidth);
            Console.WriteLine("PASS embedded ICO decodes at all nine required sizes");
            var helper = assembly.GetType("DriverPilot.AppIcon");
            Require(helper != null, "Production AppIcon loader exists");
            var source = (System.Windows.Media.ImageSource)helper!.GetMethod("CreateWindowIcon")!.Invoke(null, null)!;
            Require(source.IsFrozen, "Window image is cached independently of its closed resource stream");
            using var trayIcon = (System.Drawing.Icon)helper.GetMethod("CreateTrayIcon")!.Invoke(null, null)!;
            using var tray = new System.Windows.Forms.NotifyIcon { Icon = trayIcon, Text = "DriverPilot icon test" };
            var window = new System.Windows.Window { Title = "DriverPilot icon test (isolated)", Width = 360, Height = 180,
                ShowActivated = false, Icon = source, Content = new System.Windows.Controls.Image { Source = source, Width = 64, Height = 64 } };
            try
            {
                window.Show();
                window.UpdateLayout();
                window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                ValidateIcon(SendMessage(hwnd, 0x007F, 0, 0), "WM_GETICON small");
                ValidateIcon(SendMessage(hwnd, 0x007F, 1, 0), "WM_GETICON large");
                var render = new RenderTargetBitmap(360, 180, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                render.Render(window);
                Require(render.PixelWidth == 360 && window.IsVisible, "Minimal real WPF window rendered");
                tray.Visible = true;
                Require(tray.Visible && ReferenceEquals(tray.Icon, trayIcon), "NotifyIcon assigned production icon");
                ValidateIcon(trayIcon.Handle, "NotifyIcon native handle after stream closure");
                GC.Collect(); GC.WaitForPendingFinalizers();
                ValidateIcon(trayIcon.Handle, "NotifyIcon handle retained across GC");
                Console.WriteLine("PASS real WPF Window rendered; WM_GETICON small/large valid; NotifyIcon assignment and native handle valid");
            }
            finally { tray.Visible = false; tray.Dispose(); window.Close(); }
            var ownedHandle = trayIcon.Handle;
            trayIcon.Dispose();
            Require(!GetIconInfo(ownedHandle, out var disposedInfo), "Caller disposal destroys the owned tray HICON");
            trayIcon.Dispose();
            Console.WriteLine("PASS tray HICON destroyed after consumer disposal; repeated disposal safe");
            var executable = args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "DriverPilot.exe") : Path.GetFullPath(args[0]);
            Require(File.Exists(executable), "Native icon test executable exists: " + executable);
            var large = new nint[1]; var small = new nint[1];
            try
            {
                var extracted = ExtractIconEx(executable, 0, large, small, 1);
                Require(extracted > 0 && extracted != uint.MaxValue, "Executable exposes native icon group (ExtractIconEx=" + extracted + ", Win32=" + System.Runtime.InteropServices.Marshal.GetLastWin32Error() + ")");
                ValidateIcon(large[0], "EXE large"); ValidateIcon(small[0], "EXE small");
                using var actualLarge = System.Drawing.Icon.FromHandle(large[0]).ToBitmap();
                using var actualSmall = System.Drawing.Icon.FromHandle(small[0]).ToBitmap();
                CompareBrandPixels(assembly, actualLarge);
                CompareBrandPixels(assembly, actualSmall);
                Console.WriteLine("PASS ExtractIconEx large/small pixels match embedded brand ICO: " + executable);
            }
            finally { if (large[0] != 0) DestroyIcon(large[0]); if (small[0] != 0) DestroyIcon(small[0]); }
            // Source wiring guards complement native tests without constructing the side-effecting MainWindow.
            using var sourceReader = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("MainWindowSource")!);
            var mainSource = sourceReader.ReadToEnd();
            Require(mainSource.Contains("Icon=AppIcon.CreateWindowIcon()"), "MainWindow uses production window icon loader");
            Require(mainSource.Contains("trayIcon=AppIcon.CreateTrayIcon()") && mainSource.Contains("tray.Icon=trayIcon"), "MainWindow retains owned tray icon");
            Require(!mainSource.Contains("SystemIcons.Shield"), "System shield replaced");
            foreach (var method in new[] { "void OnClosing(", "void StopForTest(" })
            {
                var line = mainSource.Split('\n').Single(l => l.Contains(method));
                Require(line.Contains("tray.Dispose();trayIcon.Dispose();"), method + " disposes consumer before icon");
            }
            using var installerReader = new StreamReader(typeof(Program).Assembly.GetManifestResourceStream("InstallerSource")!);
            Require(installerReader.ReadToEnd().Contains("SetupIconFile=../assets/branding/DriverPilot.ico"), "Installer uses source brand ICO");
            Console.WriteLine("PASS MainWindow loader/lifetime and installer source wiring guards (no MainWindow startup)");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine("FAIL " + ex.Message); return 1; }
    }
    static void CompareBrandPixels(Assembly assembly, System.Drawing.Bitmap actual)
    {
        using var input = assembly.GetManifestResourceStream("DriverPilot.Assets.DriverPilot.ico")!;
        using var expectedIcon = new System.Drawing.Icon(input, actual.Size);
        using var expected = expectedIcon.ToBitmap();
        Require(expected.Size == actual.Size, "Native brand icon dimensions");
        for (int y = 0; y < actual.Height; y++)
            for (int x = 0; x < actual.Width; x++)
                Require(actual.GetPixel(x, y).ToArgb() == expected.GetPixel(x, y).ToArgb(), "Native executable icon matches brand at " + x + "," + y);
    }
    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    static extern uint ExtractIconEx(string path, int index, nint[] large, nint[] small, uint count);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool DestroyIcon(nint value);
    static void ValidateIcon(nint handle, string label)
    {
        Require(handle != 0, label + " exists");
        Require(GetIconInfo(handle, out var info), label + " is a real HICON");
        if (info.Color != 0) DeleteObject(info.Color);
        if (info.Mask != 0) DeleteObject(info.Mask);
    }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct IconInfo { public int IsIcon; public uint X; public uint Y; public nint Mask; public nint Color; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern nint SendMessage(nint hwnd, uint message, nint wparam, nint lparam);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool GetIconInfo(nint icon, out IconInfo info);
    [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool DeleteObject(nint value);
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
}
