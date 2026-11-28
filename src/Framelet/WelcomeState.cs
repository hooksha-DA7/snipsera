using System;
using System.IO;
using System.Text.Json;

namespace Framelet;

// Remembers whether Home's first-run welcome card was dismissed. A fresh install shows it once; an existing install
// with captures in History is treated as already welcomed, so upgrades never see it.
public sealed class WelcomeState
{
    private sealed record Options(bool Dismissed = true);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string path;
    public bool ShouldShow { get; private set; }
    public event Action? Changed;

    public WelcomeState(string dataRoot, bool hasHistory)
    {
        path = Path.Combine(Path.GetFullPath(dataRoot), "welcome.json");
        try
        {
            if (File.Exists(path))
            {
                if (new FileInfo(path).Length > 4096) throw new InvalidDataException();
                var options = JsonSerializer.Deserialize<Options>(File.ReadAllText(path), Json) ?? throw new InvalidDataException();
                ShouldShow = !options.Dismissed;
                return;
            }
            if (hasHistory) { TryWrite(); return; }
            ShouldShow = true;
        }
        // An unreadable file never blocks the app; the card simply stays hidden.
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or ArgumentException)
        { ShouldShow = false; }
    }

    public void Dismiss()
    {
        if (!ShouldShow) return;
        ShouldShow = false;
        TryWrite();
        Changed?.Invoke();
    }

    private void TryWrite()
    {
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, JsonSerializer.Serialize(new Options(true), Json));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Shown again next start at worst. */ }
        finally { try { if (File.Exists(temporary)) File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }
}
