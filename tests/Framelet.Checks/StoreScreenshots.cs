using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Framelet;

internal static class StoreScreenshots
{
    internal static int Run(string output)
    {
        output = Path.GetFullPath(output);
        if (Directory.Exists(output)) throw new IOException("Choose a fresh screenshot folder.");
        Directory.CreateDirectory(output);
        var app = new CaptureApp(Path.Combine(output, "synthetic-history"), _ => throw new InvalidOperationException("Store examples never capture the desktop."));
#pragma warning disable WPF0001
        app.ThemeMode = ThemeMode.Dark;
#pragma warning restore WPF0001
        Appearance.Current.Refresh();
        var image = UiFixture.Scene();
        app.History.Add(image, CaptureKind.Area);
        var document = new EditDocument(image);
        document.Add(new Mark(MarkKind.Arrow, [new(620, 105), new(505, 275)], "#F34461", 5, Style: MarkStyle.Sketch, Arrow: ArrowStyle.Curved));
        document.Add(new Mark(MarkKind.Ellipse, [new(72, 76), new(410, 145)], "#F34461", 4, Style: MarkStyle.Sketch));
        document.Add(new Mark(MarkKind.Text, [new(572, 70)], "#F34461", Text: "A little more focus", FontSize: 23, Style: MarkStyle.Sketch));
        document.MarkSaved();
        var home = new HomeWindow(app);
        var editor = new EditorWindow(document, _ => { }, () => { }, "Studio notes");
        var recording = new RecordingWindow(app);
        try
        {
            Save(home, Path.Combine(output, "01-history.png"));
            Save(editor, Path.Combine(output, "02-editor.png"));
            Save(recording, Path.Combine(output, "03-recording.png"));
        }
        finally { document.MarkSaved(); app.Shutdown(); }
        File.WriteAllText(Path.Combine(output, "README.txt"), "Actual Snipsera client controls rendered at 1600x900 with synthetic example content. No desktop pixels, microphone audio, user captures or account information were read. Native non-client window chrome is outside these client-area images.");
        return 0;
    }

    private static void Save(Window window, string path)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -30000; window.Top = -30000;
        window.Width = 1100; window.Height = 700;
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.Show();
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(1100, 700)); content.Arrange(new Rect(0, 0, 1100, 700)); content.UpdateLayout();
        var visual = new DrawingVisual();
        using (var draw = visual.RenderOpen())
        {
            draw.DrawRectangle(Ui.Brush("#202020"), null, new Rect(0, 0, 1600, 900));
            double scale = Math.Min(1560d / content.ActualWidth, 860d / content.ActualHeight);
            double width = content.ActualWidth * scale, height = content.ActualHeight * scale;
            draw.DrawRectangle(new VisualBrush(content), null, new Rect((1600 - width) / 2, (900 - height) / 2, width, height));
        }
        var image = new RenderTargetBitmap(1600, 900, 96, 96, PixelFormats.Pbgra32);
        image.Render(visual); image.Freeze(); CaptureService.Save(image, path);
        window.Hide();
    }
}
