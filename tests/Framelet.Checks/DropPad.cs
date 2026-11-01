using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Framelet;

internal sealed class DropPad : Window
{
    public DropPad(string output)
    {
        Title = "Framelet native drag target"; WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; Background = Ui.Brush("#31475E"); AllowDrop = true;
        var root = new Grid();
        root.Children.Add(new System.Windows.Shapes.Ellipse { Fill = Ui.Brush("#2849A7BF"), Width = 1400, Height = 1400, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-500, 0, 0, -700) });
        var stack = new StackPanel { Width = 270, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 34, 0) };
        stack.Children.Add(Ui.Text("Drag & drop", 27, Brushes.White, true));
        var result = Ui.Text("Drop a capture here\nto test the native PNG transfer.", 16, Ui.Brush("#D4E1EF")); result.Margin = new Thickness(0, 14, 0, 20); stack.Children.Add(result);
        var close = Ui.Button("Close test backdrop", "CloseDropPad", Close); close.Foreground = Brushes.White; stack.Children.Add(close); root.Children.Add(stack); Content = root;
        Loaded += (_, _) =>
        {
            var area = Native.MonitorAtCursor(true); var handle = new WindowInteropHelper(this).Handle;
            Native.SetWindowPos(handle, IntPtr.Zero, area.X, area.Y, area.Width, area.Height, 0x0010);
        };
        DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        Drop += (_, e) =>
        {
            try
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
                if (files.Length != 1) throw new InvalidDataException("Drop one PNG.");
                var image = EditFiles.LoadImage(files[0]);
                var receipt = new { status = "passed", format = "FileDrop", image.PixelWidth, image.PixelHeight, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(files[0]))).ToLowerInvariant(), sourceStillAvailable = File.Exists(files[0]) };
                File.WriteAllText(Path.Combine(output, "native-drop.json"), JsonSerializer.Serialize(receipt, new JsonSerializerOptions { WriteIndented = true }));
                result.Text = $"Received {image.PixelWidth} × {image.PixelHeight} PNG\nNative file drop succeeded.";
            }
            catch (Exception ex) { result.Text = ex.Message; }
            e.Handled = true;
        };
    }
}
