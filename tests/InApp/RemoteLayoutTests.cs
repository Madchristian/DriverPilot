using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ComboBox = System.Windows.Controls.ComboBox;
using Button = System.Windows.Controls.Button;
using Brush = System.Windows.Media.Brush;
namespace DriverPilot;

public static class RemoteLayoutTests
{
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr hwnd, IntPtr dc, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RectNative rect);
    [StructLayout(LayoutKind.Sequential)] struct RectNative { public int Left, Top, Right, Bottom; }
    static void Check(bool value, string message) { if (!value) throw new Exception("FAIL: " + message); }
    static async Task Idle() { await Task.Delay(80); await Dispatcher.CurrentDispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle); }
    static IEnumerable<T> Children<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var item in Children<T>(child)) yield return item;
        }
    }
    static System.Drawing.Bitmap Capture(IntPtr hwnd)
    {
        Check(GetWindowRect(hwnd, out var r), "capture rectangle");
        var bitmap = new System.Drawing.Bitmap(r.Right-r.Left, r.Bottom-r.Top);
        using var g = System.Drawing.Graphics.FromImage(bitmap);
        var dc = g.GetHdc();
        try { Check(PrintWindow(hwnd, dc, 2), "PrintWindow captures HWND"); }
        finally { g.ReleaseHdc(dc); }
        return bitmap;
    }
    static void Screenshot(Window window, ComboBox combo, string path)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        using var bitmap = Capture(hwnd);
        var popup = Children<Popup>(combo).FirstOrDefault(p => p.IsOpen);
        if (popup?.Child is Visual child && PresentationSource.FromVisual(child) is HwndSource source)
        {
            using var image = Capture(source.Handle);
            GetWindowRect(hwnd, out var a); GetWindowRect(source.Handle, out var b);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.DrawImageUnscaled(image, b.Left-a.Left, b.Top-a.Top);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    static double Luminance(SolidColorBrush brush)
    {
        double C(byte v) { double x=v/255d; return x<=0.04045 ? x/12.92 : Math.Pow((x+0.055)/1.055,2.4); }
        var c=brush.Color; return .2126*C(c.R)+.7152*C(c.G)+.0722*C(c.B);
    }
    static void Contrast(Brush foreground, Brush background, string name)
    {
        Check(foreground is SolidColorBrush && background is SolidColorBrush, name+" concrete brushes");
        double a=Luminance((SolidColorBrush)foreground), b=Luminance((SolidColorBrush)background);
        Check((Math.Max(a,b)+.05)/(Math.Min(a,b)+.05)>=4.5, name+" text contrast >= 4.5");
    }
    public static async Task Run(RemoteDiagnosisWindow window, string output)
    {
        var combo=(ComboBox)window.FindName("RemoteCategory");
        var originalSelection=combo.SelectedIndex;
        var screen = System.Windows.Forms.Screen.FromHandle(new WindowInteropHelper(window).Handle).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(window);
        Check(window.Height <= screen.Height / dpi.DpiScaleY, "initial dialog height fits monitor work area in DIPs");
        var fit = typeof(RemoteDiagnosisWindow).GetMethod("FitWorkArea", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Check(fit != null, "responsive work-area constraint exists");
        foreach (var scale in new[] { 1d, 1.25, 1.5, 2d })
        {
            fit!.Invoke(window, new object[] { 1920d / scale, 1040d / scale });
            Check(window.Height <= 1040 / scale && window.MinHeight <= 1040 / scale, "work area height at scale " + scale);
            Check(window.Width <= 1920 / scale && window.MinWidth <= 1920 / scale, "work area width at scale " + scale);
        }
        fit!.Invoke(window, new object[] { screen.Width / dpi.DpiScaleX, screen.Height / dpi.DpiScaleY });
        var errors=new List<string>();
        foreach(var (width,height,name) in new[]{(1000d,940d,"default"),(760d,620d,"minimum"),(760d,520d,"scaled-200")})
        {
            window.MinHeight=Math.Min(window.MinHeight,height); window.Width=width; window.Height=height;
            combo.BringIntoView(); window.UpdateLayout(); await Idle();
            Screenshot(window,combo,Path.Combine(output,"dialog-"+name+".png"));
            combo.IsDropDownOpen=true; await Idle(); await Task.Delay(350);
            Screenshot(window,combo,Path.Combine(output,"dropdown-"+name+".png"));
            combo.IsDropDownOpen=false; await Idle();
            var client=(FrameworkElement)window.Content;
            foreach(var control in new[]{"RemoteConsent","RemoteReview","RemoteSend","RemoteExport"})
            {
                var element=(FrameworkElement)window.FindName(control);
                var p=element.TransformToAncestor(client).Transform(new System.Windows.Point());
                if(p.X<0 || p.Y<0 || p.X+element.ActualWidth>client.ActualWidth+.5 || p.Y+element.ActualHeight>client.ActualHeight+.5)
                    errors.Add(name+" persistent footer outside client: "+control);
            }
        }
        Check(errors.Count==0,string.Join("; ",errors));
        combo.ApplyTemplate();
        var face=combo.Template.FindName("Face",combo) as Border;
        Check(face!=null,"themed collapsed face replaces OS chrome");
        var value=combo.Template.FindName("SelectionText",combo) as TextBlock;
        Check(value!=null,"collapsed selected text is explicitly themed");
        Contrast(value!.Foreground,face!.Background,"collapsed selected value");
        // Exercise the real WPF template trigger without relying on the interactive desktop's
        // foreground owner or moving the user's mouse. Only test code writes this read-only key.
        var mouseKey=(DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
        try
        {
            combo.SetValue(mouseKey,true); await Idle();
            Contrast(value.Foreground,face.Background,"hover collapsed selected value");
        }
        finally { combo.SetValue(mouseKey,false); }
        // The published test is launched with -WindowStyle Hidden in CI. Show()/layout
        // and a rendered popup do not establish native keyboard activation. Request it
        // explicitly, then let dispatcher/native activation settle before testing focus.
        // Activate() can return false even when IsActive becomes true on the next turn.
        window.Activate();
        var activationDeadline = System.Diagnostics.Stopwatch.StartNew();
        while (!window.IsActive && activationDeadline.Elapsed < TimeSpan.FromSeconds(2))
        {
            await Task.Delay(10);
            await window.Dispatcher.InvokeAsync(() => {}, DispatcherPriority.ApplicationIdle);
        }
        Check(window.IsActive, "dialog activated for real keyboard focus within test deadline");
        combo.IsDropDownOpen=true; await Idle();
        foreach(var item in combo.Items)
        {
            var container=(ComboBoxItem)combo.ItemContainerGenerator.ContainerFromItem(item);
            container.ApplyTemplate();
            var bg=(Border)container.Template.FindName("ItemBorder",container);
            var text=(ContentPresenter)container.Template.FindName("ItemText",container);
            Contrast(TextBlock.GetForeground(text),bg.Background,"popup item");
            foreach(var renderedText in Children<TextBlock>(text))
                Contrast(renderedText.Foreground,bg.Background,"actual popup text");
            container.IsSelected=true; await Idle();
            Contrast(TextBlock.GetForeground(text),bg.Background,"selected popup item");
            container.Focus(); await Idle();
            Check(container.IsKeyboardFocusWithin,"popup item keyboard focus");
            Contrast(TextBlock.GetForeground(text),bg.Background,"keyboard highlighted popup item");
            Contrast(bg.BorderBrush,bg.Background,"keyboard focus ring");
            container.IsEnabled=false; await Idle();
            Contrast(TextBlock.GetForeground(text),bg.Background,"disabled popup item");
            container.IsEnabled=true;
        }
        combo.IsDropDownOpen=false; combo.IsEnabled=false; await Idle();
        Contrast(value.Foreground,face.Background,"disabled collapsed value"); combo.IsEnabled=true;
        combo.SelectedIndex=originalSelection;
        ((System.Windows.Controls.CheckBox)window.FindName("RemoteConsent")).IsChecked=true;
        ((Button)window.FindName("RemoteReview")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var send=(Button)window.FindName("RemoteSend");
        Check(send.Focus() && send.IsKeyboardFocused,"enabled send keyboard focus");
        Check(send.MoveFocus(new System.Windows.Input.TraversalRequest(System.Windows.Input.FocusNavigationDirection.Next)),"footer keyboard traversal");
    }
}
