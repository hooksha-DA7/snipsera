using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Framelet;

public static class EditFiles
{
    private const long ImageLimit = 160L * 1024 * 1024;
    private const long JsonLimit = 4L * 1024 * 1024;
    private sealed record ProjectData(int Version, int OriginalWidth, int OriginalHeight, EditState State);
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true, MaxDepth = 16, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };

    public static BitmapSource LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > ImageLimit) throw new InvalidDataException("Choose a PNG or JPEG smaller than 160 MB.");
        return ReadImage(stream);
    }
    private static BitmapSource ReadImage(Stream stream)
    {
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.None);
        if (decoder is not PngBitmapDecoder and not JpegBitmapDecoder) throw new InvalidDataException("Choose a PNG or JPEG image.");
        var frame = decoder.Frames[0]; EditDocument.ValidateSize(frame.PixelWidth, frame.PixelHeight);
        int orientation = 1;
        if (decoder is JpegBitmapDecoder && frame.Metadata is BitmapMetadata metadata)
        {
            object? value = metadata.GetQuery("/app1/ifd/{ushort=274}");
            if (value is ushort number && number is >= 1 and <= 8) orientation = number;
        }
        BitmapSource image = new WriteableBitmap(frame);
        var transforms = new TransformGroup();
        if (orientation is 2 or 4 or 5 or 7) transforms.Children.Add(new ScaleTransform(-1, 1));
        int angle = orientation switch { 3 or 4 => 180, 5 or 8 => 270, 6 or 7 => 90, _ => 0 };
        if (angle != 0) transforms.Children.Add(new RotateTransform(angle));
        if (transforms.Children.Count > 0) image = new TransformedBitmap(image, transforms);
        image.Freeze(); return image;
    }

    public static void SaveProject(EditDocument document, string path)
    {
        EditDocument.Validate(document.State, document.Original.PixelWidth, document.Original.PixelHeight);
        byte[] metadata = JsonSerializer.SerializeToUtf8Bytes(new ProjectData(2, document.Original.PixelWidth, document.Original.PixelHeight, document.State), Json);
        if (metadata.Length > JsonLimit) throw new InvalidDataException("This project has too much annotation data. Split it into smaller projects.");
        using var png = new MemoryStream();
        CaptureService.WritePng(document.Original, png);
        if (png.Length > ImageLimit) throw new InvalidDataException("This project's original image exceeds 160 MB.");
        AtomicWrite(path, stream =>
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create, true);
            using (var json = archive.CreateEntry("project.json", CompressionLevel.Optimal).Open()) json.Write(metadata);
            using var original = archive.CreateEntry("original.png", CompressionLevel.NoCompression).Open();
            png.Position = 0; png.CopyTo(original);
        });
        document.MarkSaved();
    }
    public static EditDocument LoadProject(string path)
    {
        using var source = File.OpenRead(path);
        if (source.Length > ImageLimit + JsonLimit + 65536) throw new InvalidDataException("This project exceeds the supported file size.");
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        if (archive.Entries.Count != 2 || archive.Entries.Select(e => e.FullName).Distinct().Count() != 2 ||
            archive.Entries.Any(e => e.FullName != "project.json" && e.FullName != "original.png"))
            throw new InvalidDataException("This project must contain only project.json and original.png.");
        using var json = ReadEntry(archive.GetEntry("project.json")!, JsonLimit);
        var data = JsonSerializer.Deserialize<ProjectData>(json, Json) ?? throw new InvalidDataException("The project is empty.");
        if (data.Version is not (1 or 2) || data.State is null) throw new InvalidDataException("This project version is not supported.");
        EditDocument.ValidateSize(data.OriginalWidth, data.OriginalHeight);
        EditDocument.Validate(data.State, data.OriginalWidth, data.OriginalHeight);
        using var png = ReadEntry(archive.GetEntry("original.png")!, ImageLimit);
        var original = ReadImage(png);
        if (original.PixelWidth != data.OriginalWidth || original.PixelHeight != data.OriginalHeight)
            throw new InvalidDataException("The project image dimensions do not match its metadata.");
        return new EditDocument(original, data.State);
    }
    private static MemoryStream ReadEntry(ZipArchiveEntry entry, long maximum)
    {
        if (entry.Length > maximum) throw new InvalidDataException("A project component exceeds its supported size.");
        using var stream = entry.Open();
        var output = new MemoryStream(); byte[] buffer = new byte[65536];
        try
        {
            int read;
            while ((read = stream.Read(buffer)) > 0)
            { if (output.Length + read > maximum) throw new InvalidDataException("A project component exceeds its supported size."); output.Write(buffer, 0, read); }
            output.Position = 0; return output;
        }
        catch { output.Dispose(); throw; }
    }
    public static void Export(EditDocument document, string path, bool jpeg)
    {
        var image = EditRenderer.Render(document);
        AtomicWrite(path, stream =>
        {
            if (!jpeg) { CaptureService.WritePng(image, stream); return; }
            // JPEG has no alpha; composite transparent padding on white explicitly.
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) { dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight)); }
            var flat = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32); flat.Render(visual);
            var encoder = new JpegBitmapEncoder { QualityLevel = 95 }; encoder.Frames.Add(BitmapFrame.Create(flat)); encoder.Save(stream);
        });
    }
    private static void AtomicWrite(string path, Action<Stream> write)
    {
        string full = Path.GetFullPath(path), temporary = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { write(stream); stream.Flush(true); }
            File.Move(temporary, full, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
