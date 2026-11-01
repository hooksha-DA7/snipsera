using System;
using System.Collections.Specialized;
using System.IO;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public static class DragExport
{
    public static DataObject CreateData(string dataRoot, BitmapSource image)
    {
        string folder = Path.Combine(dataRoot, "drag-exports"); Directory.CreateDirectory(folder);
        // Targets may read the file after DoDragDrop returns. Retain it for seven days.
        foreach (string file in Directory.EnumerateFiles(folder, "*.png"))
            if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) && File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".png"); CaptureService.Save(image, path);
        var data = new DataObject(); data.SetFileDropList(new StringCollection { path }); data.SetImage(image); return data;
    }
    public static void Attach(FrameworkElement source, string root, Func<BitmapSource> image, Action<Exception> report, bool ignoreControls = false)
    {
        Point? origin = null;
        source.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount != 1 || ignoreControls && IsControl(e.OriginalSource as DependencyObject)) return;
            origin = e.GetPosition(source); source.CaptureMouse(); e.Handled = true;
        };
        source.PreviewMouseLeftButtonUp += (_, _) => { origin = null; if (source.IsMouseCaptured) source.ReleaseMouseCapture(); };
        source.LostMouseCapture += (_, _) => origin = null;
        source.PreviewMouseMove += (_, e) =>
        {
            if (origin is not Point start || e.LeftButton != MouseButtonState.Pressed) return;
            var delta = e.GetPosition(source) - start;
            if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            origin = null; source.ReleaseMouseCapture();
            try { DragDrop.DoDragDrop(source, CreateData(root, image()), DragDropEffects.Copy); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException) { report(ex); }
            e.Handled = true;
        };
    }
    public static bool IsControl(DependencyObject? source)
    {
        while (source is not null) { if (source is ButtonBase or Thumb) return true; source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source); } return false;
    }
}
