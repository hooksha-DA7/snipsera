using System;
using System.IO;
using System.Text.Json;

namespace Framelet;

public sealed class PreviewPreferences
{
    private sealed record Options(bool DismissAfterSharing = true);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;
    public bool DismissAfterSharing { get; private set; } = true;
    public string? Notice { get; private set; }
    public event Action? Changed;

    public PreviewPreferences(string dataRoot)
    {
        path = Path.Combine(Path.GetFullPath(dataRoot), "preview-preferences.json");
        try
        {
            if (!File.Exists(path)) return;
            if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
            var options = JsonSerializer.Deserialize<Options>(File.ReadAllText(path), Json) ?? throw new InvalidDataException();
            DismissAfterSharing = options.DismissAfterSharing;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { Notice = "Preview settings could not be read. Dismiss after sharing is on; choose the setting again to save it."; }
    }

    public void SetDismissAfterSharing(bool enabled)
    {
        if (enabled == DismissAfterSharing && Notice is null) return;
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Options(enabled), Json));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { throw new IOException("Preview settings could not be saved. Check that Snipsera's settings folder is writable, then try again.", ex); }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        DismissAfterSharing = enabled; Notice = null; Changed?.Invoke();
    }
}
