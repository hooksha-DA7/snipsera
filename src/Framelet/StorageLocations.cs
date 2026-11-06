using System;
using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Framelet;

public sealed class StorageLocations
{
    private sealed record Options(string? Images = null, string? Videos = null, bool AutomaticImages = true);
    private readonly string settingsPath, defaultImages, defaultVideos;
    private Options options = new();
    public string ImageFolder => options.Images ?? defaultImages;
    public string VideoFolder => options.Videos ?? defaultVideos;
    public bool AutomaticImageSaving => options.AutomaticImages;
    public string? Notice { get; private set; }
    public event Action? Changed;

    public StorageLocations(string dataRoot, string? picturesFolder = null)
    {
        string root = Path.GetFullPath(dataRoot);
        settingsPath = Path.Combine(root, "storage.json");
        string pictures = picturesFolder ?? Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        defaultImages = string.IsNullOrWhiteSpace(pictures) ? Path.Combine(root, "saved-images") : Path.Combine(pictures, AppIdentity.Name);
        defaultVideos = Path.Combine(root, "recordings");
        try
        {
            if (!File.Exists(settingsPath)) return;
            if (new FileInfo(settingsPath).Length > 65536) throw new InvalidDataException("Save-location settings are too large.");
            var loaded = JsonSerializer.Deserialize<Options>(File.ReadAllText(settingsPath)) ?? throw new InvalidDataException("Save-location settings are empty.");
            options = loaded with { Images = Normalize(loaded.Images), Videos = Normalize(loaded.Videos) };
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException)
        { options = new(AutomaticImages: false); Notice = "Save locations could not be read. Default folders are active; automatic screenshot saving is off."; }
    }

    public void SetImageFolder(string? path)
    {
        string? folder = Normalize(path); _ = Writable(folder ?? defaultImages);
        Save(options with { Images = folder });
    }
    public void SetVideoFolder(string? path)
    {
        string? folder = Normalize(path); _ = Writable(folder ?? defaultVideos);
        Save(options with { Videos = folder });
    }
    public void SetAutomaticImageSaving(bool enabled)
    {
        if (enabled) _ = Writable(ImageFolder);
        Save(options with { AutomaticImages = enabled });
    }

    private static string? Normalize(string? path)
    {
        if (path is null) return null;
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose a full folder path.");
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    }

    private static string? Writable(string? path)
    {
        string? folder = Normalize(path);
        if (folder is null) return null;
        Directory.CreateDirectory(folder);
        string probe = Path.Combine(folder, ".snipsera-write-test-" + Guid.NewGuid().ToString("N"));
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
        return folder;
    }

    private void Save(Options next)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
        string temporary = settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(next, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, settingsPath, true);
            options = next; Notice = null;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        Changed?.Invoke();
    }

    internal string ImagePath(CaptureEntry entry)
    {
        if (!Guid.TryParseExact(entry.Id, "N", out _)) throw new InvalidDataException("Invalid capture identifier.");
        return Path.Combine(ImageFolder, $"Snipsera-{entry.Created.LocalDateTime:yyyy-MM-dd-HHmmss}-{entry.Id}.png");
    }

    internal void SaveCapture(BitmapSource image, CaptureEntry entry)
    {
        if (!AutomaticImageSaving) return;
        Directory.CreateDirectory(ImageFolder);
        CaptureService.Save(image, ImagePath(entry));
    }

    internal string NewRecordingPath(string id, RecordingFormat format)
    {
        Directory.CreateDirectory(VideoFolder);
        // Probe before recording starts, so a read-only destination cannot waste a whole recording.
        _ = Writable(VideoFolder);
        return Path.Combine(VideoFolder, "Snipsera-" + id + (format == RecordingFormat.Gif ? ".gif" : ".mp4"));
    }
}
