using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Framelet;

internal sealed class RecordingStore
{
    internal string Root { get; }
    private string Index => Path.Combine(Root, "index.json");
    private string Settings => Path.Combine(Root, "settings.json");
    private string Folders => Path.Combine(Root, "output-folders.json");
    private readonly HashSet<string> outputFolders = new(StringComparer.OrdinalIgnoreCase);
    internal IReadOnlyList<RecordingResult> Entries { get; private set; } = [];
    internal RecordingStore(string historyRoot)
    {
        Root = Path.Combine(Path.GetFullPath(historyRoot), "recordings");
        Directory.CreateDirectory(Root);
        outputFolders.Add(Root);
        try
        {
            if (File.Exists(Folders) && new FileInfo(Folders).Length < 65536)
                foreach (string folder in JsonSerializer.Deserialize<string[]>(File.ReadAllText(Folders)) ?? [])
                    if (Path.IsPathFullyQualified(folder)) outputFolders.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or NotSupportedException) { }
        try
        {
            if (File.Exists(Index) && new FileInfo(Index).Length < 1024 * 1024)
                Entries = (JsonSerializer.Deserialize<List<RecordingResult>>(File.ReadAllText(Index)) ?? [])
                    .Where(e => IsRecording(e) && File.Exists(e.FilePath) && e.Width > 0 && e.Height > 0 && e.Duration >= TimeSpan.Zero)
                    .OrderByDescending(e => e.Created).Take(100).ToArray();
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or NotSupportedException) { }
    }
    internal bool Owns(string path) => Path.GetDirectoryName(Path.GetFullPath(path))?.Equals(Root, StringComparison.OrdinalIgnoreCase) == true;
    internal bool IsRecording(RecordingResult result) => Enum.IsDefined(result.Format) && Path.IsPathFullyQualified(result.FilePath) &&
        outputFolders.Contains(Path.GetDirectoryName(Path.GetFullPath(result.FilePath)) ?? "") &&
        Path.GetExtension(result.FilePath).Equals(result.Format == RecordingFormat.Gif ? ".gif" : ".mp4", StringComparison.OrdinalIgnoreCase);
    internal RecordingOptions LoadOptions()
    {
        try
        {
            if (File.Exists(Settings) && new FileInfo(Settings).Length < 65536)
            {
                var options = JsonSerializer.Deserialize<RecordingOptions>(File.ReadAllText(Settings)) ?? new();
                options.Validate(); return options;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException) { }
        return new();
    }
    internal void SaveOptions(RecordingOptions options) { options.Validate(); AtomicWrite(Settings, options); }
    internal void Add(RecordingResult result, string? outputFolder = null)
    {
        if (outputFolder is not null)
        {
            string folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputFolder));
            if (!Path.GetDirectoryName(Path.GetFullPath(result.FilePath))!.Equals(folder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The recording does not match its selected output folder.");
            if (!outputFolders.Contains(folder))
            {
                AtomicWrite(Folders, outputFolders.Append(folder).ToArray());
                outputFolders.Add(folder);
            }
        }
        if (!IsRecording(result)) throw new InvalidOperationException("The recording is outside its managed output folder or has an unsupported format.");
        var entries = new[] { result }.Concat(Entries).Take(100).ToArray();
        AtomicWrite(Index, entries); Entries = entries;
    }
    // Final publication never overwrites a recording. Across volumes, copy to an owned staging file before exposing the final name.
    internal static void Publish(string source, string destination)
    {
        if (File.Exists(destination)) throw new IOException("The recording output already exists.");
        try { File.Move(source, destination, false); return; }
        catch (IOException) when (File.Exists(source) && !File.Exists(destination)) { }
        string staging = Path.Combine(Path.GetDirectoryName(destination)!, ".snipsera-publish-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { input.CopyTo(output); output.Flush(true); }
            File.Move(staging, destination, false);
            File.Delete(source);
        }
        finally { if (File.Exists(staging)) File.Delete(staging); }
    }
    private static void AtomicWrite<T>(string path, T value)
    {
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
