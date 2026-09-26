using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DriverPilot;

/// <summary>Loads the embedded brand icon without depending on loose files or application startup.</summary>
internal static class AppIcon
{
    static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("DriverPilot.Assets.DriverPilot.ico")
        ?? throw new InvalidOperationException("The embedded DriverPilot icon is missing.");

    public static ImageSource CreateWindowIcon()
    {
        using var stream = Open();
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        frame.Freeze();
        return frame;
    }

    /// <summary>The caller owns this detached icon; dispose it after disposing the NotifyIcon consumer.</summary>
    public static System.Drawing.Icon CreateTrayIcon()
    {
        using var stream = Open();
        using var original = new System.Drawing.Icon(stream, System.Windows.Forms.SystemInformation.SmallIconSize);
        return (System.Drawing.Icon)original.Clone();
    }
}
