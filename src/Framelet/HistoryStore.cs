using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace Framelet;

public sealed class HistoryStore
{
    public const int Capacity = 100;
    public string Root { get; }
    public string Images => Path.Combine(Root, "captures");
    public List<CaptureEntry> Entries { get; private set; } = [];
    public string? Notice { get; private set; }
    private string Index => Path.Combine(Root, "history.json");
    private readonly JsonSerializerOptions json = new() { WriteIndented = true };

    public HistoryStore(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Images);
        Load();
    }

    public string ImagePath(CaptureEntry entry)
    {
        if (!Guid.TryParseExact(entry.Id, "N", out _)) throw new InvalidDataException("Invalid capture identifier.");
        return Path.Combine(Images, entry.Id + ".png");
    }

    private List<CaptureEntry> ReadIndex(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024) throw new InvalidDataException("History index is too large.");
        var entries = JsonSerializer.Deserialize<List<CaptureEntry>>(File.ReadAllText(path), json) ?? throw new InvalidDataException("History is empty.");
        if (entries.Count > 10000 || entries.Select(e => e.Id).Distinct().Count() != entries.Count) throw new InvalidDataException("History index is inconsistent.");
        foreach (var entry in entries)
        {
            _ = ImagePath(entry);
            if (entry.Width < 1 || entry.Height < 1 || (long)entry.Width * entry.Height > 100_000_000 || !Enum.TryParse<CaptureKind>(entry.Mode, out var mode) || !Enum.IsDefined(mode))
                throw new InvalidDataException("Invalid history metadata.");
        }
        return entries.Where(e => File.Exists(ImagePath(e))).OrderByDescending(e => e.Created).Take(Capacity).ToList();
    }

    private void Load()
    {
        if (!File.Exists(Index)) { RecoverImages(); return; }
        try { Entries = ReadIndex(Index); RecoverImages(); }
        catch (Exception ex) when (ex is JsonException or IOException or InvalidDataException or ArgumentException or NullReferenceException)
        {
            string preserved = Index + ".damaged-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
            File.Copy(Index, preserved, false);
            if (File.Exists(Index + ".bak"))
            {
                try { Entries = ReadIndex(Index + ".bak"); }
                catch (Exception backupError) when (backupError is JsonException or IOException or InvalidDataException or ArgumentException or NullReferenceException) { Entries = []; }
            }
            RecoverImages();
            Notice = "History was recovered from local files. The damaged index was preserved.";
        }
    }

    private void RecoverImages()
    {
        var known = Entries.Select(e => e.Id).ToHashSet();
        foreach (string file in Directory.EnumerateFiles(Images, "*.png").OrderByDescending(File.GetLastWriteTimeUtc).Take(Capacity))
        {
            string id = Path.GetFileNameWithoutExtension(file);
            if (!Guid.TryParseExact(id, "N", out _) || known.Contains(id)) continue;
            try
            {
                using var source = File.OpenRead(file);
                var decoder = BitmapDecoder.Create(source, BitmapCreateOptions.None, BitmapCacheOption.None);
                var frame = decoder.Frames[0];
                if ((long)frame.PixelWidth * frame.PixelHeight > 100_000_000) continue;
                Entries.Add(new CaptureEntry(id, File.GetLastWriteTimeUtc(file), "Area", frame.PixelWidth, frame.PixelHeight));
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or ArgumentException) { }
        }
        Entries = Entries.OrderByDescending(e => e.Created).Take(Capacity).ToList();
    }

    private void Commit(List<CaptureEntry> entries)
    {
        string temporary = Index + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, entries, json); stream.Flush(true); }
            if (File.Exists(Index)) File.Copy(Index, Index + ".bak", true);
            File.Move(temporary, Index, true);
            Entries = entries;
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public CaptureEntry Add(BitmapSource image, CaptureKind kind)
    {
        var entry = new CaptureEntry(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, kind.ToString(), image.PixelWidth, image.PixelHeight);
        string path = ImagePath(entry);
        CaptureService.Save(image, path);
        var expired = Entries.Skip(Capacity - 1).ToList();
        try { Commit(new[] { entry }.Concat(Entries).Take(Capacity).ToList()); }
        catch { File.Delete(path); throw; }
        foreach (var old in expired) TryDeleteImage(old);
        return entry;
    }

    public void Remove(CaptureEntry entry)
    {
        Commit(Entries.Where(e => e.Id != entry.Id).ToList());
        TryDeleteImage(entry);
    }

    private void TryDeleteImage(CaptureEntry entry)
    {
        try { File.Delete(ImagePath(entry)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Notice = "A capture file could not be removed. It remains in the captures folder."; }
    }

    public AppSettings LoadSettings()
    {
        string path = Path.Combine(Root, "settings.json");
        if (!File.Exists(path)) return new AppSettings();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new AppSettings();
            foreach (string binding in settings.Bindings) _ = Hotkey.Parse(binding);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or FormatException or NullReferenceException)
        { Notice = "Shortcut settings could not be read. Defaults are active; save shortcuts to replace them."; return new AppSettings(); }
    }

    public void SaveSettings(AppSettings settings)
    {
        foreach (string binding in settings.Bindings) _ = Hotkey.Parse(binding);
        string path = Path.Combine(Root, "settings.json");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, json));
        File.Move(temporary, path, true);
    }
}
